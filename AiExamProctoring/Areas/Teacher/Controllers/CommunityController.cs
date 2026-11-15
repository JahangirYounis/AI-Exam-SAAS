using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommunityController(ApplicationDbContext db) : Controller
{
    private const int PageSize = 12;
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private IQueryable<TeacherCommunityPost> VisiblePosts() => db.TeacherCommunityPosts.AsNoTracking()
        .Where(p => !p.IsDeleted && db.Users.Any(u => u.Id == p.AuthorId && u.IsApproved && !u.IsBlocked)
            && (p.AuthorId == Actor || p.Visibility == TeacherPostVisibility.Teachers ||
                db.TeacherConnections.Any(c => c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null &&
                    ((c.SenderId == Actor && c.RecipientId == p.AuthorId) ||
                     (c.RecipientId == Actor && c.SenderId == p.AuthorId)))));

    private IQueryable<TeacherPostCardViewModel> Project(IQueryable<TeacherCommunityPost> query) =>
        query.Select(p => new TeacherPostCardViewModel
        {
            Id = p.Id,
            AuthorId = p.AuthorId,
            AuthorName = db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
            AuthorPhoto = db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.ProfileImage).FirstOrDefault(),
            Department = db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.Department).FirstOrDefault(),
            Body = p.Body,
            Visibility = p.Visibility,
            CreatedAtUtc = p.CreatedAtUtc,
            LikeCount = db.TeacherPostLikes.Count(l => l.PostId == p.Id),
            CommentCount = db.TeacherPostComments.Count(c => c.PostId == p.Id && !c.IsDeleted),
            LikedByMe = db.TeacherPostLikes.Any(l => l.PostId == p.Id && l.TeacherId == Actor),
            IsMine = p.AuthorId == Actor
        });

    [HttpGet]
    public async Task<IActionResult> Index(string filter = "all", int page = 1, CancellationToken ct = default)
    {
        filter = filter is "mine" or "connections" ? filter : "all";
        page = Math.Clamp(page, 1, 10_000);
        var query = VisiblePosts();
        if (filter == "mine") query = query.Where(p => p.AuthorId == Actor);
        else if (filter == "connections") query = query.Where(p => p.AuthorId != Actor &&
            db.TeacherConnections.Any(c => c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null &&
                ((c.SenderId == Actor && c.RecipientId == p.AuthorId) || (c.RecipientId == Actor && c.SenderId == p.AuthorId))));
        var rows = await Project(query.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id))
            .Skip((page - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct);
        var connections = db.TeacherConnections.AsNoTracking().Where(c => c.SenderId == Actor || c.RecipientId == Actor);
        return View(new TeacherCommunityIndexViewModel
        {
            CurrentTeacherId = Actor,
            Filter = filter,
            Page = page,
            HasNext = rows.Count > PageSize,
            Posts = rows.Take(PageSize).ToList(),
            PendingRequests = await connections.CountAsync(c => c.RecipientId == Actor && c.AcceptedAtUtc == null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null, ct),
            ConnectionCount = await connections.CountAsync(c => c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null, ct)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string body, TeacherPostVisibility visibility, CancellationToken ct)
    {
        body = body?.Trim() ?? "";
        if (body.Length is < 1 or > 2000 || !Enum.IsDefined(visibility)) return BadRequest("A post must contain 1–2000 characters and a valid audience.");
        db.TeacherCommunityPosts.Add(new TeacherCommunityPost { AuthorId = Actor, Body = body, Visibility = visibility, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Your post was published to the selected Teacher audience.";
        return RedirectToAction(nameof(Index), new { filter = "mine" });
    }

    [HttpGet]
    public async Task<IActionResult> Post(int id, CancellationToken ct)
    {
        var card = await Project(VisiblePosts().Where(p => p.Id == id)).SingleOrDefaultAsync(ct);
        if (card is null) return NotFound();
        var comments = await db.TeacherPostComments.AsNoTracking().Where(c => c.PostId == id && !c.IsDeleted)
            .OrderByDescending(c => c.Id).Take(50).OrderBy(c => c.Id)
            .Select(c => new TeacherPostCommentViewModel {
                Id = c.Id, AuthorId = c.AuthorId,
                AuthorName = db.Users.Where(u => u.Id == c.AuthorId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
                Body = c.Body, CreatedAtUtc = c.CreatedAtUtc, IsMine = c.AuthorId == Actor
            }).ToListAsync(ct);
        return View(new TeacherPostDetailsViewModel { Post = card, Comments = comments, TotalComments = card.CommentCount });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Comment(int id, string body, CancellationToken ct)
    {
        if (!await VisiblePosts().AnyAsync(p => p.Id == id, ct)) return NotFound();
        body = body?.Trim() ?? "";
        if (body.Length is < 1 or > 600) return BadRequest("Comments must be 1–600 characters.");
        db.TeacherPostComments.Add(new TeacherPostComment { PostId = id, AuthorId = Actor, Body = body, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Post), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Like(int id, bool liked, string? returnTo, CancellationToken ct)
    {
        if (!await VisiblePosts().AnyAsync(p => p.Id == id, ct)) return NotFound();
        var existing = await db.TeacherPostLikes.SingleOrDefaultAsync(l => l.PostId == id && l.TeacherId == Actor, ct);
        if (liked && existing is null) db.TeacherPostLikes.Add(new TeacherPostLike { PostId = id, TeacherId = Actor });
        else if (!liked && existing is not null) db.TeacherPostLikes.Remove(existing);
        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Conflict("This reaction changed in another request. Refresh the page."); }
        }
        return returnTo == "post" ? RedirectToAction(nameof(Post), new { id }) : RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var post = await db.TeacherCommunityPosts.FirstOrDefaultAsync(p => p.Id == id && p.AuthorId == Actor && !p.IsDeleted, ct);
        if (post is null) return NotFound();
        post.IsDeleted = true; // Preserve history without continuing public visibility.
        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Your post was removed from the community.";
        return RedirectToAction(nameof(Index), new { filter = "mine" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int id, CancellationToken ct)
    {
        var comment = await db.TeacherPostComments.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted &&
            (c.AuthorId == Actor || db.TeacherCommunityPosts.Any(p => p.Id == c.PostId && p.AuthorId == Actor)), ct);
        if (comment is null || !await VisiblePosts().AnyAsync(p => p.Id == comment.PostId, ct)) return NotFound();
        comment.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Post), new { id = comment.PostId });
    }

    [HttpGet]
    public async Task<IActionResult> Profile(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        var roleIds = db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        var person = await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.IsApproved && !u.IsBlocked && db.UserRoles.Any(r => r.UserId == u.Id && roleIds.Contains(r.RoleId)))
            .Select(u => new { u.Id, u.FullName, u.ProfileImage, u.Department, u.OrganizationName, u.HighestQualification, u.TeacherHeadline, u.TeacherBio, u.City, u.Country })
            .SingleOrDefaultAsync(ct);
        if (person is null) return NotFound();
        var connection = await db.TeacherConnections.AsNoTracking()
            .Where(c => (c.SenderId == Actor && c.RecipientId == id) || (c.RecipientId == Actor && c.SenderId == id))
            .Select(c => new { c.Id, c.SenderId, c.RecipientId, c.AcceptedAtUtc, c.DeclinedAtUtc, c.ClosedAtUtc })
            .SingleOrDefaultAsync(ct);
        var status = id == Actor ? "self" : connection is null ? "none" :
            connection.ClosedAtUtc != null ? "removed" : connection.DeclinedAtUtc != null ? "declined" :
            connection.AcceptedAtUtc != null ? "connected" : connection.RecipientId == Actor ? "received" : "sent";
        var posts = await Project(VisiblePosts().Where(p => p.AuthorId == id).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id))
            .Take(20).ToListAsync(ct);
        return View(new TeacherPublicCommunityProfileViewModel
        {
            Id = person.Id, FullName = person.FullName, ProfileImage = person.ProfileImage,
            Department = person.Department, OrganizationName = person.OrganizationName,
            HighestQualification = person.HighestQualification, TeacherHeadline = person.TeacherHeadline,
            TeacherBio = person.TeacherBio, City = person.City, Country = person.Country,
            IsOwnProfile = id == Actor, ConnectionId = connection?.Id, ConnectionStatus = status, Posts = posts
        });
    }
}
