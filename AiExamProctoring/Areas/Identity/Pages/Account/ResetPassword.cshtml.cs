using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AiExamProctoring.Areas.Identity.Pages.Account;

public sealed class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ResetPasswordModel> _logger;

    public ResetPasswordModel(
        UserManager<ApplicationUser> userManager,
        ILogger<ResetPasswordModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6,
            ErrorMessage = "Password must be at least 6 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm Password is required.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password),
            ErrorMessage = "Password and Confirm Password do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? code = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Password reset code is required.");

        try
        {
            Input.Code = Encoding.UTF8.GetString(
                WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return BadRequest("The password reset link is invalid.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email.Trim());

        if (user is null || user.IsBlocked)
            return RedirectToPage("./ResetPasswordConfirmation");

        var result = await _userManager.ResetPasswordAsync(
            user, Input.Code, Input.Password);

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Password reset completed for {Email}.", Input.Email);

            return RedirectToPage("./ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }
}
