using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class TeacherController : Controller
{
    private readonly UserManager<ApplicationUser> _users;

    public TeacherController(UserManager<ApplicationUser> users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status)
    {
        // GetUsersInRoleAsync already returns the complete teacher set. Filter this
        // materialized list in memory instead of embedding a HashSet in an EF query;
        // some database providers cannot translate HashSet.Contains reliably.
        var teachers = await _users.GetUsersInRoleAsync("Teacher");
        var query = teachers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(x =>
                (x.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.CNIC?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.CandidateNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = status?.ToLowerInvariant() switch
        {
            "pending" => query.Where(x => !x.IsApproved && !x.IsBlocked),
            "approved" => query.Where(x => x.IsApproved && !x.IsBlocked),
            "blocked" => query.Where(x => x.IsBlocked),
            _ => query
        };

        ViewBag.Total = teachers.Count;
        ViewBag.Pending = teachers.Count(x => !x.IsApproved && !x.IsBlocked);
        ViewBag.Approved = teachers.Count(x => x.IsApproved && !x.IsBlocked);
        ViewBag.Blocked = teachers.Count(x => x.IsBlocked);
        return View(query.OrderByDescending(x => x.CreatedDate).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(string id) => UpdateStatus(id, approve: true, block: false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Block(string id) => UpdateStatus(id, approve: null, block: true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Unblock(string id) => UpdateStatus(id, approve: null, block: false);

    private async Task<IActionResult> UpdateStatus(string id, bool? approve, bool block)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        var teacher = await _users.FindByIdAsync(id);
        if (teacher is null || !await _users.IsInRoleAsync(teacher, "Teacher")) return NotFound();

        if (approve.HasValue) teacher.IsApproved = approve.Value;
        teacher.IsBlocked = block;
        var result = await _users.UpdateAsync(teacher);
        if (result.Succeeded && block)
            result = await _users.UpdateSecurityStampAsync(teacher);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Teacher account {(teacher.IsBlocked ? "blocked" : teacher.IsApproved ? "approved" : "updated")}."
            : string.Join("; ", result.Errors.Select(x => x.Description));
        return RedirectToAction(nameof(Index));
    }
}