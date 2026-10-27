using AiExamProctoring.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Controllers;

[Authorize]
public sealed class ProfileImageController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public ProfileImageController(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [HttpGet("/ProfileImage/{fileName}")]
    public async Task<IActionResult> Get(string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            return NotFound();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png")
            return NotFound();

        var storedUrl = "/ProfileImage/" + fileName;
        var ownerId = await _db.Users.AsNoTracking()
            .Where(x => x.ProfileImage == storedUrl)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null) return NotFound();

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isOwner = string.Equals(currentUserId, ownerId, StringComparison.Ordinal);
        var canReviewProfile = isOwner || User.IsInRole("Admin");
        if (!canReviewProfile && User.IsInRole("Teacher") && !string.IsNullOrWhiteSpace(currentUserId))
        {
            // A Teacher may see only candidates connected to an examination
            // created by that Teacher. Merely knowing a private filename never
            // grants access to an unrelated candidate photograph.
            canReviewProfile = await _db.ProctoringLogs.AsNoTracking().AnyAsync(x =>
                    x.StudentId == ownerId && x.Exam != null && x.Exam.CreatedBy == currentUserId,
                    cancellationToken) ||
                await _db.ExamAssignments.AsNoTracking().AnyAsync(x =>
                    x.StudentId == ownerId && x.IsActive && x.Exam != null && x.Exam.CreatedBy == currentUserId,
                    cancellationToken) ||
                await _db.ExamAttempts.AsNoTracking().AnyAsync(x =>
                    x.StudentId == ownerId && x.Exam != null && x.Exam.CreatedBy == currentUserId,
                    cancellationToken);
        }

        if (!canReviewProfile) return Forbid();

        var path = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, extension == ".png" ? "image/png" : "image/jpeg");
    }
}
