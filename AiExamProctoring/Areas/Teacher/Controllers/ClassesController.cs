using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"), Authorize(Roles="Teacher")]
public sealed class ClassesController(ApplicationDbContext db) : Controller {
 string TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
 public async Task<IActionResult> Index() => View(await db.AcademicClasses.AsNoTracking().Include(c=>c.Members).Where(c=>c.TeacherId==TeacherId).OrderBy(c=>c.Name).ToListAsync());
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Create(string name) {
  if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>120)return BadRequest("Enter a class name up to 120 characters.");
  db.AcademicClasses.Add(new AcademicClass{Name=name.Trim(),TeacherId=TeacherId});await db.SaveChangesAsync();return RedirectToAction(nameof(Index));
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> CreateTwelve() {
  await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
   for(int n=1;n<=12;n++){var name=$"Class {n:00}";if(!await db.AcademicClasses.AnyAsync(c=>c.TeacherId==TeacherId&&c.Name==name))db.AcademicClasses.Add(new AcademicClass{Name=name,TeacherId=TeacherId});}
   await db.SaveChangesAsync();await tx.CommitAsync();});return RedirectToAction(nameof(Index));
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Rename(int id,string name) {
  var c=await db.AcademicClasses.SingleOrDefaultAsync(c=>c.Id==id&&c.TeacherId==TeacherId);if(c==null)return NotFound();if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>120)return BadRequest();c.Name=name.Trim();await db.SaveChangesAsync();return RedirectToAction(nameof(Roster),new{id});
 }
 public async Task<IActionResult> Roster(int id) {
  var c=await db.AcademicClasses.Include(c=>c.Members).SingleOrDefaultAsync(c=>c.Id==id&&c.TeacherId==TeacherId);if(c==null)return NotFound();
  var roleIds=db.Roles.Where(r=>r.Name=="Student").Select(r=>r.Id);
  var students=db.UserRoles.Where(r=>roleIds.Contains(r.RoleId)).Select(r=>r.UserId);
  ViewBag.Students=await db.Users.AsNoTracking().Where(u=>students.Contains(u.Id)&&u.IsApproved&&!u.IsBlocked&&!db.ClassMemberships.Any(m=>m.StudentId==u.Id&&m.IsActive&&m.AcademicClassId!=id)).OrderBy(u=>u.FullName).ToListAsync();return View(c);
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Roster(int id,string[] studentIds) {
  return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async()=> {
   db.ChangeTracker.Clear();
   await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable):null;
   var c=await db.AcademicClasses.Include(c=>c.Members).SingleOrDefaultAsync(c=>c.Id==id&&c.TeacherId==TeacherId);if(c==null)return NotFound();
   var ids=(studentIds??Array.Empty<string>()).Distinct().Take(2001).ToArray();if(ids.Length>2000)return BadRequest();
   var roles=db.Roles.Where(r=>r.Name=="Student").Select(r=>r.Id);
   var allowed=db.UserRoles.Where(r=>roles.Contains(r.RoleId)).Select(r=>r.UserId);
   if(await db.Users.CountAsync(u=>ids.Contains(u.Id)&&allowed.Contains(u.Id)&&u.IsApproved&&!u.IsBlocked)!=ids.Length)return BadRequest("Only approved student accounts can be enrolled.");
   if(await db.ClassMemberships.AnyAsync(m=>ids.Contains(m.StudentId)&&m.IsActive&&m.AcademicClassId!=id))return Conflict("A selected student already belongs to another class. Remove their old membership before moving them.");
   foreach(var m in c.Members)m.IsActive=ids.Contains(m.StudentId);
   foreach(var sid in ids.Where(s=>!c.Members.Any(m=>m.StudentId==s)))c.Members.Add(new ClassMembership{StudentId=sid});
   await db.SaveChangesAsync();if(tx!=null)await tx.CommitAsync();
   TempData["Success"]="Approved roster saved. Each student can belong to one active class.";return RedirectToAction(nameof(Roster),new{id});
  });
 }
}
