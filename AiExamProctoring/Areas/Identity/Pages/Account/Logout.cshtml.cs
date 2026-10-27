using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AiExamProctoring.Areas.Identity.Pages.Account;

public sealed class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out successfully.");

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Account/Login");
    }
}
