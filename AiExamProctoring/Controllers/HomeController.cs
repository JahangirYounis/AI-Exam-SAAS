using System.Diagnostics;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Mvc;

namespace AiExamProctoring.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult About() => View();

    [HttpGet]
    public IActionResult HowItWorks() => View();

    [HttpGet]
    public IActionResult Features() => View();

    [HttpGet]
    public IActionResult Faq() => View();

    [HttpGet]
    public IActionResult Contact() => View();

    [HttpGet]
    public IActionResult Privacy() => View();

    // Status-code re-execution preserves the original verb, including POST.
    // This action only renders an error and never changes application state.
    [IgnoreAntiforgeryToken]
    public IActionResult Status(int code)
    {
        code = code is >= 400 and <= 599 ? code : StatusCodes.Status404NotFound;
        Response.StatusCode = code;
        ViewBag.StatusCode = code;
        ViewBag.StatusTitle = code switch
        {
            StatusCodes.Status404NotFound => "Page not found",
            StatusCodes.Status403Forbidden => "Access denied",
            StatusCodes.Status401Unauthorized => "Sign in required",
            _ => "Request could not be completed"
        };
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
