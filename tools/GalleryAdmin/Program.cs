using Microsoft.Data.Sqlite;

// usage: dotnet run --project tools/GalleryAdmin
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "PersonalSite.sln")))
    root = Path.GetDirectoryName(root) ?? throw new Exception("run from inside the repo");

foreach (var line in File.Exists(Path.Combine(root, ".env")) ? File.ReadAllLines(Path.Combine(root, ".env")) : [])
{
    var i = line.IndexOf('=');
    if (i > 0 && !line.StartsWith('#'))
        Environment.SetEnvironmentVariable(line[..i].Trim(), line[(i + 1)..].Trim().Trim('"'));
}

var db = new SqliteConnection($"Data Source={Path.Combine(root, "gallery.db")}");
db.Open();
using (var cmd = db.CreateCommand())
{
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS frames (
            hash TEXT PRIMARY KEY, width INTEGER NOT NULL, height INTEGER NOT NULL, med_width INTEGER NOT NULL,
            date TEXT, location TEXT, film TEXT, push REAL, description TEXT);
        """;
    cmd.ExecuteNonQuery();
}

await Editor.Run(db, args);
