using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Areas.Identity.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IList<AuthenticationScheme> ExternalLogins { get; private set; }
        = new List<AuthenticationScheme>();

    public string ReturnUrl { get; private set; } = "~/";

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember Me")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
            ModelState.AddModelError(string.Empty, ErrorMessage);

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ExternalLogins = (await _signInManager
            .GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);

        ExternalLogins = (await _signInManager
            .GetExternalAuthenticationSchemesAsync()).ToList();

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email.Trim());

        if (user is null)
        {
            AddLoginError();
            return Page();
        }

        if (user.IsBlocked)
        {
            ModelState.AddModelError(string.Empty,
                "Your account has been blocked by the administrator.");
            return Page();
        }

        var isStudent = await _userManager.IsInRoleAsync(user, "Student");
        var isTeacher = await _userManager.IsInRoleAsync(user, "Teacher");

        if (isTeacher && !user.IsApproved)
        {
            ModelState.AddModelError(string.Empty,
                "Your teacher account is waiting for administrator approval.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName ?? user.Email!,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Upgrade legacy student registrations only after the password has
            // been verified. A failed sign-in must never mutate an account.
            if (isStudent && !user.IsApproved)
            {
                user.IsApproved = true;
                var approvalUpdate = await _userManager.UpdateAsync(user);
                if (!approvalUpdate.Succeeded)
                {
                    await _signInManager.SignOutAsync();
                    ModelState.AddModelError(string.Empty, "Your student account could not be activated automatically. Please try again.");
                    return Page();
                }
            }

            user.LastLoginDate = DateTime.Now;
            await _userManager.UpdateAsync(user);

            _logger.LogInformation("User {Email} logged in successfully.", user.Email);

            if (await _userManager.IsInRoleAsync(user, "Admin"))
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

            if (await _userManager.IsInRoleAsync(user, "Teacher"))
                return RedirectToAction("Index", "Dashboard", new { area = "Teacher" });

            if (await _userManager.IsInRoleAsync(user, "Student"))
            {
                if (ReturnUrl != Url.Content("~/")) return LocalRedirect(ReturnUrl);
                return RedirectToAction("Index", "Dashboard", new { area = "Student" });
            }

            await _signInManager.SignOutAsync();
            ModelState.AddModelError(string.Empty,
                "No valid role has been assigned to your account. Please contact the administrator.");
            return Page();
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new
            {
                ReturnUrl,
                RememberMe = Input.RememberMe
            });
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User account {Email} was locked out.", user.Email);
            return RedirectToPage("./Lockout");
        }

        AddLoginError();
        return Page();
    }

    private string GetSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/")!;

    private void AddLoginError()
        => ModelState.AddModelError(string.Empty, "Invalid email or password.");
}
