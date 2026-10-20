using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Data.Sqlite;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

static class Bucket
{
    public static (AmazonS3Client, string) Open() => (
        new AmazonS3Client(
            Environment.GetEnvironmentVariable("BUCKET_ACCESS_KEY_ID"),
            Environment.GetEnvironmentVariable("BUCKET_WRITE_KEY"),
            new AmazonS3Config { ServiceURL = "https://nyc3.digitaloceanspaces.com" }),
        new Uri(Environment.GetEnvironmentVariable("BUCKET_ENDPOINT")!).Host.Split('.')[0]);
}

record ScanRow(string Path, string Name, int W, int H, string Ratio, bool Ok, string? Reason);

static class Ingest
{
    const string Mark = "@Junming Qiu";
    static readonly string[] Unsupported = [".png", ".heic", ".heif", ".tif", ".tiff", ".webp", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2"];

    // sqlite connection is shared with the Editor's request threads
    public static readonly object Lock = new();

    public static string Expand(string p) => p.StartsWith("~/") ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]) : p;

    static readonly (string Name, double Value)[] Ratios = [("3:2", 1.5), ("4:3", 4.0 / 3), ("1:1", 1.0)];

    // "3:2" / "4:3" / "1:1" within 1%, else the plain decimal
    static string RatioLabel(int w, int h)
    {
        var ratio = (double)Math.Max(w, h) / Math.Min(w, h);
        foreach (var (name, v) in Ratios) if (Math.Abs(ratio - v) / v <= 0.01) return name;
        return $"{ratio:0.00}:1";
    }

    // why this file can't be ingested, or null; also returns pixel size and hash when readable
    static (string? Reason, int W, int H, string Ratio, string Hash) Check(SqliteConnection db, string file)
    {
        var ext = Path.GetExtension(file).ToLower();
        if (ext is not (".jpg" or ".jpeg")) return ($"{ext} not supported, JPEG only", 0, 0, "", "");
        int w, h;
        try { (w, h) = Size(file); }
        catch (Exception) { return ("unreadable JPEG", 0, 0, "", ""); }
        var ratio = RatioLabel(w, h);
        if (!Ratios.Any(x => x.Name == ratio)) return ($"ratio {ratio} not supported, need 3:2, 4:3 or 1:1", w, h, ratio, "");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))[..16].ToLower();
        lock (Lock)
        {
            using var q = db.CreateCommand();
            q.CommandText = "SELECT 1 FROM frames WHERE hash = $h";
            q.Parameters.AddWithValue("$h", hash);
            if (q.ExecuteScalar() != null) return ("already ingested", w, h, ratio, hash);
        }
        return (null, w, h, ratio, hash);
    }

    public static List<string> Candidates(string folder) =>
        System.IO.Directory.EnumerateFiles(folder).Order()
            .Where(f => Path.GetExtension(f).ToLower() is ".jpg" or ".jpeg" || Unsupported.Contains(Path.GetExtension(f).ToLower())).ToList();

    // lazy so the caller can report progress as each file is checked
    public static IEnumerable<ScanRow> Scan(SqliteConnection db, IEnumerable<string> files)
    {
        var seen = new Dictionary<string, string>();
        foreach (var file in files)
        {
            var (reason, w, h, ratio, hash) = Check(db, file);
            if (reason == null && !seen.TryAdd(hash, Path.GetFileName(file))) reason = $"duplicate of {seen[hash]}";
            yield return new ScanRow(file, Path.GetFileName(file), w, h, ratio, reason == null, reason);
        }
    }

    // returns the new Frame's hash; throws InvalidOperationException with the reason if the file isn't acceptable
    public static async Task<string> One(SqliteConnection db, string file)
    {
        var (reason, _, _, _, hash) = Check(db, file);
        if (reason != null) throw new InvalidOperationException(reason);

        var (s3, bucket) = Bucket.Open();
        var tmp = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var date = ReadDate(file);
            var full = Path.Combine(tmp, "full.jpg");
            var med = Path.Combine(tmp, "med.jpg");
            var thumb = Path.Combine(tmp, "thumb.jpg");

            // ffmpeg drops all metadata; icc profile and watermark are re-added below. ponytail: EXIF orientation ignored (ffmpeg 4.2), add a transpose if a photo ever comes in sideways
            Ffmpeg(file, full, "null");
            var (w, h) = Size(full);
            var scale = Math.Min(1.0, 2400.0 / Math.Max(w, h));
            var medW = (int)Math.Round(w * scale) / 2 * 2;
            Ffmpeg(file, med, $"scale={medW}:-2");
            Ffmpeg(file, thumb, (w >= h ? "" : "transpose=1,") + "scale=720:480:force_original_aspect_ratio=decrease,pad=720:480:(ow-iw)/2:(oh-ih)/2:color=0x1b1b1d");
            var icc = IccSegments(file).ToList();
            foreach (var f in new[] { full, med, thumb }) Tag(f, icc);

            foreach (var (kind, path) in new[] { ("full", full), ("med", med), ("thumb", thumb) })
                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket, Key = $"{kind}/{hash}.jpg", FilePath = path, ContentType = "image/jpeg",
                    CannedACL = S3CannedACL.PublicRead, Headers = { CacheControl = "public, max-age=31536000, immutable" },
                    DisablePayloadSigning = true, DisableDefaultChecksumValidation = true,
                });

            lock (Lock)
            {
                using var ins = db.CreateCommand();
                ins.CommandText = "INSERT INTO frames (hash, width, height, med_width, date) VALUES ($h, $w, $ht, $m, $d)";
                ins.Parameters.AddWithValue("$h", hash);
                ins.Parameters.AddWithValue("$w", w);
                ins.Parameters.AddWithValue("$ht", h);
                ins.Parameters.AddWithValue("$m", medW);
                ins.Parameters.AddWithValue("$d", (object?)date ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
            return hash;
        }
        finally { System.IO.Directory.Delete(tmp, true); }
    }

    static string? ReadDate(string file) =>
        ImageMetadataReader.ReadMetadata(file).OfType<ExifSubIfdDirectory>().FirstOrDefault()
            ?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var d) == true ? d.ToString("yyyy-MM-ddTHH:mm:ss") : null;

    static (int, int) Size(string file)
    {
        var j = ImageMetadataReader.ReadMetadata(file).OfType<JpegDirectory>().First();
        return (j.GetImageWidth(), j.GetImageHeight());
    }

    static void Ffmpeg(string input, string output, string vf)
    {
        var p = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
        foreach (var a in new[] { "-y", "-loglevel", "error", "-i", input, "-map_metadata", "-1", "-vf", vf, "-q:v", "2", output }) p.ArgumentList.Add(a);
        using var proc = Process.Start(p)!;
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new Exception($"ffmpeg failed on {input}: {err}");
    }

    // JPEG segments: FFD8, then (FF marker, 2-byte length incl. itself, payload) until SOS (FFDA)
    static IEnumerable<byte[]> IccSegments(string file)
    {
        var b = File.ReadAllBytes(file);
        for (var i = 2; i + 4 < b.Length && b[i] == 0xFF && b[i + 1] != 0xDA;)
        {
            var len = b[i + 2] << 8 | b[i + 3];
            if (b[i + 1] == 0xE2 && Encoding.ASCII.GetString(b, i + 4, 11) == "ICC_PROFILE") yield return b[i..(i + 2 + len)];
            i += 2 + len;
        }
    }

    // ponytail: the watermark is a JPEG COM comment, not EXIF Artist/Copyright (ffmpeg can't write EXIF); swap for exiftool if real EXIF tags are needed
    static void Tag(string file, IEnumerable<byte[]> icc)
    {
        var b = File.ReadAllBytes(file);
        var text = Encoding.UTF8.GetBytes($"Artist: {Mark}; Copyright: {Mark}");
        var com = new byte[] { 0xFF, 0xFE, (byte)((text.Length + 2) >> 8), (byte)(text.Length + 2) }.Concat(text);
        File.WriteAllBytes(file, b[..2].Concat(com).Concat(icc.SelectMany(s => s)).Concat(b[2..]).ToArray());
    }
}
