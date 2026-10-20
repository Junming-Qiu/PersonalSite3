using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

public class GalleryController : Controller
{
    private readonly string cdnBase;
    private readonly string dbPath;

    public GalleryController(IConfiguration config, IWebHostEnvironment env)
    {
        cdnBase = config["Gallery:CdnBase"] ?? "";
        dbPath = Path.GetFullPath(config["Gallery:DbPath"] ?? "../gallery.db", env.ContentRootPath);
    }

    [Route("gallery")]
    public IActionResult Index()
    {
        return View();
    }

    [Route("gallery/data")]
    public IActionResult Data()
    {
        var frames = new List<object>();
        // a missing db means an empty gallery
        if (!System.IO.File.Exists(dbPath)) return Json(frames);

        using var db = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT hash, width, height, med_width, date, location, film, push, description FROM frames";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var hash = r.GetString(0);
            frames.Add(new
            {
                Hash = hash,
                Thumb = $"{cdnBase}/thumb/{hash}.jpg",
                Med = $"{cdnBase}/med/{hash}.jpg",
                Full = $"{cdnBase}/full/{hash}.jpg",
                MedW = r.GetInt32(3),
                W = r.GetInt32(1),
                H = r.GetInt32(2),
                Date = r.IsDBNull(4) ? null : r.GetString(4),
                Location = r.IsDBNull(5) ? null : r.GetString(5),
                Film = r.IsDBNull(6) ? null : r.GetString(6),
                Push = r.IsDBNull(7) ? (double?)null : r.GetDouble(7),
                Description = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return Json(frames);
    }
}
