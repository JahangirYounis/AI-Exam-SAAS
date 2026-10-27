using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.ViewModels.Student;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles = "Student")]
public class ProfileController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private readonly IFaceIdentityClient _faceIdentity;

    public ProfileController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment environment, IFaceIdentityClient faceIdentity)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
        _faceIdentity = faceIdentity;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var student = await GetCurrentStudentAsync();
        if (student is null) return Challenge();
        await LoadIdentityGalleryAsync(student.Id);
        return View(await BuildModel(student));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string? returnUrl = null)
    {
        var student = await GetCurrentStudentAsync();
        if (student is null) return Challenge();
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        ViewBag.RequirePhoto = Url.IsLocalUrl(returnUrl) && !HasStoredProfilePhoto(student.ProfileImage);
        return View(await BuildModel(student));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StudentProfileViewModel model, string? returnUrl = null)
    {
        var student = await _userManager.GetUserAsync(User);
        if (student is null) return Challenge();

        ModelState.Remove(nameof(model.Email));
        ModelState.Remove(nameof(model.ProfileImage));
        ModelState.Remove(nameof(model.Educations));
        ModelState.Remove(nameof(model.Experiences));
        ModelState.Remove(nameof(model.Certifications));

        byte[]? imageBytes = null;
        if (model.ImageFile is not null)
        {
            if (model.ImageFile.Length > 5 * 1024 * 1024)
                ModelState.AddModelError(nameof(model.ImageFile), "Profile image must be 5 MB or smaller.");

            var ext = Path.GetExtension(model.ImageFile.FileName).ToLowerInvariant();
            if (ext is not ".jpg" and not ".jpeg" and not ".png")
                ModelState.AddModelError(nameof(model.ImageFile), "Only JPG and PNG images are allowed.");

            if (model.ImageFile.Length > 0 && model.ImageFile.Length <= 5 * 1024 * 1024)
            {
                await using var imageStream = new MemoryStream();
                await model.ImageFile.CopyToAsync(imageStream);
                imageBytes = imageStream.ToArray();
                if (!IsSupportedImage(imageBytes))
                    ModelState.AddModelError(nameof(model.ImageFile), "The selected file is not a valid JPG or PNG image.");
            }
        }

        var requiresPhoto = Url.IsLocalUrl(returnUrl) && !HasStoredProfilePhoto(student.ProfileImage);
        if (requiresPhoto && imageBytes is not { Length: > 0 })
            ModelState.AddModelError(nameof(model.ImageFile), "Upload a clear, recent JPG or PNG photo before returning to the examination.");

        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            ViewBag.RequirePhoto = requiresPhoto;
            await LoadCollectionsAsync(model, student.Id);
            return View(model);
        }

        string? oldImage = student.ProfileImage;
        string? newImage = null;
        if (model.ImageFile is not null && imageBytes is { Length: > 0 })
        {
            var folder = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(model.ImageFile.FileName).ToLowerInvariant()}";
            var path = Path.Combine(folder, fileName);
            await System.IO.File.WriteAllBytesAsync(path, imageBytes);
            newImage = $"/ProfileImage/{fileName}";
            student.ProfileImage = newImage;
            student.ProfilePhotoCapturedAt = DateTime.Now;
        }

        var fullName = model.FullName.Trim();
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        student.FullName = fullName;
        student.FirstName = parts.FirstOrDefault() ?? student.FirstName;
        student.LastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : student.LastName;
        student.PhoneNumber = model.PhoneNumber?.Trim();
        student.CNIC = model.CNIC?.Trim();
        student.FatherName = model.FatherName?.Trim();
        student.Gender = model.Gender;
        student.DateOfBirth = model.DateOfBirth;
        student.Province = model.Province?.Trim();
        student.District = model.District?.Trim();
        student.Domicile = model.District?.Trim() ?? model.Domicile?.Trim() ?? student.Domicile;
        student.DomicileTaluka = model.DomicileTaluka?.Trim();
        student.UnionCouncil = model.UnionCouncil?.Trim();
        student.DomicileNumber = model.DomicileNumber?.Trim();
        student.PrcNumber = model.PrcNumber?.Trim();
        student.DomicileIssueDate = model.DomicileIssueDate;
        student.PermanentAddress = model.PermanentAddress?.Trim();
        student.TestingOrganization = model.TestingOrganization?.Trim();
        student.Department = model.Department?.Trim();
        if (!string.IsNullOrWhiteSpace(model.Batch)) student.Batch = model.Batch.Trim();
        if (!string.IsNullOrWhiteSpace(model.Semester)) student.Semester = model.Semester.Trim();
        student.Session = model.Session?.Trim();
        student.University = model.University?.Trim();
        student.Country = model.Country?.Trim();
        student.City = model.City?.Trim();
        student.Address = model.Address?.Trim();

        var result = await _userManager.UpdateAsync(student);
        if (!result.Succeeded)
        {
            if (newImage is not null) DeleteImage(newImage);
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            ViewBag.RequirePhoto = requiresPhoto;
            await LoadCollectionsAsync(model, student.Id);
            return View(model);
        }

        if (newImage is not null && !string.IsNullOrWhiteSpace(oldImage)) DeleteImage(oldImage);
        TempData["Success"] = "Profile information updated successfully.";
        if (Url.IsLocalUrl(returnUrl) && !string.IsNullOrWhiteSpace(student.ProfileImage))
            return LocalRedirect(returnUrl!);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> EnrollmentPhoto(int index)
    {
        var studentId = _userManager.GetUserId(User);
        if (studentId is null) return Challenge();
        var photo = await _faceIdentity.EnrollmentPhotoAsync(studentId, index, HttpContext.RequestAborted);
        return photo is null ? NotFound() : File(photo.Bytes, photo.ContentType);
    }

    [HttpGet]
    public async Task<IActionResult> FaceIdentity(string? returnUrl = null)
    {
        var student = await _userManager.GetUserAsync(User);
        if (student is null) return Challenge();
        var status = await _faceIdentity.TemplateStatusAsync(student.Id, HttpContext.RequestAborted);
        ViewBag.PhotoCount = status.Data?.PhotoCount ?? 0;
        ViewBag.ReferenceCount = status.Data?.ImageCount ?? 0;
        ViewBag.TemplateExists = status.Success && status.Data?.Exists == true;
        ViewBag.ServiceReady = status.Success;
        ViewBag.ServiceMessage = status.Success ? null : status.Message;
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View(student);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(55_000_000)]
    public async Task<IActionResult> AddIdentityPhotos(List<IFormFile> images)
    {
        var student = await _userManager.GetUserAsync(User);
        if (student is null) return Challenge();
        if (await _context.ExamAttempts.AnyAsync(a => a.StudentId == student.Id && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked))) return Conflict("Identity photos cannot change during an active or paused exam.");
        var status = await _faceIdentity.TemplateStatusAsync(student.Id, HttpContext.RequestAborted);
        if (!status.Success || status.Data?.Exists != true)
        {
            TempData["Error"] = status.Success ? "Create your face identity enrollment first." : status.Message;
            return RedirectToAction(nameof(Index));
        }
        var remaining = Math.Max(0, 10 - status.Data.PhotoCount);
        if (images.Count < 1 || images.Count > remaining)
        {
            TempData["Error"] = remaining == 0 ? "Your identity gallery already contains the maximum 10 photos." : $"Choose between 1 and {remaining} new photo(s).";
            return RedirectToAction(nameof(Index));
        }
        var payloads = await ReadFaceImagesAsync(images, null, HttpContext.RequestAborted);
        if (payloads.Count != images.Count)
        {
            TempData["Error"] = "Every selected file must be a valid JPG or PNG image up to 5 MB.";
            return RedirectToAction(nameof(Index));
        }
        var result = await _faceIdentity.AddEnrollmentPhotosAsync(student.Id, payloads, HttpContext.RequestAborted);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "New identity photo(s) added successfully." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveIdentityPhoto(int index)
    {
        var studentId = _userManager.GetUserId(User);
        if (studentId is null) return Challenge();
        if (await _context.ExamAttempts.AnyAsync(a => a.StudentId == studentId && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked))) return Conflict("Identity photos cannot change during an active or paused exam.");
        var result = await _faceIdentity.DeleteEnrollmentPhotoAsync(studentId, index, HttpContext.RequestAborted);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "Identity photo removed successfully." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(75_000_000)]
    [RequestFormLimits(ValueLengthLimit = 72_000_000, MultipartBodyLengthLimit = 75_000_000)]
    public async Task<IActionResult> FaceIdentity(FaceEnrollmentRequest request)
    {
        var student = await _userManager.GetUserAsync(User);
        if (student is null) return Challenge();
        if (await _context.ExamAttempts.AnyAsync(a => a.StudentId == student.Id && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked))) return Conflict("Identity photos cannot change during an active or paused exam.");
        var existing = await _faceIdentity.TemplateStatusAsync(student.Id, HttpContext.RequestAborted);
        if (existing.Success && existing.Data?.Exists == true)
        {
            TempData["Info"] = "Face identity is already enrolled. Only an administrator can reset it for authorized re-enrollment.";
            return RedirectToAction(nameof(FaceIdentity), new { returnUrl = request.ReturnUrl });
        }

        var images = await ReadFaceImagesAsync(request.Images, request.CapturedImagesJson, HttpContext.RequestAborted);
        if (images.Count is < 5 or > 10)
        {
            TempData["Error"] = "Provide 5 to 10 clear photos of the same person.";
            return RedirectToAction(nameof(FaceIdentity), new { returnUrl = request.ReturnUrl });
        }
        var result = await _faceIdentity.EnrollAsync(student.Id, images, HttpContext.RequestAborted);
        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(FaceIdentity), new { returnUrl = request.ReturnUrl });
        }

        {
            var first = images[Math.Clamp(result.Data?.BestPhotoIndex ?? 0, 0, images.Count - 1)];
            var extension = first.ContentType == "image/png" ? ".png" : ".jpg";
            var folder = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), first.Bytes, HttpContext.RequestAborted);
            student.ProfileImage = "/ProfileImage/" + fileName;
            student.ProfilePhotoCapturedAt = DateTime.Now;
            var update = await _userManager.UpdateAsync(student);
            if (!update.Succeeded)
            {
                try { System.IO.File.Delete(Path.Combine(folder, fileName)); } catch { }
                await _faceIdentity.DeleteTemplateAsync(student.Id, HttpContext.RequestAborted);
                TempData["Error"] = "Identity photos were valid, but the student profile could not be updated. Please try again.";
                return RedirectToAction(nameof(FaceIdentity), new { returnUrl = request.ReturnUrl });
            }
        }

        TempData["Success"] = "Face Identity Model 1 enrollment completed successfully.";
        if (Url.IsLocalUrl(request.ReturnUrl)) return LocalRedirect(request.ReturnUrl!);
        return RedirectToAction(nameof(FaceIdentity));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEducation(StudentEducation education)
    {
        var id = CurrentStudentId();
        if (id is null) return Challenge();
        ModelState.Remove(nameof(education.StudentId));
        ModelState.Remove(nameof(education.Student));
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please complete the required education fields.";
            return RedirectToAction(nameof(Edit), new { section = "education" });
        }
        education.StudentEducationId = 0;
        education.StudentId = id;
        education.DisplayOrder = await _context.StudentEducations.CountAsync(x => x.StudentId == id) + 1;
        education.CreatedDate = DateTime.Now;
        _context.StudentEducations.Add(education);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Education record added.";
        return RedirectToAction(nameof(Edit), new { section = "education" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEducation(int id)
    {
        var studentId = CurrentStudentId();
        if (studentId is null) return Challenge();
        var item = await _context.StudentEducations.FirstOrDefaultAsync(x => x.StudentEducationId == id && x.StudentId == studentId);
        if (item is not null) { _context.StudentEducations.Remove(item); await _context.SaveChangesAsync(); }
        return RedirectToAction(nameof(Edit), new { section = "education" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExperience(StudentExperience experience)
    {
        var id = CurrentStudentId();
        if (id is null) return Challenge();
        ModelState.Remove(nameof(experience.StudentId));
        ModelState.Remove(nameof(experience.Student));
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please complete the required experience fields.";
            return RedirectToAction(nameof(Edit), new { section = "experience" });
        }
        experience.StudentExperienceId = 0;
        experience.StudentId = id;
        experience.CreatedDate = DateTime.Now;
        if (experience.IsCurrentlyWorking) experience.EndDate = null;
        _context.StudentExperiences.Add(experience);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Experience record added.";
        return RedirectToAction(nameof(Edit), new { section = "experience" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExperience(int id)
    {
        var studentId = CurrentStudentId();
        if (studentId is null) return Challenge();
        var item = await _context.StudentExperiences.FirstOrDefaultAsync(x => x.StudentExperienceId == id && x.StudentId == studentId);
        if (item is not null) { _context.StudentExperiences.Remove(item); await _context.SaveChangesAsync(); }
        return RedirectToAction(nameof(Edit), new { section = "experience" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCertification(StudentCertification certification)
    {
        var id = CurrentStudentId();
        if (id is null) return Challenge();
        ModelState.Remove(nameof(certification.StudentId));
        ModelState.Remove(nameof(certification.Student));
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please complete the required certification fields.";
            return RedirectToAction(nameof(Edit), new { section = "certification" });
        }
        certification.StudentCertificationId = 0;
        certification.StudentId = id;
        certification.CreatedDate = DateTime.Now;
        _context.StudentCertifications.Add(certification);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Certification record added.";
        return RedirectToAction(nameof(Edit), new { section = "certification" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCertification(int id)
    {
        var studentId = CurrentStudentId();
        if (studentId is null) return Challenge();
        var item = await _context.StudentCertifications.FirstOrDefaultAsync(x => x.StudentCertificationId == id && x.StudentId == studentId);
        if (item is not null) { _context.StudentCertifications.Remove(item); await _context.SaveChangesAsync(); }
        return RedirectToAction(nameof(Edit), new { section = "certification" });
    }

    private async Task<ApplicationUser?> GetCurrentStudentAsync()
    {
        var id = CurrentStudentId();
        return id is null ? null : await _context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    private string? CurrentStudentId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<StudentProfileViewModel> BuildModel(ApplicationUser s)
    {
        var id = s.Id;
        var analytics = await _context.ExamResults.AsNoTracking()
            .Where(x => x.Attempt != null && x.Attempt.StudentId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Passed = g.Count(x => x.IsPassed),
                Failed = g.Count(x => !x.IsPassed),
                Average = g.Average(x => (double)x.TotalMarksObtained)
            }).FirstOrDefaultAsync();

        var attemptStats = await _context.ExamAttempts.AsNoTracking()
            .Where(x => x.StudentId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Completed = g.Count(x => x.Status == AttemptStatus.Completed || x.Status == AttemptStatus.AutoSubmitted || x.Status == AttemptStatus.Disqualified),
                Ongoing = g.Count(x => x.Status == AttemptStatus.InProgress ||
                                         x.Status == AttemptStatus.ProctoringBlocked)
            }).FirstOrDefaultAsync();

        var model = new StudentProfileViewModel
        {
            Id=s.Id, FullName=s.FullName ?? "Student", Email=s.Email ?? "", PhoneNumber=s.PhoneNumber, CNIC=s.CNIC,
            FatherName=s.FatherName, Gender=s.Gender, DateOfBirth=s.DateOfBirth, RollNumber=s.RollNumber,
            RegistrationNumber=s.RegistrationNumber, Department=s.Department, Batch=s.Batch, Semester=s.Semester,
            Session=s.Session, University=s.University, HighestQualification=s.HighestQualification, CurrentStatus=s.CurrentStatus,
            OrganizationName=s.OrganizationName, Province=s.Province, District=s.District, Domicile=s.Domicile,
            DomicileTaluka=s.DomicileTaluka, UnionCouncil=s.UnionCouncil, DomicileNumber=s.DomicileNumber,
            PrcNumber=s.PrcNumber, DomicileIssueDate=s.DomicileIssueDate, PermanentAddress=s.PermanentAddress,
            CandidateNumber=s.CandidateNumber, TestingOrganization=s.TestingOrganization, Country=s.Country,
            City=s.City, Address=s.Address, ProfileImage=s.ProfileImage, IsApproved=s.IsApproved, IsBlocked=s.IsBlocked,
            CreatedDate=s.CreatedDate, LastLoginDate=s.LastLoginDate,
            CompletedExams=attemptStats?.Completed ?? 0, OngoingExams=attemptStats?.Ongoing ?? 0,
            PassedExams=analytics?.Passed ?? 0, FailedExams=analytics?.Failed ?? 0,
            AverageMarks=Math.Round(analytics?.Average ?? 0, 2)
        };
        var totalResults = model.PassedExams + model.FailedExams;
        model.PassPercentage = totalResults == 0 ? 0 : Math.Round(model.PassedExams * 100d / totalResults, 2);
        model.TotalExams = model.CompletedExams + model.OngoingExams;

        var fields = new object?[] { s.FullName,s.PhoneNumber,s.CNIC,s.FatherName,s.Gender,s.DateOfBirth,s.Province,s.District,s.Domicile,s.PermanentAddress,s.ProfileImage };
        model.ProfileCompletion=(int)Math.Round(fields.Count(x=>x is not null && !string.IsNullOrWhiteSpace(x.ToString())) * 100d / fields.Length);
        await LoadCollectionsAsync(model, id);
        return model;
    }

    private async Task LoadIdentityGalleryAsync(string studentId)
    {
        var status = await _faceIdentity.TemplateStatusAsync(studentId, HttpContext.RequestAborted);
        ViewBag.IdentityTemplateExists = status.Success && status.Data?.Exists == true;
        ViewBag.IdentityPhotoCount = status.Data?.PhotoCount ?? 0;
        ViewBag.IdentityServiceReady = status.Success;
        ViewBag.IdentityServiceMessage = status.Success ? null : status.Message;
    }

    private async Task LoadCollectionsAsync(StudentProfileViewModel model, string studentId)
    {
        model.Educations = await _context.StudentEducations.AsNoTracking().Where(x => x.StudentId == studentId).OrderBy(x => x.DisplayOrder).ThenByDescending(x => x.PassingYear).ToListAsync();
        model.Experiences = await _context.StudentExperiences.AsNoTracking().Where(x => x.StudentId == studentId).OrderByDescending(x => x.IsCurrentlyWorking).ThenByDescending(x => x.StartDate).ToListAsync();
        model.Certifications = await _context.StudentCertifications.AsNoTracking().Where(x => x.StudentId == studentId).OrderByDescending(x => x.EndDate).ThenByDescending(x => x.CreatedDate).ToListAsync();
    }

    private void DeleteImage(string path)
    {
        try
        {
            var full = path.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", Path.GetFileName(path))
                : Path.Combine(_environment.WebRootPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if(System.IO.File.Exists(full)) System.IO.File.Delete(full);
        }
        catch { }
    }

    private bool HasStoredProfilePhoto(string? profileImage)
    {
        if (string.IsNullOrWhiteSpace(profileImage) ||
            !profileImage.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase)) return false;
        var fileName = Path.GetFileName(profileImage);
        if (string.IsNullOrWhiteSpace(fileName) || fileName != profileImage["/ProfileImage/".Length..]) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png") return false;
        return System.IO.File.Exists(Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", fileName));
    }

    private static bool IsSupportedImage(ReadOnlySpan<byte> data)
    {
        var isJpeg = data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9;
        var isPng = data.Length >= 12 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                    data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        return isJpeg || isPng;
    }

    private async Task<List<FaceIdentityImage>> ReadFaceImagesAsync(List<IFormFile> uploads, string? capturedJson, CancellationToken cancellationToken)
    {
        var images = new List<FaceIdentityImage>();
        if (uploads.Count > 10) return images;
        if (!string.IsNullOrWhiteSpace(capturedJson))
        {
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(capturedJson) ?? new();
                if (values.Count + uploads.Count > 10) return images;
                foreach (var value in values.Take(10))
                {
                    var comma = value.IndexOf(',');
                    var bytes = Convert.FromBase64String(comma >= 0 ? value[(comma + 1)..] : value);
                    if (IsSupportedImage(bytes) && bytes.Length >= 8_000 && bytes.Length <= 5 * 1024 * 1024)
                        images.Add(new($"capture-{images.Count + 1}.jpg", "image/jpeg", bytes));
                }
            }
            catch { }
        }
        foreach (var upload in uploads.Take(10 - images.Count))
        {
            if (upload.Length < 8_000 || upload.Length > 5 * 1024 * 1024) continue;
            await using var stream = new MemoryStream();
            await upload.CopyToAsync(stream, cancellationToken);
            var bytes = stream.ToArray();
            if (!IsSupportedImage(bytes)) continue;
            var isPng = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50;
            images.Add(new(upload.FileName, isPng ? "image/png" : "image/jpeg", bytes));
        }
        return images;
    }
}
