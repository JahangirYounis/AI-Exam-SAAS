
using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Notifications;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

public sealed class TeacherActivityItem
{
    // The (Kind, EntryId) pair is a stable, database-backed receipt identity.
    // For monitoring summaries EntryId is the latest unresolved incident (or last historical incident).
    public string Kind { get; set; } = "";
    public int EntryId { get; set; }
    public string Key => $"{Kind}:{EntryId}";
    public string Category { get; set; } = "Updates";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Url { get; set; } = "";
    public string? LiveUrl { get; set; }
    public string? Candidate { get; set; }
    public string? Exam { get; set; }
    public int IncidentCount { get; set; } = 1;
    public int Priority { get; set; } = 4;
    public bool NeedsAction { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public bool Read { get; set; }
}

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationCenterController(ApplicationDbContext db) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<List<TeacherActivityItem>> Items(CancellationToken ct)
    {
        var list = new List<TeacherActivityItem>();
        var invites = await db.ExamInvigilators.AsNoTracking().Include(i => i.Exam)
          .Where(i => i.TeacherId == Actor && i.RevokedAtUtc == null)
          .OrderByDescending(i => i.InvitedAtUtc).ToListAsync(ct);
        foreach (var i in invites.GroupBy(i => i.ExamId).Select(g => g.OrderByDescending(x => x.InvitedAtUtc).First()))
            list.Add(new()
            {
                Kind = "invitation",
                Category = "Invitations",
                Priority = 2,
                EntryId = i.Id,
                Title = "Exam collaboration invitation",
                Detail = $"{i.Exam?.ExamTitle ?? "Exam"} · {(i.IsActive ? "Accepted" : "Awaiting your response")}",
                OccurredAtUtc = i.InvitedAtUtc,
                Url = Url.Action("Index", "Invitations", new { area = "Teacher" })!
            });

        var accepted = await db.ExamInvigilators.AsNoTracking().Include(i => i.Exam).Include(i => i.Teacher)
          .Where(i => i.Exam != null && i.Exam.CreatedBy == Actor && i.AcceptedAtUtc != null && i.IsActive && i.RevokedAtUtc == null)
          .OrderByDescending(i => i.AcceptedAtUtc).ToListAsync(ct);
        foreach (var i in accepted.GroupBy(i => new { i.ExamId, i.TeacherId }).Select(g => g.OrderByDescending(x => x.AcceptedAtUtc).First()))
            list.Add(new()
            {
                Kind = "accepted",
                Category = "Invitations",
                Priority = 4,
                EntryId = i.Id,
                Title = "Teacher accepted exam invitation",
                Detail = $"{i.Teacher?.FullName ?? "Teacher"} joined {i.Exam?.ExamTitle}",
                OccurredAtUtc = i.AcceptedAtUtc,
                Url = Url.Action("Manage", "Invitations", new { area = "Teacher", examId = i.ExamId })!
            });

        var connections = await db.TeacherConnections.AsNoTracking()
          .Where(c => c.RecipientId == Actor || c.SenderId == Actor).OrderByDescending(c => c.Id).ToListAsync(ct);
        // A re-invitation gets a new stable request event ID, so old read receipts
        // never suppress the new request notification.
        var connectionIds = connections.Select(c => c.Id).ToArray();
        var connectionEvents = await db.TeacherConnectionEvents.AsNoTracking()
            .Where(e => connectionIds.Contains(e.ConnectionId) &&
                (e.Action == "Requested" || e.Action == "RequestedAgain" || e.Action == "Accepted"))
            .OrderByDescending(e => e.Id).ToListAsync(ct);
        var latestRequests = connectionEvents.Where(e => e.Action is "Requested" or "RequestedAgain")
            .GroupBy(e => e.ConnectionId).ToDictionary(g => g.Key, g => g.First());
        var latestAcceptances = connectionEvents.Where(e => e.Action == "Accepted")
            .GroupBy(e => e.ConnectionId).ToDictionary(g => g.Key, g => g.First());
        var otherIds = connections.Select(c => c.RecipientId == Actor ? c.SenderId : c.RecipientId).Distinct().ToArray();
        var people = await db.Users.AsNoTracking().Where(u => otherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        foreach (var c in connections)
        {
            if (c.RecipientId == Actor && c.AcceptedAtUtc == null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null)
            {
                latestRequests.TryGetValue(c.Id, out var evt);
                list.Add(new()
                {
                    Kind = evt is null ? "connection" : "conn-request",
                    Category = "Connections",
                    Priority = 3,
                    EntryId = evt?.Id ?? c.Id,
                    Title = "Teacher connection request",
                    Detail = $"{people.GetValueOrDefault(c.SenderId, "Teacher")} wants to connect",
                    OccurredAtUtc = evt?.OccurredAtUtc ?? c.CreatedAtUtc,
                    Url = Url.Action("Index", "Connections", new { area = "Teacher", tab = "received" })!
                });
            }
            else if (c.SenderId == Actor && c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null)
            {
                latestAcceptances.TryGetValue(c.Id, out var evt);
                list.Add(new()
                {
                    Kind = evt is null ? "connected" : "conn-accepted",
                    Category = "Connections",
                    Priority = 4,
                    EntryId = evt?.Id ?? c.Id,
                    Title = "Connection accepted",
                    Detail = $"{people.GetValueOrDefault(c.RecipientId, "Teacher")} accepted your connection request",
                    OccurredAtUtc = evt?.OccurredAtUtc ?? c.AcceptedAtUtc,
                    Url = Url.Action("Chat", "Connections", new { area = "Teacher", id = c.Id })!
                });
            }
        }

        var accessibleConnectionIds = connections.Where(c => c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null).Select(c => c.Id).ToArray();
        var privateMessages = await db.TeacherPrivateMessages.AsNoTracking()
          .Where(m => m.SenderId != Actor && accessibleConnectionIds.Contains(m.ConnectionId))
          .OrderByDescending(m => m.Id).ToListAsync(ct);
        var roomOwners = connections.ToDictionary(c => c.Id, c => c);
        var senderIds = privateMessages.Select(m => m.SenderId).Distinct().ToArray();
        var senders = await db.Users.AsNoTracking().Where(u => senderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        foreach (var m in privateMessages)
            if (roomOwners.ContainsKey(m.ConnectionId)) list.Add(new()
            {
                Kind = "private",
                Category = "Messages",
                Priority = 3,
                EntryId = m.Id,
                Title = "New private message",
                Detail = $"{senders.GetValueOrDefault(m.SenderId, "Teacher")}: {Preview(m.Body)}",
                OccurredAtUtc = m.SentAtUtc,
                Url = Url.Action("Chat", "Connections", new { area = "Teacher", id = m.ConnectionId })!
            });

        var access = await db.ExamInvigilators.AsNoTracking()
          .Where(i => i.TeacherId == Actor && i.IsActive && i.AcceptedAtUtc != null && i.RevokedAtUtc == null)
          .Select(i => i.ExamId).Distinct().ToListAsync(ct);
        var ownExams = await db.Exams.AsNoTracking().Where(e => e.CreatedBy == Actor).Select(e => e.ExamId).ToListAsync(ct);
        var accessibleExams = access.Concat(ownExams).Distinct().ToArray();
        // Exam publication is visible to accepted co-teachers; never notify someone lacking exam access.
        var published = await db.Exams.AsNoTracking().Where(e => access.Contains(e.ExamId) && e.IsPublished)
          .OrderByDescending(e => e.UpdatedDate).ToListAsync(ct);
        foreach (var exam in published)
            list.Add(new()
            {
                Kind = "published",
                Category = "Examinations",
                Priority = 3,
                EntryId = exam.ExamId,
                Title = "Exam published",
                Detail = $"{exam.ExamTitle} is now published; open the collaboration room.",
                OccurredAtUtc = exam.UpdatedDate ?? exam.CreatedDate,
                Url = Url.Action("Team", "Invitations", new { area = "Teacher", examId = exam.ExamId })!
            });

        var messages = await db.ExamTeamMessages.AsNoTracking().Where(m => accessibleExams.Contains(m.ExamId) && m.SenderId != Actor)
          .OrderByDescending(m => m.Id).ToListAsync(ct);
        var messageExamIds = messages.Select(m => m.ExamId).Distinct().ToArray();
        var examNames = await db.Exams.AsNoTracking().Where(e => messageExamIds.Contains(e.ExamId)).ToDictionaryAsync(e => e.ExamId, e => e.ExamTitle, ct);
        var teamSenderIds = messages.Select(m => m.SenderId).Distinct().ToArray();
        var teamSenders = await db.Users.AsNoTracking().Where(u => teamSenderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        foreach (var m in messages) list.Add(new()
        {
            Kind = "team",
            Category = "Messages",
            Priority = 3,
            EntryId = m.Id,
            Title = $"Exam discussion · {examNames.GetValueOrDefault(m.ExamId, "Exam")}",
            Detail = $"{teamSenders.GetValueOrDefault(m.SenderId, "Teacher")}: {Preview(m.Body)}",
            OccurredAtUtc = m.SentAtUtc,
            Url = Url.Action("Team", "Invitations", new { area = "Teacher", examId = m.ExamId })!
        });
        // The existing AlertsController.Feed remains the authoritative incident notification source.
        return list.OrderByDescending(i => i.OccurredAtUtc ?? DateTime.MinValue).ThenByDescending(i => i.EntryId).ToList();
    }

    private static string Preview(string? value) => value is null ? "" : value.Length > 110 ? value[..110] + "…" : value;

    // Monitoring feed: only actual candidate attempts, never a teacher audit row.
    // All data comes from committed logs; no second notification/event writer is introduced.
    private IQueryable<ProctoringLog> AuthorizedCandidateLogs()
    {
        var teacher = Actor;
        var studentRoles = db.Roles.Where(r => r.Name == "Student").Select(r => r.Id);
        return db.ProctoringLogs.AsNoTracking().Where(l =>
            l.ExamId != null && l.AttemptId != null && l.Attempt != null && l.Exam != null &&
            l.Attempt.ExamId == l.ExamId && l.Attempt.StudentId == l.StudentId &&
            db.UserRoles.Any(ur => ur.UserId == l.StudentId && studentRoles.Contains(ur.RoleId)) &&
            (l.Exam.CreatedBy == teacher || db.ExamInvigilators.Any(i =>
                i.ExamId == l.ExamId && i.TeacherId == teacher && i.IsActive &&
                i.CanMonitor && i.AcceptedAtUtc != null && i.RevokedAtUtc == null)));
    }

    private static IQueryable<ProctoringLog> MeaningfulLogs(IQueryable<ProctoringLog> source) =>
        source.Where(l => l.AlertType != ProctoringAlertType.SystemAudit &&
            (l.AlertType == ProctoringAlertType.ExamBlocked ||
             (l.AlertType == ProctoringAlertType.ReviewRequested && l.ModelName == "Student appeal") ||
             (TeacherAlertContract.MonitoringKinds.Contains(l.AlertType) && l.CountsAsViolation && l.PolicyProcessed) ||
             (l.ModelName == "Speech screening" && l.IncidentKey == "speech" && l.ParentIncidentId == null &&
                (l.AlertType == ProctoringAlertType.AudioDetected || l.AlertType == ProctoringAlertType.TalkingDetected ||
                    l.AlertType == ProctoringAlertType.AnswerPromptSpeech)) ||
             (l.IncidentKey == "attention" && l.ParentIncidentId == null && l.PolicyProcessed &&
                (l.AlertType == ProctoringAlertType.HeadAway || l.AlertType == ProctoringAlertType.GazeAway ||
                    l.AlertType == ProctoringAlertType.LookAway)) ||
             l.AlertType == ProctoringAlertType.ExamPageHidden ||
             (l.AlertType == ProctoringAlertType.FullScreenExit && l.IncidentKey == "browser" && l.PolicyProcessed) ||
             (l.IncidentKey != null && l.IncidentKey.StartsWith("object:") && l.ParentIncidentId == null &&
                l.PolicyProcessed && (l.AlertType == ProctoringAlertType.ObjectObservation ||
                    l.AlertType == ProctoringAlertType.ElectronicDeviceDetected)) ||
             (l.AlertType == ProctoringAlertType.AnswerPromptSpeech && l.Severity == ProctoringSeverity.High &&
              l.MatchedQuestionId.HasValue && l.PolicyProcessed) ||
             (l.AlertType == ProctoringAlertType.IdentityMismatch && l.Severity == ProctoringSeverity.High) ||
             (l.AlertType == ProctoringAlertType.CameraMonitoringInterrupted && l.Severity == ProctoringSeverity.High) ||
             (l.IncidentKey == "browser" && l.ParentIncidentId == null &&
                (l.AlertType == ProctoringAlertType.ScreenShareLost || l.AlertType == ProctoringAlertType.ScreenCaptureUnavailable)) ||
             ((l.AlertType == ProctoringAlertType.CameraMonitoringInterrupted || l.AlertType == ProctoringAlertType.MicrophoneAccessLost) &&
                l.Description != null && l.Description.StartsWith("[DEVICE_WARNING]")) ||
             (l.AlertType == ProctoringAlertType.AiMonitoringUnavailable && l.Severity == ProctoringSeverity.High)));

    private async Task<List<TeacherActivityItem>> MonitoringItems(CancellationToken ct)
    {
        var records = MeaningfulLogs(AuthorizedCandidateLogs());
        // Keep each committed incident as a separate notification with its own receipt.
        var incidentRows = await records
            .Where(l => l.AlertType != ProctoringAlertType.ExamBlocked &&
                l.AlertType != ProctoringAlertType.ReviewRequested)
            .Include(l => l.Student).Include(l => l.Exam).Include(l => l.Attempt)
            .OrderByDescending(l => l.LogId)
            .ToListAsync(ct);
        var output = new List<TeacherActivityItem>();
        foreach (var log in incidentRows)
        {
            var candidate = log.Student?.FullName ?? "Candidate";
            var name = log.Exam?.ExamTitle ?? "Examination";
            var title = TeacherAlertContract.Title(log);
            output.Add(new TeacherActivityItem
            {
                Kind = "monitor",
                EntryId = log.LogId,
                Category = "Monitoring",
                Priority = log.IsReviewed ? 5 : log.AlertType == ProctoringAlertType.IdentityMismatch ? 0 : 2,
                NeedsAction = !log.IsReviewed,
                Candidate = candidate,
                Exam = name,
                OccurredAtUtc = log.CreatedDate,
                Title = $"{title} · {candidate}",
                Detail = log.WarningNumber is int warning ? $"{name} · Warning {warning}/{log.Exam!.AllowedWarnings}" : name,
                Url = Url.Action("Incident", "Alerts", new { area = "Teacher", id = log.LogId }) ?? "",
                LiveUrl = log.Attempt?.Status == AttemptStatus.InProgress
                    ? Url.Action("Live", "Monitoring", new { area = "Teacher", examId = log.ExamId,
                        attemptId = log.AttemptId, incidentId = log.IncidentKey == "absence:escalation" ? log.ParentIncidentId : log.LogId })
                    : null
            });
        }

        var important = await records
            .Where(l => l.AlertType == ProctoringAlertType.ExamBlocked ||
                (l.AlertType == ProctoringAlertType.ReviewRequested && l.ModelName == "Student appeal"))
            .Include(l => l.Student).Include(l => l.Exam).Include(l => l.Attempt)
            .OrderByDescending(l => l.LogId).ToListAsync(ct);
        foreach (var log in important)
        {
            var appeal = log.AlertType == ProctoringAlertType.ReviewRequested;
            var outstanding = !log.IsReviewed && log.Attempt?.Status == AttemptStatus.ProctoringBlocked;
            var candidate = log.Student?.FullName ?? "Candidate";
            var name = log.Exam?.ExamTitle ?? "Examination";
            output.Add(new TeacherActivityItem
            {
                Kind = appeal ? "appeal" : "blocked",
                EntryId = log.LogId,
                Category = appeal ? "Review Requests" : "Monitoring",
                Priority = outstanding ? (appeal ? 0 : 1) : 5,
                NeedsAction = outstanding,
                Candidate = candidate,
                Exam = name,
                OccurredAtUtc = log.CreatedDate,
                Title = appeal ? $"Review requested · {candidate}" : $"Student blocked · {candidate}",
                Detail = appeal
                    ? $"{candidate} requested review for {name}. Open the evidence and decide whether the exam can resume."
                    : log.Description?.StartsWith("Student blocked —", StringComparison.Ordinal) == true
                        ? $"{name} · {log.Description}"
                        : $"{candidate} was blocked in {name}. Review the saved incidents and exam status.",
                Url = appeal
                    ? Url.Action("Details", "ReviewRequests", new { area = "Teacher", id = log.LogId }) ?? ""
                    : Url.Action("Incident", "Alerts", new { area = "Teacher", id = log.LogId }) ?? ""
            });
        }
        return output;
    }

    private async Task<List<TeacherActivityItem>> AllItems(CancellationToken ct)
    {
        var activity = await Items(ct);
        activity.AddRange(await MonitoringItems(ct));
        // Server-side stable keys: a duplicate source row cannot create another inbox item.
        return TeacherNotificationRules.Deduplicate(activity);
    }

    private async Task<HashSet<string>> ReadKeys(CancellationToken ct)
    {
        var actor = Actor;
        var rows = await db.TeacherActivityReads.AsNoTracking()
            .Where(r => r.UserId == actor)
            .Select(r => new { r.Kind, r.EntryId }).ToListAsync(ct);
        return rows.Select(r => $"{r.Kind}:{r.EntryId}").ToHashSet(StringComparer.Ordinal);
    }

    private async Task<HashSet<int>> LegacyIncidentReads(CancellationToken ct)
    {
        var actor = Actor;
        const string prefix = "TeacherIncidentRead:";
        var types = await db.StudentNotifications.AsNoTracking()
            .Where(n => n.StudentId == actor && n.Type.StartsWith(prefix))
            .Select(n => n.Type).ToListAsync(ct);
        return types.Select(t => int.TryParse(t[prefix.Length..], out var id) ? id : 0)
            .Where(id => id > 0).ToHashSet();
    }

    private async Task<List<TeacherActivityItem>> LoadedWithReceipts(CancellationToken ct)
    {
        var items = await AllItems(ct);
        var receipts = await ReadKeys(ct);
        var legacy = await LegacyIncidentReads(ct);
        foreach (var item in items)
            item.Read = receipts.Contains(item.Key) ||
                (item.Kind is "monitor" or "blocked" or "appeal" && legacy.Contains(item.EntryId)) ||
                (item.Kind is "monitor" or "blocked" or "appeal" && !item.NeedsAction);
        return items;
    }

    [HttpGet]
    public async Task<IActionResult> Feed(CancellationToken cancellationToken,
        int page = 0, bool history = true, string category = "All", bool? unreadOnly = null)
    {
        var items = await LoadedWithReceipts(cancellationToken);
        var unread = items.Count(i => !i.Read);
        var visible = TeacherNotificationRules.Filter(items, category, unreadOnly ?? !history);
        var offset = Math.Clamp(page, 0, 100000) * 25L;
        var slice = visible.Skip((int)Math.Min(offset, int.MaxValue)).Take(25).ToList();
        return Json(new
        {
            version = 2,
            unread,
            category,
            page = Math.Max(0, page),
            hasMore = offset + slice.Count < visible.Count,
            total = visible.Count,
            items = slice
        });
    }

    private async Task SaveReceipts(IReadOnlyCollection<TeacherActivityItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var actor = Actor;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct) : null;
            // Serialize concurrent read writes for this teacher, protecting the existing
            // (UserId, Kind, EntryId) unique key from double inserts across open tabs.
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT Id FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id={actor}", ct);
            var existing = await ReadKeys(ct);
            foreach (var item in items.Where(i => !existing.Contains(i.Key)))
                db.TeacherActivityReads.Add(new TeacherActivityRead
                { UserId = actor, Kind = item.Kind, EntryId = item.EntryId });
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        var items = await AllItems(cancellationToken);
        await SaveReceipts(items, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(string kind, int entryId, CancellationToken cancellationToken)
    {
        var item = (await AllItems(cancellationToken))
            .FirstOrDefault(i => i.Kind == kind && i.EntryId == entryId);
        if (item is null) return NotFound();
        await SaveReceipts(new[] { item }, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(string kind, int entryId, CancellationToken cancellationToken)
    {
        // Never accept a browser-supplied URL: re-check current ownership/permissions
        // and return the server-generated route for this exact existing event.
        var item = (await AllItems(cancellationToken))
            .FirstOrDefault(i => i.Kind == kind && i.EntryId == entryId);
        if (item is null) return NotFound();
        await SaveReceipts(new[] { item }, cancellationToken);
        return Ok(new { url = item.Url });
    }
}
