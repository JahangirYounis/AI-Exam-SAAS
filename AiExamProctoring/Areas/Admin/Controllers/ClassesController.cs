using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

// Never expose class or roster mutations from the Teacher area.
[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        ViewBag.Teachers = await db.Users.AsNoTracking()
            .Where(u => u.IsApproved && !u.IsBlocked && db.UserRoles.Any(r => r.UserId == u.Id &&
                db.Roles.Any(role => role.Id == r.RoleId && role.Name == "Teacher")))
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        var classes = await db.AcademicClasses.AsNoTracking()
            .Include(c => c.Members.Where(m => m.IsActive))
            .AsSplitQuery()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return View(classes);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string teacherId, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120) return BadRequest("Enter a class name up to 120 characters.");
        if (!await ValidTeacherAsync(teacherId, cancellationToken)) return BadRequest("Choose an approved teacher.");
        if (await db.AcademicClasses.AnyAsync(c => c.TeacherId == teacherId && c.Name == name, cancellationToken))
            return Conflict("This teacher already has a class with that name.");

        db.AcademicClasses.Add(new AcademicClass { Name = name, TeacherId = teacherId });
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Class created and assigned to the selected teacher.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, string name, CancellationToken cancellationToken = default)
    {
        var klass = await db.AcademicClasses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (klass is null) return NotFound();
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120) return BadRequest("Enter a valid class name.");
        if (await db.AcademicClasses.AnyAsync(c => c.Id != id && c.TeacherId == klass.TeacherId && c.Name == name, cancellationToken))
            return Conflict("This teacher already has a class with that name.");
        klass.Name = name;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Class renamed.";
        return RedirectToAction(nameof(Roster), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Roster(int id, CancellationToken cancellationToken = default)
    {
        var klass = await db.AcademicClasses.AsNoTracking().Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (klass is null) return NotFound();

        var currentMemberIds = klass.Members.Where(m => m.IsActive).Select(m => m.StudentId).ToArray();
        var students = await db.Users.AsNoTracking()
            .Where(u => currentMemberIds.Contains(u.Id) ||
                (u.IsApproved && !u.IsBlocked &&
                 db.UserRoles.Any(r => r.UserId == u.Id &&
                     db.Roles.Any(role => role.Id == r.RoleId && role.Name == "Student")) &&
                 !db.ClassMemberships.Any(m => m.StudentId == u.Id && m.IsActive && m.AcademicClassId != id)))
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        ViewBag.Students = students;
        ViewBag.TransferDestinations = await db.AcademicClasses.AsNoTracking()
            .Where(c => c.Id != id && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
        ViewBag.AssignedTeacherName = await db.Users.AsNoTracking().Where(u => u.Id == klass.TeacherId)
            .Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);
        return View(klass);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Roster(int id, string[]? studentIds, CancellationToken cancellationToken = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;

            var klass = await db.AcademicClasses.Include(c => c.Members)
                .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (klass is null) return NotFound();
            if (!klass.IsActive) return Conflict("An archived class cannot change enrollment.");

            var ids = (studentIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).Take(2001).ToArray();
            if (ids.Length > 2000) return BadRequest("A class may contain up to 2000 selected students.");

            var existingActiveIds = klass.Members.Where(m => m.IsActive)
                .Select(m => m.StudentId).ToHashSet(StringComparer.Ordinal);
            var newlyEnrolled = ids.Where(s => !existingActiveIds.Contains(s)).ToArray();
            if (newlyEnrolled.Length > 0)
            {
                var valid = await db.Users.CountAsync(u => newlyEnrolled.Contains(u.Id) &&
                    u.IsApproved && !u.IsBlocked && db.UserRoles.Any(r => r.UserId == u.Id &&
                        db.Roles.Any(role => role.Id == r.RoleId && role.Name == "Student")), cancellationToken);
                if (valid != newlyEnrolled.Length)
                    return BadRequest("New enrollments must be approved Student accounts.");
            }

            if (await db.ClassMemberships.AnyAsync(m => ids.Contains(m.StudentId) &&
                m.IsActive && m.AcademicClassId != id, cancellationToken))
                return Conflict("One or more students are enrolled elsewhere. Use Transfer Student instead.");

            // Removing a current member must not interrupt a running/blocked examination.
            var removed = existingActiveIds.Except(ids, StringComparer.Ordinal).ToArray();
            if (removed.Length > 0 && await HasProtectedAttemptsAsync(id, removed, cancellationToken))
                return Conflict("A student has an ongoing or blocked class examination. Finish it before changing membership.");
            if (removed.Length > 0 && await HasPendingClassExaminationAsync(id, cancellationToken))
                return Conflict("This class has a published examination still available to candidates. Finish it before removing students.");

            var selected = ids.ToHashSet(StringComparer.Ordinal);
            foreach (var membership in klass.Members)
                membership.IsActive = selected.Contains(membership.StudentId);

            var existingMembers = klass.Members.Select(m => m.StudentId).ToHashSet(StringComparer.Ordinal);
            foreach (var studentId in newlyEnrolled.Where(s => !existingMembers.Contains(s)))
                klass.Members.Add(new ClassMembership { StudentId = studentId, IsActive = true });

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            TempData["Success"] = "Roster saved. Previous memberships remain in the database as inactive records.";
            return RedirectToAction(nameof(Roster), new { id });
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferStudent(int id, string studentId, int destinationClassId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentId) || id == destinationClassId)
            return BadRequest("Choose a different destination class and a student.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;

            var source = await db.AcademicClasses.Include(c => c.Members)
                .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            var destination = await db.AcademicClasses.Include(c => c.Members)
                .SingleOrDefaultAsync(c => c.Id == destinationClassId, cancellationToken);
            if (source is null || destination is null) return NotFound();
            if (!source.IsActive || !destination.IsActive)
                return Conflict("Both classes must be active to transfer a student.");

            var sourceMembership = source.Members.SingleOrDefault(m => m.StudentId == studentId && m.IsActive);
            if (sourceMembership is null) return NotFound();
            if (await db.ClassMemberships.AnyAsync(m => m.StudentId == studentId &&
                m.IsActive && m.AcademicClassId != id, cancellationToken))
                return Conflict("Student has another active class membership. Resolve the conflicting membership first.");
            if (!await db.Users.AnyAsync(u => u.Id == studentId && u.IsApproved && !u.IsBlocked &&
                db.UserRoles.Any(r => r.UserId == u.Id &&
                    db.Roles.Any(role => role.Id == r.RoleId && role.Name == "Student")), cancellationToken))
                return BadRequest("Student must have an approved active Student account.");
            if (await HasProtectedAttemptsAsync(id, new[] { studentId }, cancellationToken))
                return Conflict("Student has an ongoing or blocked attempt in this class. Complete/review it first.");
            if (await HasPendingClassExaminationAsync(id, cancellationToken))
                return Conflict("A published class examination is still available. Complete it before transferring students.");

            // Transfer changes membership only: never rewrite examination owners,
            // saved answers, evidence, historic exam assignments or results.
            sourceMembership.IsActive = false;
            var oldDestinationRow = destination.Members.SingleOrDefault(m => m.StudentId == studentId);
            if (oldDestinationRow is null)
                destination.Members.Add(new ClassMembership { StudentId = studentId, IsActive = true });
            else
                oldDestinationRow.IsActive = true;

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            TempData["Success"] = "Student transferred. Historical class memberships and exam records were preserved.";
            return RedirectToAction(nameof(Roster), new { id = destinationClassId });
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(int id, string teacherId, bool active,
        CancellationToken cancellationToken = default)
    {
        var klass = await db.AcademicClasses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (klass is null) return NotFound();
        if (!await ValidTeacherAsync(teacherId, cancellationToken)) return BadRequest("Choose an approved teacher.");
        if (!active && await db.ExamAttempts.AnyAsync(a => a.Exam != null &&
            a.Exam.AcademicClassId == id &&
            (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked), cancellationToken))
            return Conflict("Finish or resolve ongoing and blocked attempts before archiving.");
        if (!active && await HasPendingClassExaminationAsync(id, cancellationToken))
            return Conflict("Complete or close published class examinations before archiving the class.");
        if (klass.TeacherId != teacherId && await db.Exams.AnyAsync(e => e.AcademicClassId == id &&
            e.CreatedBy != teacherId, cancellationToken))
            return Conflict("Class has examinations owned by the current teacher. Their ownership cannot be transferred implicitly. Preserve these examinations and reassign them through a separate authorized workflow.");

        klass.TeacherId = teacherId;
        klass.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Teacher assignment and class status saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssociateExam(int id, int examId, CancellationToken cancellationToken = default)
    {
        var klass = await db.AcademicClasses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.ExamId == examId, cancellationToken);
        if (klass is null || exam is null) return NotFound();
        if (!klass.IsActive || exam.CreatedBy != klass.TeacherId)
            return BadRequest("Select an active class assigned to the examination creator.");
        if (exam.IsPublished || exam.IsLocked || (exam.Status == ExamStatus.Cancelled || exam.Status == ExamStatus.Completed) ||
            await db.ExamAttempts.AnyAsync(a => a.ExamId == examId, cancellationToken))
            return Conflict("A published, locked, completed or attempted examination cannot be reassigned to another class.");
        if (exam.AcademicClassId == id) return RedirectToAction(nameof(Index));
        exam.AcademicClassId = id;
        exam.Audience = ExamAudience.Private;
        exam.ClassName = klass.Name;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Draft examination associated with the class.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ValidTeacherAsync(string? teacherId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teacherId)) return false;
        return await db.Users.AnyAsync(u => u.Id == teacherId &&
            u.IsApproved && !u.IsBlocked && db.UserRoles.Any(r => r.UserId == u.Id &&
                db.Roles.Any(role => role.Id == r.RoleId && role.Name == "Teacher")), cancellationToken);
    }

    private Task<bool> HasProtectedAttemptsAsync(int classId, string[] studentIds, CancellationToken cancellationToken) =>
        db.ExamAttempts.AnyAsync(a => studentIds.Contains(a.StudentId) && a.Exam != null &&
            a.Exam.AcademicClassId == classId &&
            (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked), cancellationToken);

    private Task<bool> HasPendingClassExaminationAsync(int classId, CancellationToken cancellationToken) =>
        db.Exams.AnyAsync(e => e.AcademicClassId == classId && e.IsPublished &&
            e.Status != ExamStatus.Completed && e.Status != ExamStatus.Cancelled &&
            (e.IsReusable || e.ExamDate >= DateTime.Today.AddDays(-2)), cancellationToken);
}
