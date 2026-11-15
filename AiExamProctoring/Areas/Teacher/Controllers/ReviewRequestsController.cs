using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewRequestsController(ApplicationDbContext db, IProctoringStateService states) : Controller
{
    private const int PageSize = 15;
    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private IQueryable<ProctoringLog> Requests(string teacherId) =>
        TeacherEvidenceAccess.Incidents(db, teacherId).AsNoTracking()
            .Where(l => l.AlertType == ProctoringAlertType.ReviewRequested &&
                l.ModelName == ReviewRequestRules.RequestModel && l.AttemptId.HasValue);

    private async Task<List<TeacherReviewRequestItemViewModel>> LoadAsync(
        string teacherId, CancellationToken ct)
    {
        // Scope to a real Student-role ExamAttempt, NOT a user referenced by arbitrary logs.
        var appeals = await Requests(teacherId)
            .Include(l => l.Attempt).ThenInclude(a => a!.Exam)
            .Include(l => l.Student).Include(l => l.Exam)
            .OrderByDescending(l => l.CreatedDate).ThenByDescending(l => l.LogId)
            .ToListAsync(ct);
        if (appeals.Count == 0) return [];
        var attempts = appeals.Select(x => x.AttemptId!.Value).Distinct().ToArray();
        var decisions = await TeacherEvidenceAccess.Incidents(db, teacherId).AsNoTracking()
            .Where(l => l.ModelName == ReviewRequestRules.DecisionModel &&
                l.ParentIncidentId.HasValue && l.AttemptId.HasValue && attempts.Contains(l.AttemptId.Value))
            .OrderByDescending(l => l.LogId).ToListAsync(ct);
        var byRequest = decisions.GroupBy(x => x.ParentIncidentId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var blocks = await TeacherEvidenceAccess.Incidents(db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId.HasValue && attempts.Contains(l.AttemptId.Value) &&
                l.AlertType == ProctoringAlertType.ExamBlocked)
            .OrderByDescending(l => l.LogId).ToListAsync(ct);
        var byAttempt = blocks.GroupBy(l => l.AttemptId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var counts = await TeacherEvidenceAccess.Incidents(db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId.HasValue && attempts.Contains(l.AttemptId.Value) &&
                (l.CountsAsViolation || l.AlertType == ProctoringAlertType.ExamBlocked))
            .GroupBy(l => l.AttemptId!.Value)
            .Select(g => new { AttemptId = g.Key, Incidents = g.Count(),
                Evidence = g.Count(x => x.ScreenshotPath != null || x.RecordingPath != null || x.VideoEvidencePath != null) })
            .ToDictionaryAsync(g => g.AttemptId, ct);
        var finalAttemptIds = await db.ExamResults.AsNoTracking()
            .Where(r => attempts.Contains(r.AttemptId)).Select(r => r.AttemptId).Distinct().ToListAsync(ct);
        var finalSet = finalAttemptIds.ToHashSet();
        return appeals.Select(l =>
        {
            var attempt = l.Attempt!;
            byRequest.TryGetValue(l.LogId, out var decision);
            byAttempt.TryGetValue(attempt.AttemptId, out var block);
            counts.TryGetValue(attempt.AttemptId, out var count);
            var closed = finalSet.Contains(attempt.AttemptId) || attempt.CompletedAt.HasValue ||
                attempt.ResultReleasedAtUtc.HasValue || attempt.IsDisqualified ||
                attempt.Exam?.Status is ExamStatus.Completed or ExamStatus.Cancelled;
            var status = l.IsReviewed && decision is null
                ? ReviewRequestStatus.Completed // Legacy observation-reviewed appeals, not actionable.
                : ReviewRequestRules.Resolve(attempt.Status, closed,
                    decision?.ModelVersion, ReviewRequestRules.BelongsToCurrentLock(
                        l.LogId, block?.LogId ?? 0, l.ModelVersion, attempt.StateVersion));
            return new TeacherReviewRequestItemViewModel
            {
                RequestId = l.LogId, AttemptId = attempt.AttemptId, ExamId = attempt.ExamId,
                StudentId = attempt.StudentId, StudentName = l.Student?.FullName ?? "Candidate",
                CandidateNumber = l.Student?.CandidateNumber, ExamTitle = l.Exam?.ExamTitle ?? "Examination",
                AttemptStatus = attempt.Status, RequestedAt = l.CreatedDate,
                Status = status, DecisionReason = decision?.Description,
                ReviewerName = decision?.ActionByName, DecidedAt = decision?.CreatedDate,
                BlockReason = block?.Description ?? "A review of the recorded examination observations was requested.",
                IncidentCount = count?.Incidents ?? 0, EvidenceCount = count?.Evidence ?? 0,
                CanDecide = status == ReviewRequestStatus.Pending &&
                    ReviewRequestRules.CanResolve(attempt, finalSet.Contains(attempt.AttemptId)) &&
                    !l.IsReviewed
            };
        }).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status = "pending", int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var filter = (status ?? "pending").Trim().ToLowerInvariant();
        if (filter is not ("all" or "pending" or "approved" or "rejected" or "completed")) filter = "pending";
        var all = await LoadAsync(teacherId, cancellationToken);
        var filtered = filter == "all" ? all : all.Where(x => x.Status.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var totalPages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, totalPages);
        return View(new TeacherReviewRequestsViewModel
        {
            Filter = filter, Page = page, TotalPages = totalPages, TotalMatching = filtered.Count,
            Pending = all.Count(x => x.Status == ReviewRequestStatus.Pending),
            Approved = all.Count(x => x.Status == ReviewRequestStatus.Approved),
            Rejected = all.Count(x => x.Status == ReviewRequestStatus.Rejected),
            Completed = all.Count(x => x.Status == ReviewRequestStatus.Completed),
            Requests = filtered.Skip((page - 1) * PageSize).Take(PageSize).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (!await Requests(teacherId).AnyAsync(l => l.LogId == id, cancellationToken)) return NotFound();
        var item = (await LoadAsync(teacherId, cancellationToken)).FirstOrDefault(x => x.RequestId == id);
        if (item is null) return NotFound(); // Access could have been revoked between queries.
        var incidentsQuery = TeacherEvidenceAccess.Incidents(db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId == item.AttemptId && l.AlertType != ProctoringAlertType.SystemAudit);
        var total = await incidentsQuery.CountAsync(cancellationToken);
        var incidents = await incidentsQuery.OrderByDescending(l => l.CreatedDate)
            .ThenByDescending(l => l.LogId).Take(100).ToListAsync(cancellationToken);
        var decisions = await incidentsQuery.Where(l => l.ParentIncidentId == id &&
            l.ModelName == ReviewRequestRules.DecisionModel)
            .OrderByDescending(l => l.CreatedDate).ThenByDescending(l => l.LogId).ToListAsync(cancellationToken);
        return View(new TeacherReviewRequestDetailsViewModel
        {
            Request = item, Incidents = incidents, Decisions = decisions, TotalIncidents = total
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int id, string decision, string reason,
        CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (!await Requests(teacherId).AnyAsync(l => l.LogId == id, cancellationToken)) return NotFound();
        if (decision is not ("approve" or "reject")) return BadRequest("Invalid review decision.");
        if (!ReviewRequestRules.IsValidReason(reason))
        {
            TempData["Error"] = "Explain the decision in 10–300 characters.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var success = await states.ResolveAppealAsync(id, teacherId,
            decision == "approve", reason.Trim(), cancellationToken);
        TempData[success ? "Success" : "Error"] = success
            ? decision == "approve"
                ? "Review approved. The candidate may resume their saved examination."
                : "Block confirmed and decision saved. The candidate's saved answers remain intact."
            : "This request was already decided, is no longer open, or cannot be resumed under its examination policy.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
