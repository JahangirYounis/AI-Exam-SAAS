using AiExamProctoring.Models;
using AiExamProctoring.Services.FaceIdentity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AiExamProctoring.Areas.Identity.Pages.Account;

[RequestSizeLimit(75_000_000)]
[RequestFormLimits(ValueLengthLimit = 72_000_000, MultipartBodyLengthLimit = 75_000_000)]
public sealed class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IWebHostEnvironment _env;
    private readonly IFaceIdentityClient _faceIdentity;

    public RegisterModel(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        RoleManager<IdentityRole> roles,
        IWebHostEnvironment env,
        IFaceIdentityClient faceIdentity)
    {
        _users = users;
        _signIn = signIn;
        _roles = roles;
        _env = env;
        _faceIdentity = faceIdentity;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public IList<AuthenticationScheme> ExternalLogins { get; private set; } = new List<AuthenticationScheme>();
    public string ReturnUrl { get; private set; } = "~/";

    public sealed class InputModel
    {
        [Required, RegularExpression("^(Student|Teacher)$"), Display(Name = "Register as")]
        public string AccountType { get; set; } = "Student";
        [Required, StringLength(75), Display(Name = "First name")] public string FirstName { get; set; } = "";
        [Required, StringLength(75), Display(Name = "Last name")] public string LastName { get; set; } = "";
        [Required, EmailAddress, Display(Name = "Email address")]
        public string Email { get; set; } = "";
        [Required, Phone, Display(Name = "Contact number")] public string PhoneNumber { get; set; } = "";
        [Required, StringLength(15), Display(Name = "CNIC"), RegularExpression(@"^\d{5}-\d{7}-\d$|^\d{13}$", ErrorMessage = "Enter a valid CNIC (e.g. 42101-1234567-1).")]
        public string CNIC { get; set; } = "";

        [Display(Name = "Enrollment photos")] public List<IFormFile> FaceEnrollmentImages { get; set; } = new();
        public string? CapturedEnrollmentPhotosJson { get; set; }

        public bool AcceptMonitoringNotice { get; set; }

        [DataType(DataType.Password), Required, StringLength(100, MinimumLength = 8), Display(Name = "Password")]
        public string Password { get; set; } = "";
        [DataType(DataType.Password), Required, Compare(nameof(Password)), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";

        [StringLength(100), Display(Name = "Father name")] public string? FatherName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        [StringLength(20)] public string? Gender { get; set; }
        [StringLength(100), Display(Name = "Testing organization")] public string? TestingOrganization { get; set; }
        [StringLength(100)] public string? Province { get; set; }
        [StringLength(100)] public string? District { get; set; }
        [StringLength(100)] public string? Domicile { get; set; }
        [StringLength(250)] public string? Address { get; set; }
        [StringLength(100)] public string? City { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = Safe(returnUrl);
        ExternalLogins = (await _signIn.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Safe(returnUrl);
        ExternalLogins = (await _signIn.GetExternalAuthenticationSchemesAsync()).ToList();
        if (!Input.AcceptMonitoringNotice)
            ModelState.AddModelError("Input.AcceptMonitoringNotice", "Please read and accept the camera and evidence notice.");
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        var normalizedCnic = Input.CNIC.Replace("-", "").Trim();
        if (await _users.FindByEmailAsync(email) != null)
        {
            ModelState.AddModelError("Input.Email", "An account with this email already exists.");
            return Page();
        }
        if (await _users.Users.AnyAsync(x => x.CNIC != null && x.CNIC.Replace("-", "") == normalizedCnic))
        {
            ModelState.AddModelError("Input.CNIC", "An account with this CNIC already exists.");
            return Page();
        }

        var selectedRole = string.Equals(Input.AccountType, "Teacher", StringComparison.Ordinal) ? "Teacher" : "Student";
        var images = await ReadImagesAsync(Input.FaceEnrollmentImages, Input.CapturedEnrollmentPhotosJson, Request.HttpContext.RequestAborted);
        if (!ModelState.IsValid) return Page();
        var minimum = selectedRole == "Student" ? 5 : 1;
        var maximum = selectedRole == "Student" ? 10 : 1;
        if (images.Count < minimum || images.Count > maximum)
        {
            ModelState.AddModelError("Input.FaceEnrollmentImages", selectedRole == "Student"
                ? "Provide 5 to 10 clear, different photos of the same person."
                : "Upload or capture one clear profile photo.");
            return Page();
        }

        var userId = Guid.NewGuid().ToString();
        var faceTemplateCreated = false;
        var bestPhotoIndex = 0;
        if (selectedRole == "Student")
        {
            var enrollment = await _faceIdentity.EnrollAsync(userId, images, Request.HttpContext.RequestAborted);
            if (!enrollment.Success)
            {
                ModelState.AddModelError("Input.FaceEnrollmentImages", enrollment.ServiceUnavailable ? "Face verification is temporarily unavailable. Ask the administrator to start the AI services, then try again. Your account has not been created." : enrollment.Message);
                return Page();
            }
            bestPhotoIndex = Math.Clamp(enrollment.Data?.BestPhotoIndex ?? 0, 0, images.Count - 1);
            faceTemplateCreated = true;
        }

        if (!await _roles.RoleExistsAsync(selectedRole))
            await _roles.CreateAsync(new IdentityRole(selectedRole));

        var profileBytes = images[bestPhotoIndex].Bytes;
        var extension = IsPng(profileBytes) ? ".png" : ".jpg";
        var folder = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_env), "profiles");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(folder, fileName);
        await System.IO.File.WriteAllBytesAsync(physicalPath, profileBytes, Request.HttpContext.RequestAborted);

        var firstName = Input.FirstName.Trim();
        var lastName = Input.LastName.Trim();
        var userIdToken = userId.Replace("-", string.Empty)[..12].ToUpperInvariant();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            PhoneNumber = Input.PhoneNumber.Trim(),
            FirstName = firstName,
            LastName = lastName,
            FullName = $"{firstName} {lastName}".Trim(),
            CNIC = Input.CNIC.Trim(),
            FatherName = Input.FatherName?.Trim(),
            DateOfBirth = Input.DateOfBirth,
            Gender = Input.Gender?.Trim(),
            TestingOrganization = Input.TestingOrganization?.Trim(),
            CandidateNumber = $"{(selectedRole == "Teacher" ? "EGT" : "EGC")}-{DateTime.UtcNow:yyyy}-{userIdToken}",
            Province = Input.Province?.Trim(),
            District = Input.District?.Trim(),
            Domicile = Input.Domicile?.Trim(),
            Address = Input.Address?.Trim(),
            City = Input.City?.Trim(),
            Country = "Pakistan",
            ProfileImage = "/ProfileImage/" + fileName,
            ProfilePhotoCapturedAt = DateTime.Now,
            EmailConfirmed = true,
            IsApproved = selectedRole == "Student",
            IsBlocked = false,
            CreatedDate = DateTime.Now
        };

        var result = await _users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            System.IO.File.Delete(physicalPath);
            if (faceTemplateCreated) await _faceIdentity.DeleteTemplateAsync(userId, Request.HttpContext.RequestAborted);
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        var roleResult = await _users.AddToRoleAsync(user, selectedRole);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            System.IO.File.Delete(physicalPath);
            if (faceTemplateCreated) await _faceIdentity.DeleteTemplateAsync(userId, Request.HttpContext.RequestAborted);
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        if (selectedRole == "Student")
        {
            await _signIn.SignInAsync(user, isPersistent: false);
            TempData["Success"] = "Registration completed. Your multi-photo face identity template is ready.";
            if (ReturnUrl != Url.Content("~/")) return LocalRedirect(ReturnUrl);
            return RedirectToAction("Index", "Dashboard", new { area = "Student" });
        }

        TempData["Success"] = "Teacher registration completed. Your secure account is waiting for administrator approval.";
        return RedirectToPage("./Login", new { ReturnUrl });
    }

    private async Task<List<FaceIdentityImage>> ReadImagesAsync(List<IFormFile> uploads, string? capturedJson, CancellationToken cancellationToken)
    {
        var images = new List<FaceIdentityImage>();
        if (!string.IsNullOrWhiteSpace(capturedJson))
        {
            try
            {
                var dataUrls = JsonSerializer.Deserialize<List<string>>(capturedJson) ?? new();
                if (dataUrls.Count + uploads.Count > 10) throw new FormatException();
                foreach (var dataUrl in dataUrls)
                {
                    var comma = dataUrl.IndexOf(',');
                    var bytes = Convert.FromBase64String(comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl);
                    AddIfValid(images, bytes, $"capture-{images.Count + 1}.jpg", "image/jpeg");
                }
            }
            catch
            {
                ModelState.AddModelError("Input.FaceEnrollmentImages", "Captured photos are invalid. Clear them and capture again.");
            }
        }
        if (uploads.Count + images.Count > 10)
        {
            ModelState.AddModelError("Input.FaceEnrollmentImages", "Use at most 10 photos in total, including captures and uploads.");
            return images;
        }
        foreach (var upload in uploads)
        {
            if (upload.Length <= 0 || upload.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError("Input.FaceEnrollmentImages", "Every photo must be between 8 KB and 5 MB.");
                continue;
            }
            await using var stream = new MemoryStream();
            await upload.CopyToAsync(stream, cancellationToken);
            AddIfValid(images, stream.ToArray(), upload.FileName, upload.ContentType);
        }
        return images;
    }

    private void AddIfValid(List<FaceIdentityImage> images, byte[] bytes, string fileName, string contentType)
    {
        if (bytes.Length < 8_000 || bytes.Length > 5 * 1024 * 1024 || (!IsJpeg(bytes) && !IsPng(bytes)))
        {
            ModelState.AddModelError("Input.FaceEnrollmentImages", "Only valid JPG or PNG photos between 8 KB and 5 MB are accepted.");
            return;
        }
        images.Add(new(fileName, IsPng(bytes) ? "image/png" : "image/jpeg", bytes));
    }

    private string Safe(string? url) => !string.IsNullOrWhiteSpace(url) && Url.IsLocalUrl(url) ? url : Url.Content("~/")!;
    private static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9;
    private static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 12 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
}
