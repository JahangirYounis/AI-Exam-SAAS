using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace AiExamProctoring.Areas.Identity.Pages.Account;

public sealed class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender)
    {
        _userManager = userManager;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email.Trim());

        // Always use the same response to avoid account enumeration.
        if (user is null ||
            !await _userManager.IsEmailConfirmedAsync(user) ||
            user.IsBlocked)
        {
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var callbackUrl = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { area = "Identity", code = encodedToken },
            protocol: Request.Scheme);

        var safeUrl = HtmlEncoder.Default.Encode(callbackUrl!);
        var safeName = HtmlEncoder.Default.Encode(user.FullName);

        await _emailSender.SendEmailAsync(
            user.Email!,
            "AI Based Online Examination System - Reset Password",
            $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:auto">
                <h2>AI Based Online Examination System</h2>
                <p>Hello {safeName},</p>
                <p>We received a request to reset your password.</p>
                <p>
                    <a href="{safeUrl}"
                       style="display:inline-block;padding:12px 20px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">
                        Reset Password
                    </a>
                </p>
                <p>If you did not request this, you can safely ignore this email.</p>
                <hr>
                <small>AI Based Online Examination System</small>
            </div>
            """);

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
