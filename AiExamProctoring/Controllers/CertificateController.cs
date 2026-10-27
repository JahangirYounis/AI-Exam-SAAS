using System.Globalization;
using System.Security.Cryptography;
using AiExamProctoring.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Controllers;

[AllowAnonymous]
public sealed class CertificateController(ApplicationDbContext db, IDataProtectionProvider protection) : Controller
{
    public const string Purpose = "Examination.Certificate.Verification.v1";

    [HttpGet("certificate/verify"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Verify(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return NotFound();
        int id;
        try
        {
            if (!int.TryParse(protection.CreateProtector(Purpose).Unprotect(token), NumberStyles.None,
                CultureInfo.InvariantCulture, out id)) return NotFound();
        }
        catch (CryptographicException) { return NotFound(); }
        var result = await db.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null || (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AiExamProctoring.Models.AttemptStatus.ProctoringBlocked && !db.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation))).Include(x => x.Attempt).ThenInclude(x => x!.Student)
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .SingleOrDefaultAsync(x => x.ResultId == id && x.Attempt!.Status == Models.AttemptStatus.Completed, ct);
        if (result is null) return NotFound();
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View(result);
    }
}
