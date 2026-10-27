using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
public sealed class ProfileController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWebHostEnvironment _environment;

    public ProfileController(ApplicationDbContext db, UserManager<ApplicationUser> users, IWebHostEnvironment environment)
    {
        _db = db;
        _users = users;
        _environment = environment;
    }

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        return model is null ? Challenge() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        return model is null ? Challenge() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TeacherProfileViewModel model, CancellationToken cancellationToken)
    {
        var teacher = await _users.GetUserAsync(User);
        if (teacher is null) return Challenge();

        ModelState.Remove(nameof(model.Email));
        ModelState.Remove(nameof(model.ProfileImage));

        byte[]? photo = null;
        string? extension = null;
        if (model.ImageFile is not null)
        {
            extension = Path.GetExtension(model.ImageFile.FileName).ToLowerInvariant();
            if (model.ImageFile.Length is <= 0 or > 5 * 1024 * 1024)
                ModelState.AddModelError(nameof(model.ImageFile), "Photo must be a JPG/PNG file up to 5 MB.");
            else if (extension is not ".jpg" and not ".jpeg" and not ".png")
                ModelState.AddModelError(nameof(model.ImageFile), "Only JPG and PNG photographs are allowed.");
            else
            {
                await using var memory = new MemoryStream();
                await model.ImageFile.CopyToAsync(memory, cancellationToken);
                photo = memory.ToArray();
                if (!IsSupportedImage(photo))
                    ModelState.AddModelError(nameof(model.ImageFile), "The selected file is not a valid JPG or PNG photograph.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.Email = teacher.Email ?? string.Empty;
            model.ProfileImage = teacher.ProfileImage;
            model.IsApproved = teacher.IsApproved;
            return View(model);
        }

        var oldImage = teacher.ProfileImage;
        string? newImage = null;
        if (photo is { Length: > 0 } && extension is not null)
        {
            var folder = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), photo, cancellationToken);
            newImage = $"/ProfileImage/{fileName}";
            teacher.ProfileImage = newImage;
            teacher.ProfilePhotoCapturedAt = DateTime.Now;
        }

        teacher.FirstName = model.FirstName.Trim();
        teacher.LastName = model.LastName.Trim();
        teacher.FullName = $"{teacher.FirstName} {teacher.LastName}".Trim();
        teacher.PhoneNumber = Clean(model.PhoneNumber);
        teacher.CNIC = Clean(model.CNIC);
        teacher.HighestQualification = Clean(model.HighestQualification);
        teacher.Department = Clean(model.Department);
        teacher.OrganizationName = Clean(model.OrganizationName);
        teacher.City = Clean(model.City);
        teacher.Country = Clean(model.Country);
        teacher.Address = Clean(model.Address);

        var result = await _users.UpdateAsync(teacher);
        if (!result.Succeeded)
        {
            if (newImage is not null) DeleteStoredPhoto(newImage);
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            model.Email = teacher.Email ?? string.Empty;
            model.ProfileImage = oldImage;
            return View(model);
        }

        if (newImage is not null && !string.IsNullOrWhiteSpace(oldImage)) DeleteStoredPhoto(oldImage);
        TempData["Success"] = "Teacher profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<TeacherProfileViewModel?> BuildModelAsync(CancellationToken cancellationToken)
    {
        var id = TeacherId;
        if (id is null) return null;
        var teacher = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (teacher is null) return null;
        var examIds = await _db.Exams.AsNoTracking().Where(x => x.CreatedBy == id).Select(x => x.ExamId).ToListAsync(cancellationToken);

        return new TeacherProfileViewModel
        {
            FirstName = teacher.FirstName,
            LastName = teacher.LastName,
            Email = teacher.Email ?? string.Empty,
            PhoneNumber = teacher.PhoneNumber,
            CNIC = teacher.CNIC,
            HighestQualification = teacher.HighestQualification,
            Department = teacher.Department,
            OrganizationName = teacher.OrganizationName,
            City = teacher.City,
            Country = teacher.Country,
            Address = teacher.Address,
            ProfileImage = teacher.ProfileImage,
            ProfilePhotoCapturedAt = teacher.ProfilePhotoCapturedAt,
            IsApproved = teacher.IsApproved,
            CreatedDate = teacher.CreatedDate,
            LastLoginDate = teacher.LastLoginDate,
            ExaminationCount = examIds.Count,
            QuestionCount = await _db.Questions.AsNoTracking().CountAsync(x => x.CreatedBy == id, cancellationToken),
            CandidateCount = await _db.ExamAssignments.AsNoTracking().Where(x => examIds.Contains(x.ExamId) && x.IsActive).Select(x => x.StudentId).Distinct().CountAsync(cancellationToken)
        };
    }

    private void DeleteStoredPhoto(string path)
    {
        if (!path.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var file = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", Path.GetFileName(path));
            if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSupportedImage(ReadOnlySpan<byte> data)
    {
        var jpeg = data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9;
        var png = data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        return jpeg || png;
    }
}
