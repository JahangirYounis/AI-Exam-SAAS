using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Controllers;

/// <summary>
/// Students and Admins have NO role-wide raw-evidence permission. Only a named,
/// unexpired grant issued by the actual exam owner makes ONE media item available.
/// </summary>
[Authorize(Roles = "Student,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SharedEvidenceController(
    ApplicationDbContext db, IProctoringEvidenceStore store) : Controller
{
    private string? RecipientId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (RecipientId is not string userId) return Challenge();
        var grants = await EvidenceSharing.Available(db, userId).AsNoTracking()
            .Include(g => g.Log).ThenInclude(l => l!.Exam)
            .OrderByDescending(g => g.CreatedAtUtc).Take(100).ToListAsync(ct);
        return View(grants);
    }

    [HttpGet]
    public async Task<IActionResult> Media(Guid id, bool download = false, CancellationToken ct = default)
    {
        if (RecipientId is not string userId) return Challenge();
        // Authorize the grant before opening storage. Check revocation/expiry
        // on EVERY request; a copied link never grants access to another user.
        var grant = await EvidenceSharing.Available(db, userId).AsNoTracking()
            .Include(g => g.Log).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grant?.Log is not ProctoringLog log || log.AlertType == ProctoringAlertType.SystemAudit ||
            grant.Kind is not ("screenshot" or "recording" or "video") ||
            !string.Equals(EvidenceSharing.Key(log, grant.Kind), grant.StorageKey, StringComparison.Ordinal))
            return NotFound();

        var file = await store.OpenReadAsync(grant.StorageKey, ct);
        if (file is null) return NotFound();
        try { await EvidenceAccessAudit.RecordAsync(db, log, userId, grant.Kind, download, ct); }
        catch { await file.Stream.DisposeAsync(); throw; }

        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var ext = Path.GetExtension(grant.StorageKey).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webm" or ".wav")) ext = ".bin";
        return File(file.Stream, file.ContentType,
            download ? $"shared-evidence-{grant.Id:N}{ext}" : null, enableRangeProcessing: true);
    }
}
