using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

public class GalleryController : Controller
{
    private readonly string cdnBase;

    public GalleryController(IConfiguration config)
    {
        cdnBase = config["Gallery:CdnBase"] ?? "";
    }

    [Route("gallery")]
    public IActionResult Index()
    {
        return View();
    }

    [Route("gallery/data")]
    public IActionResult Data()
    {
        return Json(MockFrames().Select(f => new
        {
            f.Hash,
            Thumb = $"{cdnBase}/{f.ThumbKey}",
            Full = $"{cdnBase}/{f.FullKey}",
            Med = f.MedKey == null ? null : $"{cdnBase}/{f.MedKey}",
            MedW = f.MedKey == null ? (int?)null : 2400,
            W = f.Portrait ? 1600 : 5184,
            H = f.Portrait ? 2400 : 3456,
            f.Date,
            f.Location,
            f.Description,
            f.Film,
            f.Push,
        }));
    }

    private record MockFrame(string Hash, bool Portrait, string? Date, string? Location, string? Description, string? Film, double? Push)
    {
        // portrait thumb is the full image rotated clockwise, same as ingest will do
        public string ThumbKey => "mock_img_thumb.jpg";
        // mid-size popup rendition (2400px long edge) so phones don't decode the 8MB original
        public string? MedKey => Portrait ? null : "mock_img_med.jpg";
        public string FullKey => Portrait ? "mock_img_portrait.jpg" : "mock_img.jpg";
    }

    // mock data until gallery.db exists
    private static IEnumerable<MockFrame> MockFrames()
    {
        var captions = new[]
        {
            "Low tide at the stone wall, grass already turning.",
            "Overcast, ISO 400, nothing but wind.",
            "Walked out here after the rain stopped. The light lasted about ten minutes before the clouds closed back in, and I only had four frames left on the roll.",
            "The hill in the distance is a landfill, which I did not know until later.",
            null,
            "Shot from a crouch, grass brushing the lens.",
        };
        var films = new (string? Film, double? Push)[]
        {
            ("Kodak Portra 400", 0),
            ("Kodak Portra 400", 1),
            ("Ilford HP5 Plus 400", 2),
            ("Fujifilm Superia 400", -1),
            ("Kodak Gold 200", 0.5),
            (null, null),
        };
        var specs = new (string? Location, string? Month, int Count)[]
        {
            ("Kyoto, Japan", "2026-10", 14),
            ("Brooklyn, NY", "2026-10", 5),
            (null, "2026-10", 3),
            ("Banff, Canada", "2026-08", 11),
            (null, "2026-08", 2),
            ("Lisbon, Portugal", "2026-05", 9),
            ("Brooklyn, NY", "2026-03", 7),
            ("Lisbon, Portugal", "2025-12", 4),
            ("Banff, Canada", null, 2),
        };

        var i = 0;
        foreach (var (location, month, count) in specs)
        {
            for (var j = 0; j < count; j++, i++)
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"mock{i}")))[..16].ToLower();
                var date = month == null ? null : $"{month}-{1 + j * 2 % 27:00}T{10 + j % 8:00}:{j * 7 % 60:00}:00";
                yield return new MockFrame(hash, i % 5 == 3, date, location, captions[i % captions.Length], films[i / 3 % films.Length].Film, films[i / 3 % films.Length].Push);
            }
        }
    }
}
