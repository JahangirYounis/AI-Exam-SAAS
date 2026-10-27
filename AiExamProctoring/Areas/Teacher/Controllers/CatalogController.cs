using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
public sealed class CatalogController : Controller
{
    private readonly ApplicationDbContext _db;

    public CatalogController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var departmentQuery = _db.Departments.AsNoTracking()
            .Include(x => x.Posts)
            .AsQueryable();
        var subjectQuery = _db.Subjects.AsNoTracking()
            .Include(x => x.Questions)
            .AsQueryable();

        if (search is not null)
        {
            departmentQuery = departmentQuery.Where(x =>
                x.DepartmentName.Contains(search) ||
                (x.DepartmentCode != null && x.DepartmentCode.Contains(search)) ||
                x.Posts.Any(p => p.PostName.Contains(search)));
            subjectQuery = subjectQuery.Where(x => x.SubjectName.Contains(search));
        }

        return View(new TeacherCatalogViewModel
        {
            Search = search,
            Departments = await departmentQuery.OrderBy(x => x.DepartmentName).ToListAsync(cancellationToken),
            Subjects = await subjectQuery.OrderBy(x => x.SubjectName).ToListAsync(cancellationToken)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(
        string departmentName,
        string? departmentCode,
        string? description,
        CancellationToken cancellationToken)
    {
        departmentName = departmentName?.Trim() ?? string.Empty;
        departmentCode = Clean(departmentCode)?.ToUpperInvariant();
        description = Clean(description);

        if (departmentName.Length is < 2 or > 150)
        {
            TempData["Error"] = "Enter a department name between 2 and 150 characters.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Departments.AnyAsync(
                x => x.DepartmentName.ToLower() == departmentName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "A department with this name already exists.";
            return RedirectToAction(nameof(Index));
        }

        _db.Departments.Add(new Department
        {
            DepartmentName = departmentName,
            DepartmentCode = departmentCode,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Department created. It is now available in recruitment exam setup.";
        return RedirectToAction(nameof(Index), new { section = "departments" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDepartment(
        int departmentId,
        string departmentName,
        string? departmentCode,
        string? description,
        CancellationToken cancellationToken)
    {
        var department = await _db.Departments.FindAsync([departmentId], cancellationToken);
        if (department is null) return NotFound();

        departmentName = departmentName?.Trim() ?? string.Empty;
        if (departmentName.Length is < 2 or > 150)
        {
            TempData["Error"] = "Enter a valid department name.";
            return RedirectToAction(nameof(Index), new { section = "departments" });
        }

        if (await _db.Departments.AnyAsync(
                x => x.DepartmentId != departmentId &&
                     x.DepartmentName.ToLower() == departmentName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "Another department already uses this name.";
            return RedirectToAction(nameof(Index), new { section = "departments" });
        }

        department.DepartmentName = departmentName;
        department.DepartmentCode = Clean(departmentCode)?.ToUpperInvariant();
        department.Description = Clean(description);
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Department updated.";
        return RedirectToAction(nameof(Index), new { section = "departments" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleDepartment(int id, CancellationToken cancellationToken)
    {
        var department = await _db.Departments.FindAsync([id], cancellationToken);
        if (department is null) return NotFound();
        department.IsActive = !department.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = department.IsActive ? "Department activated." : "Department archived. Existing examinations are unchanged.";
        return RedirectToAction(nameof(Index), new { section = "departments" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(
        int departmentId,
        string postName,
        string? grade,
        string? description,
        CancellationToken cancellationToken)
    {
        postName = postName?.Trim() ?? string.Empty;
        var departmentExists = await _db.Departments.AnyAsync(
            x => x.DepartmentId == departmentId && x.IsActive,
            cancellationToken);

        if (!departmentExists || postName.Length is < 2 or > 150)
        {
            TempData["Error"] = "Select an active department and enter a valid post/position name.";
            return RedirectToAction(nameof(Index), new { section = "posts" });
        }

        if (await _db.Posts.AnyAsync(
                x => x.DepartmentId == departmentId && x.PostName.ToLower() == postName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "This post already exists in the selected department.";
            return RedirectToAction(nameof(Index), new { section = "posts" });
        }

        _db.Posts.Add(new Post
        {
            DepartmentId = departmentId,
            PostName = postName,
            Grade = Clean(grade),
            Description = Clean(description),
            IsActive = true,
            CreatedDate = DateTime.Now
        });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Post/position created.";
        return RedirectToAction(nameof(Index), new { section = "posts" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePost(
        int postId,
        int departmentId,
        string postName,
        string? grade,
        string? description,
        CancellationToken cancellationToken)
    {
        var post = await _db.Posts.FindAsync([postId], cancellationToken);
        if (post is null) return NotFound();

        postName = postName?.Trim() ?? string.Empty;
        var validDepartment = await _db.Departments.AnyAsync(
            x => x.DepartmentId == departmentId && x.IsActive,
            cancellationToken);
        if (!validDepartment || postName.Length is < 2 or > 150)
        {
            TempData["Error"] = "Select an active department and enter a valid post name.";
            return RedirectToAction(nameof(Index), new { section = "posts" });
        }

        if (await _db.Posts.AnyAsync(
                x => x.PostId != postId && x.DepartmentId == departmentId &&
                     x.PostName.ToLower() == postName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "Another post with this name already exists in that department.";
            return RedirectToAction(nameof(Index), new { section = "posts" });
        }

        post.DepartmentId = departmentId;
        post.PostName = postName;
        post.Grade = Clean(grade);
        post.Description = Clean(description);
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Post/position updated.";
        return RedirectToAction(nameof(Index), new { section = "posts" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePost(int id, CancellationToken cancellationToken)
    {
        var post = await _db.Posts.FindAsync([id], cancellationToken);
        if (post is null) return NotFound();
        post.IsActive = !post.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = post.IsActive ? "Post activated." : "Post archived. Existing examinations are unchanged.";
        return RedirectToAction(nameof(Index), new { section = "posts" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubject(
        string subjectName,
        string? description,
        CancellationToken cancellationToken)
    {
        subjectName = subjectName?.Trim() ?? string.Empty;
        if (subjectName.Length is < 2 or > 150)
        {
            TempData["Error"] = "Enter a subject name between 2 and 150 characters.";
            return RedirectToAction(nameof(Index), new { section = "subjects" });
        }

        if (await _db.Subjects.AnyAsync(
                x => x.SubjectName.ToLower() == subjectName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "This subject already exists.";
            return RedirectToAction(nameof(Index), new { section = "subjects" });
        }

        _db.Subjects.Add(new Subject
        {
            SubjectName = subjectName,
            Description = Clean(description),
            IsActive = true,
            CreatedDate = DateTime.Now
        });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Subject created. It is now available in the Teacher question bank.";
        return RedirectToAction(nameof(Index), new { section = "subjects" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSubject(
        int subjectId,
        string subjectName,
        string? description,
        CancellationToken cancellationToken)
    {
        var subject = await _db.Subjects.FindAsync([subjectId], cancellationToken);
        if (subject is null) return NotFound();
        if (subject.IsLocked)
        {
            TempData["Error"] = "This seeded subject is locked to protect the shared question bank.";
            return RedirectToAction(nameof(Index), new { section = "subjects" });
        }

        subjectName = subjectName?.Trim() ?? string.Empty;
        if (subjectName.Length is < 2 or > 150 || await _db.Subjects.AnyAsync(
                x => x.SubjectId != subjectId && x.SubjectName.ToLower() == subjectName.ToLower(),
                cancellationToken))
        {
            TempData["Error"] = "Enter a unique, valid subject name.";
            return RedirectToAction(nameof(Index), new { section = "subjects" });
        }

        subject.SubjectName = subjectName;
        subject.Description = Clean(description);
        subject.UpdatedDate = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Subject updated.";
        return RedirectToAction(nameof(Index), new { section = "subjects" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSubject(int id, CancellationToken cancellationToken)
    {
        var subject = await _db.Subjects.FindAsync([id], cancellationToken);
        if (subject is null) return NotFound();
        if (subject.IsLocked)
        {
            TempData["Error"] = "This seeded subject is locked and must remain available.";
            return RedirectToAction(nameof(Index), new { section = "subjects" });
        }

        subject.IsActive = !subject.IsActive;
        subject.UpdatedDate = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = subject.IsActive ? "Subject activated." : "Subject archived. Existing papers are unchanged.";
        return RedirectToAction(nameof(Index), new { section = "subjects" });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
