using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AiExamProctoring.Data;
using AiExamProctoring.Services.Results;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Controllers;
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CertificateController(ApplicationDbContext db,IDataProtectionProvider protection):Controller
{
 public const string Purpose=CertificateRecords.TokenPurpose;
 [AllowAnonymous,HttpGet("certificate/verify")]
 public async Task<IActionResult> Verify(string? token,CancellationToken ct)
 {
  Response.Headers["X-Robots-Tag"]="noindex, nofollow";Response.Headers["Referrer-Policy"]="no-referrer";
  if(string.IsNullOrWhiteSpace(token)||token.Length>2048)return NotFound();
  string reference;
  try{reference=protection.CreateProtector(Purpose).Unprotect(token);}catch(CryptographicException){return NotFound();}
  if(reference.Length!=64)return NotFound();
  var result=await db.ExamResults.AsNoTracking().Include(r=>r.Attempt).SingleOrDefaultAsync(r=>r.CertificateReference==reference,ct);
  if(result?.CertificateSnapshotJson==null)return NotFound();
  var snapshot=JsonSerializer.Deserialize<CertificateSnapshot>(result.CertificateSnapshotJson);
  if(snapshot==null)return NotFound();
  return View(new CertificateVerification(snapshot.Number,snapshot.Examination,CertificateRecords.Status(result),snapshot.DocumentType,snapshot.IssuedAtUtc));
 }
 [Authorize,HttpGet("certificate/photo/{attemptId:int}")]
 public async Task<IActionResult> Photo(int attemptId,CancellationToken ct)
 {
  var actor=User.FindFirstValue(ClaimTypes.NameIdentifier);if(actor==null)return Challenge();
  var allowed=User.IsInRole("Student")&&await db.ExamAttempts.AnyAsync(a=>a.AttemptId==attemptId&&a.StudentId==actor&&a.ResultReleasedAtUtc!=null,ct)
   ||User.IsInRole("Teacher")&&await TeacherEvidenceAccess.Attempts(db,actor).AnyAsync(a=>a.AttemptId==attemptId,ct);
  if(!allowed)return NotFound();
  var photo=await db.AttemptIdentityPhotos.AsNoTracking().SingleOrDefaultAsync(p=>p.AttemptId==attemptId,ct);
  if(!CertificateRecords.ValidPhoto(photo))return NotFound();
  var bytes=photo!.ImageBytes;
  var png=bytes.Length>=8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71;
  if(!png&&!(bytes.Length>3&&bytes[0]==255&&bytes[1]==216))return NotFound();
  Response.Headers.CacheControl="private,no-store,max-age=0";Response.Headers["X-Content-Type-Options"]="nosniff";
  return File(bytes,png?"image/png":"image/jpeg");
 }
}
