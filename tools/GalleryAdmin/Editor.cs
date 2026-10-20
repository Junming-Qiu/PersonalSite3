using System.Diagnostics;
using System.Text.Json;
using Amazon.S3.Model;
using Microsoft.Data.Sqlite;

static class Editor
{
    static readonly string[] Editable = ["date", "location", "film", "push", "description"];

    public static async Task Run(SqliteConnection db, string[] args)
    {
        var cdn = Environment.GetEnvironmentVariable("BUCKET_CDN_ENDPOINT")!.TrimEnd('/');
        var app = WebApplication.CreateBuilder(args).Build();
        app.Urls.Add("http://localhost:5100");
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/frames", () =>
        {
            lock (Ingest.Lock)
            {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT hash, date, location, film, push, description FROM frames ORDER BY date IS NULL, date, hash";
            using var r = c.ExecuteReader();
            var rows = new List<object>();
            while (r.Read())
                rows.Add(new { hash = r.GetString(0), thumb = $"{cdn}/thumb/{r.GetString(0)}.jpg",
                    date = r.IsDBNull(1) ? null : r.GetString(1), location = r.IsDBNull(2) ? null : r.GetString(2),
                    film = r.IsDBNull(3) ? null : r.GetString(3), push = r.IsDBNull(4) ? (double?)null : r.GetDouble(4),
                    description = r.IsDBNull(5) ? null : r.GetString(5) });
            return rows;
            }
        });

        // body: { hashes: [...], fields: { location: "x", push: null } }; only listed fields change
        app.MapPatch("/api/frames", (Patch p) =>
        {
            // month moves each Frame into that month keeping its day (clamped) and time; undated Frames land on the 1st at midnight
            if (p.Fields.TryGetProperty("month", out var m))
            {
                if (!DateTime.TryParseExact(m.GetString() + "-01", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var first) || p.Hashes.Length == 0)
                    return Results.BadRequest();
                lock (Ingest.Lock)
                {
                    using var tx = db.BeginTransaction();
                    foreach (var hash in p.Hashes)
                    {
                        using var get = db.CreateCommand();
                        get.Transaction = tx;
                        get.CommandText = "SELECT date FROM frames WHERE hash = $hash";
                        get.Parameters.AddWithValue("$hash", hash);
                        var old = get.ExecuteScalar() as string;
                        var d = old != null && DateTime.TryParse(old, out var parsed) ? parsed : first;
                        var moved = new DateTime(first.Year, first.Month, Math.Min(d.Day, DateTime.DaysInMonth(first.Year, first.Month))).Add(old == null ? TimeSpan.Zero : d.TimeOfDay);
                        using var set = db.CreateCommand();
                        set.Transaction = tx;
                        set.CommandText = "UPDATE frames SET date = $d WHERE hash = $hash";
                        set.Parameters.AddWithValue("$d", moved.ToString("yyyy-MM-ddTHH:mm:ss"));
                        set.Parameters.AddWithValue("$hash", hash);
                        set.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                return Results.NoContent();
            }
            var fields = p.Fields.EnumerateObject().Where(f => Editable.Contains(f.Name)).ToList();
            if (fields.Count == 0 || p.Hashes.Length == 0) return Results.BadRequest();
            lock (Ingest.Lock)
            {
            using var tx = db.BeginTransaction();
            foreach (var hash in p.Hashes)
            {
                using var c = db.CreateCommand();
                c.Transaction = tx;
                c.CommandText = $"UPDATE frames SET {string.Join(", ", fields.Select(f => $"{f.Name} = ${f.Name}"))} WHERE hash = $hash";
                c.Parameters.AddWithValue("$hash", hash);
                foreach (var f in fields)
                    c.Parameters.AddWithValue($"${f.Name}", f.Value.ValueKind switch
                    {
                        JsonValueKind.Null => DBNull.Value,
                        JsonValueKind.Number => f.Value.GetDouble(),
                        _ => string.IsNullOrWhiteSpace(f.Value.GetString()) ? DBNull.Value : f.Value.GetString()!,
                    });
                c.ExecuteNonQuery();
            }
            tx.Commit();
            }
            return Results.NoContent();
        });

        // bucket objects go first: if that fails the rows stay and the delete can be retried
        app.MapDelete("/api/frames", async ([Microsoft.AspNetCore.Mvc.FromBody] Delete d) =>
        {
            if (d.Hashes.Length == 0) return Results.BadRequest();
            var (s3, bucket) = Bucket.Open();
            await s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = bucket,
                Objects = d.Hashes.SelectMany(h => new[] { "thumb", "med", "full" }.Select(k => new KeyVersion { Key = $"{k}/{h}.jpg" })).ToList(),
            });
            lock (Ingest.Lock)
            {
            using var tx = db.BeginTransaction();
            foreach (var hash in d.Hashes)
            {
                using var c = db.CreateCommand();
                c.Transaction = tx;
                c.CommandText = "DELETE FROM frames WHERE hash = $hash";
                c.Parameters.AddWithValue("$hash", hash);
                c.ExecuteNonQuery();
            }
            tx.Commit();
            }
            return Results.NoContent();
        });

        // native macOS folder picker; empty path means cancelled
        app.MapPost("/api/browse", () =>
        {
            var p = Process.Start(new ProcessStartInfo("osascript", ["-e", "POSIX path of (choose folder with prompt \"Photos to ingest\")"]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return new { path };
        });

        // tiny preview of a not-yet-ingested file; local-only tool, so any readable image path is allowed
        app.MapGet("/api/preview", (string path) =>
        {
            path = Ingest.Expand(path);
            if (!File.Exists(path)) return Results.NotFound();
            var p = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-nostdin", "-loglevel", "error", "-i", path, "-vf", "scale=160:-2", "-frames:v", "1", "-q:v", "5", "-f", "image2pipe", "-c:v", "mjpeg", "-" }) p.ArgumentList.Add(a);
            using var proc = Process.Start(p)!;
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit();
            if (ms.Length == 0) return Results.NotFound();
            return Results.File(ms.ToArray(), "image/jpeg", enableRangeProcessing: false);
        });

        // streams newline-delimited JSON: {"total":N} then one row per file, so the page can show progress
        app.MapPost("/api/scan", async (HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromBody] FolderReq r) =>
        {
            var folder = Ingest.Expand(r.Folder.Trim());
            if (!System.IO.Directory.Exists(folder)) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "folder not found" }); return; }
            var files = Ingest.Candidates(folder);
            ctx.Response.ContentType = "application/x-ndjson";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { total = files.Count }) + "\n");
            foreach (var row in Ingest.Scan(db, files))
            {
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n");
                await ctx.Response.Body.FlushAsync();
            }
        });

        app.MapPost("/api/ingest", async ([Microsoft.AspNetCore.Mvc.FromBody] FileReq r) =>
        {
            try { return Results.Ok(new { hash = await Ingest.One(db, r.Path) }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
        });

        Console.WriteLine("Editor on http://localhost:5100");
        await app.RunAsync();
    }

    record FolderReq(string Folder);
    record FileReq(string Path);
    record Delete(string[] Hashes);
    record Patch(string[] Hashes, JsonElement Fields);
}
