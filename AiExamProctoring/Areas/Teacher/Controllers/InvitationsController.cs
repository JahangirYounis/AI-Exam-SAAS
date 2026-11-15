using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AiExamProctoring.ViewModels;
using AiExamProctoring.Services.Collaboration;
using System.Data;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher")]
public sealed class InvitationsController(ApplicationDbContext db, IHubContext<TeacherCollaborationHub>? hub = null, IEmailSender? emailSender = null, ILogger<InvitationsController>? logger = null):Controller {
 string TeacherId=>User.FindFirstValue(ClaimTypes.NameIdentifier)!;
 [HttpGet]
 [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
 public async Task<IActionResult> Pending(CancellationToken ct)
 {
     var candidates = await db.ExamInvigilators.AsNoTracking()
         .Include(i => i.Exam).ThenInclude(e => e!.Sessions)
         .Where(i => i.TeacherId == TeacherId && i.RevokedAtUtc == null &&
                     i.AcceptedAtUtc == null && !i.IsActive)
         .ToListAsync(ct);
     return Json(new { count = candidates.Count(i => !CollaborationLifecycle.IsExamPast(i.Exam, DateTime.Now)) });
 }

 [HttpGet]
 public async Task<IActionResult> Index(CancellationToken cancellationToken)
 {
     ViewData["Title"] = "Exam collaborations";
     return View(await LoadCollaborationPageAsync(cancellationToken));
 }

 [HttpGet]
 public async Task<IActionResult> Past(CancellationToken cancellationToken)
 {
     ViewData["Title"] = "Past collaborations";
     return View(await LoadCollaborationPageAsync(cancellationToken));
 }

 private async Task<TeacherCollaborationPageViewModel> LoadCollaborationPageAsync(CancellationToken ct)
 {
     var invitations = await db.ExamInvigilators.AsNoTracking()
         .Include(i => i.Exam).ThenInclude(e => e!.Sessions)
         .Where(i => i.TeacherId == TeacherId)
         .OrderByDescending(i => i.InvitedAtUtc)
         .ToListAsync(ct);

     var events = await db.ExamCollaborationEvents.AsNoTracking()
         .Where(e => e.TeacherId == TeacherId)
         .OrderBy(e => e.Id)
         .ToListAsync(ct);
     var ids = invitations.Select(i => i.Exam?.CreatedBy).Where(id => !string.IsNullOrWhiteSpace(id))
         .Concat(events.Select(e => e.ActorId))
         .Distinct().ToList();
     var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
         .Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
     var eventGroups = events.GroupBy(e => e.InvitationId)
         .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).ToList());

     List<TeacherCollaborationEventViewModel> Timeline(IEnumerable<ExamCollaborationEvent> rows) =>
         rows.Select(e => new TeacherCollaborationEventViewModel
         {
             EventType = e.EventType, ActorName = e.ActorId == "__legacy__" ? "Legacy record (actor unavailable)" : people.GetValueOrDefault(e.ActorId, "Teacher"),
             OccurredAtUtc = e.OccurredAtUtc,
             CanEditQuestions = e.CanEditQuestions, CanMonitor = e.CanMonitor
         }).ToList();

     var page = new TeacherCollaborationPageViewModel();
     foreach (var invitation in invitations)
     {
         var history = eventGroups.GetValueOrDefault(invitation.Id) ?? new List<ExamCollaborationEvent>();
         var last = history.LastOrDefault();
         var declined = invitation.RevokedAtUtc.HasValue && last?.EventType == "Declined";
         var status = CollaborationLifecycle.Classify(invitation, DateTime.Now, declined);
         var card = new TeacherCollaborationCardViewModel
         {
             InvitationId = invitation.Id, ExamId = invitation.ExamId,
             ExamTitle = invitation.Exam?.ExamTitle ?? "Removed examination",
             OwnerName = invitation.Exam?.CreatedBy is string ownerId
                 ? people.GetValueOrDefault(ownerId, "Exam owner") : "Exam owner",
             Status = status, CanEditQuestions = invitation.CanEditQuestions,
             CanMonitor = invitation.CanMonitor, IsReusable = invitation.Exam?.IsReusable ?? false,
             InvitedAtUtc = invitation.InvitedAtUtc, AcceptedAtUtc = invitation.AcceptedAtUtc,
             ClosedAtUtc = invitation.RevokedAtUtc,
             ExamStartLocal = invitation.Exam?.StartDate,
             History = Timeline(history)
         };
         if (status == CollaborationState.Pending) page.Pending.Add(card);
         else if (status == CollaborationState.Active) page.Active.Add(card);
         else page.Past.Add(card);

         // Historical invitation cycles are displayed even if the unique record was
         // subsequently reused for a newer pending/active invitation.
         var terminal = history.Where(e => e.EventType is "Revoked" or "Declined" or "LegacyRevoked").ToList();
         var omitLatestTerminal = status is CollaborationState.Revoked or CollaborationState.Declined
             ? terminal.LastOrDefault()?.Id : null;
         foreach (var eventRow in terminal.Where(e => e.Id != omitLatestTerminal))
         {
             var startEvent = history.LastOrDefault(e => e.Id <= eventRow.Id &&
                 e.EventType is "Invited" or "Reinvited");
             var startId = startEvent?.Id ?? 0;
             var cycle = history.Where(e => e.Id >= startId && e.Id <= eventRow.Id).ToList();
             page.Past.Add(new TeacherCollaborationCardViewModel
             {
                 InvitationId = invitation.Id, ExamId = invitation.ExamId,
                 ExamTitle = card.ExamTitle, OwnerName = card.OwnerName,
                 Status = eventRow.EventType == "Declined" ? CollaborationState.Declined : CollaborationState.Revoked,
                 CanEditQuestions = eventRow.CanEditQuestions, CanMonitor = eventRow.CanMonitor,
                 IsHistoricalCycle = true, IsReusable = card.IsReusable,
                 InvitedAtUtc = startEvent?.OccurredAtUtc ?? invitation.InvitedAtUtc,
                 AcceptedAtUtc = cycle.LastOrDefault(e => e.EventType == "Accepted")?.OccurredAtUtc,
                 ClosedAtUtc = eventRow.OccurredAtUtc, ExamStartLocal = card.ExamStartLocal,
                 History = Timeline(cycle)
             });
         }
     }
     page.Pending.Sort((a,b) => b.InvitedAtUtc.CompareTo(a.InvitedAtUtc));
     page.Active.Sort((a,b) => b.InvitedAtUtc.CompareTo(a.InvitedAtUtc));
     page.Past.Sort((a,b) => (b.ClosedAtUtc ?? b.InvitedAtUtc).CompareTo(a.ClosedAtUtc ?? a.InvitedAtUtc));
     return page;
 }

 private static ExamCollaborationEvent Record(ExamInvigilator invitation, string actor, string type) => new()
 {
     InvitationId = invitation.Id, ExamId = invitation.ExamId,
     TeacherId = invitation.TeacherId, ActorId = actor, EventType = type,
     CanEditQuestions = invitation.CanEditQuestions, CanMonitor = invitation.CanMonitor,
     OccurredAtUtc = DateTime.UtcNow
 };

 private async Task NotifyUsersAsync(IEnumerable<string?> recipients, int examId, string kind, string message)
 {
     if (hub is null) return;
     try
     {
         var groups = recipients.Where(id => !string.IsNullOrWhiteSpace(id))
             .Distinct(StringComparer.Ordinal).Select(id => "collaboration:" + id).ToArray();
         if (groups.Length != 0)
             await hub.Clients.Groups(groups).SendAsync("CollaborationChanged", new { kind, examId, message });
     }
     catch (Exception ex) { logger?.LogWarning(ex, "Collaboration event persisted but live delivery failed for exam {ExamId}", examId); }
 }

 [HttpGet] public async Task<IActionResult> Directory(string? search=null,int page=1,int examId=0){
  page=Math.Clamp(page,1,1000);
  var roles=db.Roles.Where(r=>r.Name=="Teacher").Select(r=>r.Id);
  var ids=db.UserRoles.Where(r=>roles.Contains(r.RoleId)).Select(r=>r.UserId);
  var query=db.Users.AsNoTracking().Where(u=>u.Id!=TeacherId&&u.IsApproved&&!u.IsBlocked&&ids.Contains(u.Id));
  if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim();query=query.Where(u=>u.FullName.Contains(term)||u.Email!=null&&u.Email.Contains(term));}
  var entries=await query.OrderBy(u=>u.FullName).ThenBy(u=>u.Id).Skip((page-1)*50).Take(51).Select(u=>new{u.Id,u.FullName,u.Email}).ToListAsync();
  var visible=entries.Take(50).ToList();
  var invited=examId>0 && await db.Exams.AnyAsync(e=>e.ExamId==examId && e.CreatedBy==TeacherId)
    ? await db.ExamInvigilators.Where(i=>i.ExamId==examId && i.RevokedAtUtc==null).Select(i=>i.TeacherId).Distinct().ToListAsync()
    : new List<string>();
  return Json(new{teachers=visible.Select(t=>new{id=t.Id,name=t.FullName,email=t.Email,invited=invited.Contains(t.Id)}),hasMore=entries.Count>50});
 }

 public async Task<IActionResult> Manage(int examId, bool monitoringOnly=false, string? search=null, int page=1){
  var exam=await db.Exams.AsNoTracking().Include(e=>e.Sessions).SingleOrDefaultAsync(e=>e.ExamId==examId&&e.CreatedBy==TeacherId);if(exam==null)return NotFound();
  var roles=db.Roles.Where(r=>r.Name=="Teacher").Select(r=>r.Id);
  var query=db.Users.AsNoTracking().Where(u=>u.Id!=TeacherId&&u.IsApproved&&!u.IsBlocked&&db.UserRoles.Any(r=>r.UserId==u.Id&&roles.Contains(r.RoleId)));
  if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim();query=query.Where(u=>u.FullName.Contains(term)||u.Email!=null&&u.Email.Contains(term));}
  page=Math.Clamp(page,1,1000);
  var teachers=await query.OrderBy(u=>u.FullName).ThenBy(u=>u.Id).Skip((page-1)*50).Take(51).ToListAsync();
  ViewBag.HasMore=teachers.Count>50;ViewBag.Teachers=teachers.Take(50).ToList();ViewBag.Page=page;
  ViewBag.Search=search;ViewBag.MonitoringOnly=monitoringOnly;ViewBag.Actor=TeacherId;
  ViewBag.Connections=await db.TeacherConnections.AsNoTracking().Where(c=>c.SenderId==TeacherId||c.RecipientId==TeacherId).ToListAsync();
  ViewBag.Exam=exam;
  ViewBag.CanInvite=!CollaborationLifecycle.IsExamPast(exam, DateTime.Now);
  return View(await db.ExamInvigilators.AsNoTracking().Include(i=>i.Teacher).Where(i=>i.ExamId==examId).ToListAsync());
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Invite(int examId,string email, bool canEditQuestions = false, bool canMonitor = false, bool monitoringOnly = false){
  if(monitoringOnly){canEditQuestions=false;canMonitor=true;}
  if (!canEditQuestions && !canMonitor){TempData["Error"]="Select question preparation, monitoring or both.";return RedirectToAction(nameof(Manage),new{examId,monitoringOnly});}
  var ownedExam = await db.Exams.AsNoTracking().Include(e => e.Sessions)
     .FirstOrDefaultAsync(e => e.ExamId == examId && e.CreatedBy == TeacherId);
  if (ownedExam is null) return NotFound();
  if (CollaborationLifecycle.IsExamPast(ownedExam, DateTime.Now))
  { TempData["Error"] = "This examination has ended. New collaboration invitations are closed.";
    return RedirectToAction(nameof(Manage), new { examId, monitoringOnly }); }
  var addresses=(email??"").Split(new[]{',',';','\n','\r'},StringSplitOptions.RemoveEmptyEntries).Select(e=>e.Trim().ToUpperInvariant()).Distinct().ToArray();
  if(addresses.Length is <1 or >50){TempData["Error"]="Select 1 to 50 teachers.";return RedirectToAction(nameof(Manage),new{examId,monitoringOnly});}
  var roleIds=db.Roles.Where(r=>r.Name=="Teacher").Select(r=>r.Id);
  var teacherIds=db.UserRoles.Where(r=>roleIds.Contains(r.RoleId)).Select(r=>r.UserId);
  var teachers=await db.Users.Where(u=>addresses.Contains(u.NormalizedEmail!)&&teacherIds.Contains(u.Id)&&u.IsApproved&&!u.IsBlocked&&u.Id!=TeacherId).ToListAsync();
  if(teachers.Count!=addresses.Length){TempData["Error"]="Some selected accounts are not approved teachers. No invitations were sent.";return RedirectToAction(nameof(Manage),new{examId,monitoringOnly});}
  var notified=new List<AiExamProctoring.Models.ApplicationUser>();
  try {
   await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
    db.ChangeTracker.Clear();
    notified.Clear();
    await using var inviteTransaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
  if(db.Database.IsSqlServer())await db.Database.ExecuteSqlInterpolatedAsync($"SELECT ExamId FROM dbo.Exams WITH (UPDLOCK,HOLDLOCK) WHERE ExamId={examId}");
  var lockedExam = await db.Exams.Include(e=>e.Sessions).SingleOrDefaultAsync(e=>e.ExamId==examId && e.CreatedBy==TeacherId);
  if (lockedExam is null || CollaborationLifecycle.IsExamPast(lockedExam, DateTime.Now))
      throw new InvalidOperationException("The examination is no longer open for invitations.");
  var rows=await db.ExamInvigilators.Where(i=>i.ExamId==examId).ToListAsync();
  var changed = new List<(ExamInvigilator row, string kind)>();
  var knownClosures = await db.ExamCollaborationEvents.AsNoTracking()
      .Where(e => e.ExamId == examId && (e.EventType == "Revoked" || e.EventType == "Declined" || e.EventType == "LegacyRevoked"))
      .Select(e => new { e.InvitationId, e.OccurredAtUtc }).ToListAsync();
  foreach(var teacher in teachers){
   var previous=rows.Where(i=>i.TeacherId==teacher.Id).OrderByDescending(i=>i.RevokedAtUtc==null).ThenByDescending(i=>i.InvitedAtUtc).FirstOrDefault();
   if(CollaborationLifecycle.IsDuplicateInvitation(previous))continue; // The unique active or pending record is retained.
   if(previous is not null){
    // A pre-migration revoked row may be the only surviving evidence of its prior
    // invitation cycle. Snapshot it before the unique row is reused.
    if (previous.RevokedAtUtc.HasValue && !knownClosures.Any(e =>
        e.InvitationId == previous.Id && e.OccurredAtUtc >= previous.InvitedAtUtc))
    {
        db.ExamCollaborationEvents.Add(new ExamCollaborationEvent
        {
            InvitationId = previous.Id, ExamId = previous.ExamId,
            TeacherId = previous.TeacherId, ActorId = "__legacy__",
            EventType = "LegacyRevoked", OccurredAtUtc = previous.RevokedAtUtc.Value,
            CanEditQuestions = previous.CanEditQuestions, CanMonitor = previous.CanMonitor
        });
    }
    previous.RevokedAtUtc=null;previous.AcceptedAtUtc=null;previous.IsActive=false;
    previous.CanEditQuestions=canEditQuestions;previous.CanMonitor=canMonitor;
    previous.InvitedAtUtc=DateTime.UtcNow;
    changed.Add((previous, "Reinvited"));
   } else {
    var row = new ExamInvigilator { ExamId=examId, TeacherId=teacher.Id,
        IsActive=false, AcceptedAtUtc=null, CanEditQuestions=canEditQuestions, CanMonitor=canMonitor };
    db.ExamInvigilators.Add(row);
    changed.Add((row, "Invited"));
   }
   notified.Add(teacher);
  }
    await db.SaveChangesAsync(); // Generate invitation primary keys before recording events.
    foreach (var (row, kind) in changed)
        db.ExamCollaborationEvents.Add(Record(row, TeacherId, kind));
    await db.SaveChangesAsync();
    await inviteTransaction.CommitAsync();
   });
  }
  catch(Exception ex) when (ex is DbUpdateException or InvalidOperationException)
  { logger?.LogWarning(ex, "Collaboration invite failed for exam {ExamId}", examId);
    TempData["Error"]="Invitations could not be saved or the exam is no longer open. Refresh and try again.";
    return RedirectToAction(nameof(Manage),new{examId,monitoringOnly}); }
  foreach(var teacher in notified){
   try {
    if(hub is not null)await hub.Clients.Group("collaboration:"+teacher.Id).SendAsync("CollaborationChanged",new{message="You received an exam invitation.",kind="invitation",examId});
    if(emailSender is not null && teacher.Email is not null)await emailSender.SendEmailAsync(teacher.Email,"Exam collaboration invitation","You were invited to collaborate on an examination. Open Teacher / Invitations to review and accept.");
   }catch(Exception){TempData["Error"]="Invitation saved; one or more delivery notifications failed.";}
  }
  if(notified.Count==0)TempData["Success"]="These teachers were already invited. No duplicate invitations were sent.";
  else TempData["Success"]=$"Sent {notified.Count} new invitation(s). Already invited teachers were skipped.";
  return RedirectToAction(nameof(Manage),new{examId,monitoringOnly});
 }
 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Accept(int id, CancellationToken ct)
 {
     var actor = TeacherId;
     var invitation = await db.ExamInvigilators.AsNoTracking().Include(i => i.Exam)
         .ThenInclude(e => e!.Sessions)
         .FirstOrDefaultAsync(i => i.Id == id && i.TeacherId == actor && i.RevokedAtUtc == null, ct);
     if (invitation is null) return NotFound();
     if (CollaborationLifecycle.IsExamPast(invitation.Exam, DateTime.Now))
     { TempData["Error"] = "This invitation has expired because the examination ended.";
       return RedirectToAction(nameof(Past)); }
     if (invitation.AcceptedAtUtc.HasValue && invitation.IsActive)
         return RedirectToAction(nameof(Index));
     if (!invitation.CanEditQuestions && !invitation.CanMonitor) return BadRequest("No collaboration permission is assigned.");

     var changed = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
     {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
         var row = await db.ExamInvigilators.Include(i => i.Exam).ThenInclude(e => e!.Sessions)
             .FirstOrDefaultAsync(i => i.Id == id && i.TeacherId == actor, ct);
         if (row is null || row.RevokedAtUtc.HasValue ||
             CollaborationLifecycle.IsExamPast(row.Exam, DateTime.Now)) return false;
         if (row.AcceptedAtUtc.HasValue && row.IsActive) return false;
         row.AcceptedAtUtc = DateTime.UtcNow;
         row.IsActive = true;
         db.ExamCollaborationEvents.Add(Record(row, actor, "Accepted"));
         await db.SaveChangesAsync(ct);
         await tx.CommitAsync(ct);
         return true;
     });
     if (changed) await NotifyUsersAsync(new[] { invitation.Exam?.CreatedBy, actor }, invitation.ExamId,
         "accepted", "A collaboration invitation was accepted.");
     return RedirectToAction(nameof(Index));
 }

 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Decline(int id, CancellationToken ct)
 {
     var actor = TeacherId;
     var row = await db.ExamInvigilators.Include(i => i.Exam)
         .FirstOrDefaultAsync(i => i.Id == id && i.TeacherId == actor &&
             i.RevokedAtUtc == null && i.AcceptedAtUtc == null && !i.IsActive, ct);
     if (row is null) return NotFound();
     var owner = row.Exam?.CreatedBy;
     row.IsActive = false;
     row.RevokedAtUtc = DateTime.UtcNow;
     db.ExamCollaborationEvents.Add(Record(row, actor, "Declined"));
     await db.SaveChangesAsync(ct);
     await NotifyUsersAsync(new[] { actor, owner }, row.ExamId, "declined", "An exam invitation was declined.");
     TempData["Success"] = "Invitation declined. It remains in Past Collaborations.";
     return RedirectToAction(nameof(Past));
 }

 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Revoke(int id, CancellationToken ct)
 {
     var actor = TeacherId;
     var row = await db.ExamInvigilators.Include(i => i.Exam)
         .FirstOrDefaultAsync(i => i.Id == id && i.Exam!.CreatedBy == actor, ct);
     if (row is null) return NotFound();
     if (row.RevokedAtUtc.HasValue) return RedirectToAction(nameof(Manage), new { examId = row.ExamId });
     row.IsActive = false;
     row.RevokedAtUtc = DateTime.UtcNow;
     db.ExamCollaborationEvents.Add(Record(row, actor, "Revoked"));
     await db.SaveChangesAsync(ct);
     await NotifyUsersAsync(new[] { actor, row.TeacherId }, row.ExamId, "revoked", "Your exam collaboration permissions changed.");
     TempData["Success"] = "Collaboration revoked. Its history remains available.";
     return RedirectToAction(nameof(Manage), new { examId = row.ExamId });
 }

 private Task<bool> TeamAccess(int examId) => db.Exams.AnyAsync(e => e.ExamId == examId &&
     (e.CreatedBy == TeacherId || db.ExamInvigilators.Any(i => i.ExamId == examId &&
       i.TeacherId == TeacherId && i.IsActive && i.AcceptedAtUtc != null && i.RevokedAtUtc == null &&
       (i.CanMonitor || i.CanEditQuestions))));

 [HttpGet] public async Task<IActionResult> Team(int examId) {
  if (!await TeamAccess(examId)) return NotFound();
  ViewBag.ExamId = examId;
  var exam = await db.Exams.AsNoTracking().Include(e=>e.Sessions).FirstOrDefaultAsync(e=>e.ExamId==examId);
  var isOwner = exam?.CreatedBy == TeacherId;
  var granted = isOwner ? null : await db.ExamInvigilators.AsNoTracking()
      .FirstOrDefaultAsync(i => i.ExamId==examId && i.TeacherId==TeacherId &&
         i.IsActive && i.AcceptedAtUtc!=null && i.RevokedAtUtc==null);
  ViewBag.CanEditQuestions = isOwner || granted?.CanEditQuestions==true;
  ViewBag.CanMonitor = isOwner || granted?.CanMonitor==true;
  ViewBag.IsPast = CollaborationLifecycle.IsExamPast(exam, DateTime.Now);
  var senders = db.ExamTeamMessages.Where(m => m.ExamId == examId).Select(m => m.SenderId);
  ViewBag.Names = await db.Users.Where(u => senders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
  return View(await db.ExamTeamMessages.AsNoTracking().Where(m => m.ExamId == examId).OrderByDescending(m => m.Id).Take(100).OrderBy(m => m.Id).ToListAsync());
 }
 [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
 public async Task<IActionResult> Messages(int examId, int after = 0) {
  if (!await TeamAccess(examId)) return NotFound();
  after = Math.Max(0,after);
  var query = db.ExamTeamMessages.AsNoTracking().Where(m => m.ExamId == examId);
  var raw = after == 0 ? await query.OrderByDescending(m=>m.Id).Take(100).ToListAsync() :
    await query.Where(m=>m.Id>after).OrderBy(m=>m.Id).Take(100).ToListAsync();
  var senders = raw.Select(m=>m.SenderId).Distinct().ToArray();
  var names = await db.Users.Where(u=>senders.Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.FullName);
  var entries = raw.OrderBy(m=>m.Id).Select(m=>new TeacherChatEntry {
    Kind="team", RoomId=examId, EntryId=m.Id, Body=m.Body, SenderId=m.SenderId,
    Sender=names.GetValueOrDefault(m.SenderId,"Teacher"), SentAtUtc=m.SentAtUtc }).ToList();
  await TeacherChatStore.EnrichAsync(db,"team",examId,TeacherId,entries);
  foreach(var entry in entries.Where(e=>e.ReplyToId.HasValue)) {
   var quoted=entries.FirstOrDefault(e=>e.EntryId==entry.ReplyToId);
   entry.ReplyBody=quoted?.Body;
   entry.ReplySender=quoted?.Sender;
  }
  return Json(entries);
 }
 [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Message(int examId, string body, int? replyToId = null) {
  if (!await TeamAccess(examId)) return NotFound();
  var targetExam = await db.Exams.AsNoTracking().Include(e=>e.Sessions)
      .FirstOrDefaultAsync(e=>e.ExamId==examId);
  if (CollaborationLifecycle.IsExamPast(targetExam, DateTime.Now))
      return Conflict("This collaboration is archived. Team messages are read-only.");
  body = body?.Trim() ?? "";
  if (body.Length is < 1 or > 2000) return BadRequest("Message must contain 1–2000 characters.");
  if(replyToId.HasValue && !await db.ExamTeamMessages.AnyAsync(m=>m.ExamId==examId&&m.Id==replyToId.Value))return BadRequest("Reply target is not in this discussion.");
  ExamTeamMessage message = null!;
  await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
   db.ChangeTracker.Clear();
   await using var transaction = await db.Database.BeginTransactionAsync();
   message = new ExamTeamMessage { ExamId=examId,SenderId=TeacherId,Body=body };
   db.ExamTeamMessages.Add(message); await db.SaveChangesAsync();
   if(replyToId.HasValue)await TeacherChatStore.SaveReplyAsync(db,"team",examId,message.Id,replyToId.Value);
   await transaction.CommitAsync();
  });
  if(hub is not null) {
   var recipients = await db.ExamInvigilators.Where(i=>i.ExamId==examId && i.IsActive && i.AcceptedAtUtc!=null && i.RevokedAtUtc==null && (i.CanEditQuestions || i.CanMonitor)).Select(i=>i.TeacherId).ToListAsync();
   var owner = await db.Exams.Where(e=>e.ExamId==examId).Select(e=>e.CreatedBy).SingleAsync();
   if(owner is not null)recipients.Add(owner);
   try { await hub.Clients.Groups(recipients.Distinct().Select(id=>"collaboration:"+id).ToArray()).SendAsync("CollaborationChanged",new {message="New exam team message",kind="team",roomId=examId,entryId=message.Id,body,sender=await db.Users.Where(u=>u.Id==TeacherId).Select(u=>u.FullName).FirstOrDefaultAsync(),sentAtUtc=message.SentAtUtc}); }
   catch(Exception ex){logger?.LogWarning(ex,"Exam team message notification failed; message remains saved. MessageId={MessageId}",message.Id);}
  }
  if(Request.Headers["X-Requested-With"]=="XMLHttpRequest")return Ok(new {ok=true});
  return RedirectToAction(nameof(Team),new{examId});
 }
}
