using Microsoft.AspNetCore.Mvc;

public class UsersController : Controller
{
    private static readonly List<string> Users = new() { "Ada", "Linus" };

    [HttpGet]
    public IActionResult List()
        => PartialView("_List", Users);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) Users.Add(name);
        return PartialView("_List", Users); // return the updated fragment
    }

    [HttpGet]
    public IActionResult Form()
        => PartialView("_Form");
}
