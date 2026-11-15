using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
using ExamEntity = AiExamProctoring.Models.Exam;

namespace AiExamProctoring.Services.Exam;

/// <summary>
/// Server-side examination clock. It is intentionally independent from the browser:
/// expired sessions are auto-submitted, rest windows are enforced, the next session
/// starts after the configured break, and the final result is generated even if the
/// candidate closes the tab or manipulates the local device clock.
/// </summary>
public sealed class ExamLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExamLifecycleService> _logger;

    public ExamLifecycleService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExamLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay keeps database migrations/seeding first in the lifecycle.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Server-side examination lifecycle processing failed.");
            }
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var now = DateTime.Now;

        // ------------------------------------------------------------
        // 1. Move released scheduled examinations into Running state
        //    and generate "exam started" notifications.
        // ------------------------------------------------------------
        var scheduledExams = await db.Exams
            .Include(x => x.Assignments)
            .Where(x =>
                x.IsPublished &&
                x.Status == ExamStatus.Scheduled &&
                x.ExamDate <= now.Date)
            .ToListAsync(cancellationToken);

        var scheduledExamChanged = false;

        foreach (var exam in scheduledExams.Where(x => now >= x.StartDate))
        {
            exam.Status = ExamStatus.Running;
            exam.UpdatedDate = now;
            scheduledExamChanged = true;

            var activeAssignments = exam.Assignments
                .Where(x => x.IsActive)
                .ToList();

            if (activeAssignments.Count == 0)
            {
                continue;
            }

            var studentIds = activeAssignments
                .Select(x => x.StudentId)
                .Distinct()
                .ToList();

            var existingStudentIds = await db.StudentNotifications
                .AsNoTracking()
                .Where(x =>
                    x.ExamId == exam.ExamId &&
                    x.Type == "ExamStarted" &&
                    studentIds.Contains(x.StudentId))
                .Select(x => x.StudentId)
                .ToListAsync(cancellationToken);

            var existingSet = existingStudentIds
                .ToHashSet(StringComparer.Ordinal);

            foreach (var assignment in activeAssignments)
            {
                if (existingSet.Contains(assignment.StudentId))
                {
                    continue;
                }

                db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = assignment.StudentId,
                    ExamId = exam.ExamId,
                    Title = "Your examination has started",
                    Message =
                        $"{exam.ExamTitle} is now open. " +
                        $"Your exam roll number is {assignment.RollNumber}. " +
                        $"Complete the readiness check and start before " +
                        $"{exam.LatestStartDate:hh:mm tt}.",
                    Type = "ExamStarted",
                    CreatedDate = now,
                    IsRead = false
                });

                existingSet.Add(assignment.StudentId);
            }
        }

        if (scheduledExamChanged ||
            db.ChangeTracker.Entries<StudentNotification>()
                .Any(x => x.State == EntityState.Added))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // ------------------------------------------------------------
        // 2. Auto-submit expired active session attempts.
        //
        //    Expiry is calculated in .NET intentionally. This avoids
        //    SQL translation problems with DateTime.Add/TimeSpan logic.
        // ------------------------------------------------------------
        var activeSessions = await db.ExamSessionAttempts
            .Include(x => x.Session)
            .Include(x => x.Attempt)
                .ThenInclude(x => x!.Exam)
            .Where(x =>
                x.Status == AttemptStatus.InProgress &&
                x.FrozenAtUtc == null &&
                x.StartedAt != null)
            .ToListAsync(cancellationToken);

        foreach (var sessionAttempt in activeSessions)
        {
            if (sessionAttempt.Session is null ||
                sessionAttempt.Attempt?.Exam is null ||
                sessionAttempt.StartedAt is null)
            {
                continue;
            }

            var expiresAt = sessionAttempt.StartedAt.Value
                .AddMinutes(sessionAttempt.Session.DurationMinutes);

            if (now < expiresAt)
            {
                continue;
            }

            await AutoSubmitSessionAsync(
                db,
                sessionAttempt,
                now,
                cancellationToken);
        }

        // ------------------------------------------------------------
        // 3. Enforce rest windows and automatically start the next
        //    session when the maximum break period ends.
        //
        //    This ensures browser refresh/closure cannot pause the exam.
        // ------------------------------------------------------------
        var attempts = await db.ExamAttempts
            .Include(x => x.Exam)
            .Where(x => x.Status == AttemptStatus.InProgress)
            .ToListAsync(cancellationToken);

        foreach (var attempt in attempts)
        {
            if (attempt.Exam is null)
            {
                continue;
            }

            var hasActiveSession = await db.ExamSessionAttempts
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.AttemptId == attempt.AttemptId &&
                        x.Status == AttemptStatus.InProgress,
                    cancellationToken);

            if (hasActiveSession)
            {
                continue;
            }

            var completedSessions = await db.ExamSessionAttempts
                .AsNoTracking()
                .Include(x => x.Session)
                .Where(x =>
                    x.AttemptId == attempt.AttemptId &&
                    (x.Status == AttemptStatus.Completed ||
                     x.Status == AttemptStatus.AutoSubmitted))
                .OrderByDescending(x => x.CompletedAt)
                .ToListAsync(cancellationToken);

            var completedSessionIds = completedSessions
                .Select(x => x.SessionId)
                .ToHashSet();

            var nextSession = await db.ExamSessions
                .AsNoTracking()
                .Where(x =>
                    x.ExamId == attempt.ExamId &&
                    x.IsActive &&
                    !completedSessionIds.Contains(x.SessionId))
                .OrderBy(x => x.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextSession is null)
            {
                await FinalizeAttemptAsync(
                    db,
                    attempt,
                    now,
                    cancellationToken);

                continue;
            }

            // Maximum supported rest is 10 minutes.
            if (completedSessions.Count > 0)
            {
                var previousSession = completedSessions[0];

                var restMinutes = Math.Clamp(previousSession.Session?.RestAfterMinutes ?? 0, 0, 10);

                var restEndsAt =
                    (previousSession.CompletedAt ?? now)
                    .AddMinutes(restMinutes);

                if (now < restEndsAt)
                {
                    continue;
                }
            }

            var alreadyCreated = await db.ExamSessionAttempts
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.AttemptId == attempt.AttemptId &&
                        x.SessionId == nextSession.SessionId,
                    cancellationToken);

            if (alreadyCreated)
            {
                continue;
            }

            db.ExamSessionAttempts.Add(new ExamSessionAttempt
            {
                AttemptId = attempt.AttemptId,
                SessionId = nextSession.SessionId,
                StartedAt = now,
                Status = AttemptStatus.InProgress,
                TotalMarks = nextSession.TotalMarks
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // A simultaneous browser Continue request may win the race.
                // The database unique index protects AttemptId + SessionId.
                _logger.LogDebug(
                    ex,
                    "Next session was already created for attempt {AttemptId}.",
                    attempt.AttemptId);

                db.ChangeTracker.Clear();
            }
        }

        // ------------------------------------------------------------
        // 4. Move elapsed released examinations into permanent history.
        //
        //    EndDate should already represent:
        //    latest allowed start + active session time + allowed breaks.
        // ------------------------------------------------------------
        var releasableHistory = await db.Exams.Include(x => x.Sessions)
            .Where(x =>
                x.IsPublished &&
                (x.Status == ExamStatus.Scheduled ||
                 x.Status == ExamStatus.Running) &&
                x.ExamDate <= now.Date)
            .ToListAsync(cancellationToken);

        foreach (var exam in releasableHistory.Where(x => now >= x.EndDate))
        {
            exam.Status = ExamStatus.Completed;
            exam.UpdatedDate = now;
        }

        // IMPORTANT:
        // This project has a namespace named AiExamProctoring.Services.Exam.
        // The model alias below avoids the Services.Exam namespace/type-name collision.
        // ExamEntity explicitly means AiExamProctoring.Models.Exam.
        if (db.ChangeTracker
            .Entries<ExamEntity>()
            .Any(x => x.State == EntityState.Modified))
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    internal static async Task AutoSubmitSessionAsync(
        ApplicationDbContext db,
        ExamSessionAttempt sessionAttempt,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        if (sessionAttempt.Attempt?.Exam is null ||
            sessionAttempt.Session is null)
        {
            return;
        }

        var allowBlocked = db.Database.CurrentTransaction != null;
        var sessionId = sessionAttempt.SessionAttemptId;
        async Task<bool> Score()
        {
            sessionAttempt = await db.ExamSessionAttempts.Include(s => s.Session).Include(s => s.Attempt).ThenInclude(a => a!.Exam)
                .SingleAsync(s => s.SessionAttemptId == sessionId, cancellationToken);
            if (!allowBlocked && (sessionAttempt.Attempt!.Status != AttemptStatus.InProgress || sessionAttempt.FrozenAtUtc.HasValue)) return false;
            if (sessionAttempt.Status is not (AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked)) return false;
            await Services.Results.ExamResultFinalizationService.ScoreSessionAsync(db, sessionAttempt,
                sessionAttempt.Attempt!.Exam!.ShuffleOptions, null, completedAt,
                latestSavedAt: sessionAttempt.Status == AttemptStatus.InProgress && sessionAttempt.StartedAt.HasValue
                    ? sessionAttempt.StartedAt.Value.AddMinutes(sessionAttempt.Session!.DurationMinutes) : null,
                ct: cancellationToken);
            sessionAttempt.Status = AttemptStatus.AutoSubmitted;
            sessionAttempt.CompletedAt = completedAt;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        if (db.Database.CurrentTransaction != null) await Score();
        else await Services.Results.ExamResultFinalizationService.WriteAsync(db, sessionAttempt.AttemptId, Score, cancellationToken);
    }

    internal static async Task FinalizeAttemptAsync(
        ApplicationDbContext db,
        ExamAttempt attempt,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction != null)
            await Services.Results.ExamResultFinalizationService.FinalizeCompletedInTransactionAsync(db, attempt.AttemptId, completedAt, cancellationToken);
        else
            await Services.Results.ExamResultFinalizationService.FinalizeCompletedAsync(db, attempt.AttemptId, completedAt, cancellationToken);
    }
}
