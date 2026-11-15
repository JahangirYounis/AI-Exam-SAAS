using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

// Read-only class directory. All class/roster writes belong exclusively to the Admin area.
[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassesController(ApplicationDbContext db) : Controller
{
    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var teacherId = TeacherId;
        if (string.IsNullOrWhiteSpace(teacherId)) return Challenge();

        // Filter on the server before materializing class or membership records.
        var classes = await db.AcademicClasses.AsNoTracking()
            .Where(c => c.TeacherId == teacherId)
            .Include(c => c.Members.Where(m => m.IsActive))
            .AsSplitQuery()
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return View(classes);
    }

    [HttpGet]
    public async Task<IActionResult> Roster(int id, CancellationToken cancellationToken = default)
    {
        var teacherId = TeacherId;
        if (string.IsNullOrWhiteSpace(teacherId)) return Challenge();

        var assignedClass = await db.AcademicClasses.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId, cancellationToken);
        if (assignedClass is null) return NotFound();

        var members = await (from membership in db.ClassMemberships.AsNoTracking()
                             join user in db.Users.AsNoTracking() on membership.StudentId equals user.Id
                             where membership.AcademicClassId == id && membership.IsActive &&
                                   db.UserRoles.Any(ur => ur.UserId == user.Id &&
                                       db.Roles.Any(role => role.Id == ur.RoleId && role.Name == "Student"))
                             orderby user.FullName
                             select user)
            .ToListAsync(cancellationToken);

        ViewBag.RosterStudents = members;
        return View(assignedClass);
    }

    // No Create, CreateTwelve, Rename, Roster POST or Transfer action is exposed.
    // [HttpGet] above is important: unannotated MVC actions accept arbitrary verbs.
}
