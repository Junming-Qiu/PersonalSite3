using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

public class HomeController : Controller
{
    private readonly IGithubPoller poller;
    private object experiences;

    public HomeController(IGithubPoller poller)
    {
        this.poller = poller!;

        using (StreamReader r = new StreamReader("./wwwroot/content/experiences.json"))
        {
            string data = r.ReadToEnd();
            var converter = new ExpandoObjectConverter();
            experiences = JsonConvert.DeserializeObject<List<dynamic>>(data.ToString(), converter)!;
        }

    }
    public IActionResult Index()
    {
        // var repos = await poller.GetRepositories();
        return View(experiences);
    }

    public IActionResult Blog()
    {
        return View();
    }

}
