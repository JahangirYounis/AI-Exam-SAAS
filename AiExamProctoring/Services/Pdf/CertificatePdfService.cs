using AiExamProctoring.Models;
using AiExamProctoring.Services.Results;
using System.Text.Json;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
namespace AiExamProctoring.Services.Pdf;
public sealed class CertificatePdfService
{
 public byte[] Generate(ExamResult result, IReadOnlyCollection<ProctoringLog> logs, byte[]? profileImage,
    string? examRollNumber, string? verificationUrl=null, string? teacherName=null, byte[]? agencyLogo=null, string? agencyName=null)
 {
  QuestPDF.Settings.License=LicenseType.Community;
  var s=JsonSerializer.Deserialize<CertificateSnapshot>(result.CertificateSnapshotJson ?? "null")
      ?? throw new InvalidOperationException("Issue a persisted certificate record before rendering.");
  QuestPDF.Infrastructure.Image? photo=null;
  if(s.PhotoSha256!=null){
   if(profileImage==null || !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(profileImage)),s.PhotoSha256,StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Original attempt photograph is missing or changed.");
   photo=QuestPDF.Infrastructure.Image.FromBinaryData(profileImage);
  }
  byte[]? qr=null;
  if(!string.IsNullOrWhiteSpace(verificationUrl)){
   using var data=QRCodeGenerator.GenerateQrCode(verificationUrl,QRCodeGenerator.ECCLevel.M);
   using var renderer=new PngByteQRCode(data);qr=renderer.GetGraphic(8);
  }
  return Document.Create(doc=>doc.Page(page=>{
   page.Size(PageSizes.A4);page.Margin(30);page.DefaultTextStyle(x=>x.FontSize(10).FontColor("203B42"));
   page.Content().Border(2).BorderColor("B89751").Padding(5).Border(.5f).BorderColor("165D50").Padding(20).Column(c=>{
    c.Spacing(13);
    c.Item().AlignCenter().Text(agencyName??"AI Based Online Examination System").FontSize(13).SemiBold().FontColor("165D50");
    c.Item().AlignCenter().Text(s.DocumentType.ToUpperInvariant()).FontSize(22).Bold();
    c.Item().AlignCenter().Text("Record No. "+s.Number).FontSize(9);
    c.Item().LineHorizontal(1).LineColor("B89751");
    c.Item().Row(row=>{
     row.RelativeItem().Column(identity=>{
      void Field(string label,string value)=>identity.Item().PaddingBottom(7).Text(t=>{t.Span(label+": ").SemiBold();t.Span(value);});
      Field("Candidate",s.StudentName);Field("Exam roll number",s.RollNumber);Field("Candidate number",s.CandidateNumber);
      Field("Examination",s.Examination);Field("Test date",s.ExaminationDate.ToString("dd MMM yyyy"));
     });
     row.ConstantItem(14);row.ConstantItem(90).Column(p=>{
      p.Item().Height(112).Border(.5f).BorderColor("B9C9C2").Padding(3).Element(box=>{
       if(photo!=null)box.Image(photo).FitArea();else box.AlignMiddle().AlignCenter().Text("Original attempt photo unavailable").FontSize(8);
      });p.Item().PaddingTop(4).AlignCenter().Text("Original examination photo").FontSize(7);
     });
    });
    c.Item().Background("EDF4F1").Padding(12).Row(row=>{
     void Metric(string label,string value)=>row.RelativeItem().Column(m=>{m.Item().AlignCenter().Text(label).FontSize(8);m.Item().AlignCenter().PaddingTop(6).Text(value).FontSize(14).Bold();});
     Metric("MARKS",$"{s.Obtained:0.##} / {s.Total:0.##}");Metric("PERCENTAGE",$"{s.Percentage:0.##}%");Metric("RESULT",s.Passed?"PASS":"FAIL");Metric("GRADE",s.Grade);
    });
    c.Item().Text(s.Completion).FontSize(10).SemiBold();
    c.Item().Text("Academic marks are recorded independently of integrity observations.").FontSize(9).FontColor("586E65");
    c.Item().PaddingTop(35).LineHorizontal(.7f).LineColor("B89751");
    c.Item().AlignCenter().Text("Authorized Examination Controller / Director").FontSize(10).SemiBold();
    c.Item().AlignCenter().Text("Signature and official stamp area").FontSize(8);
    c.Item().PaddingTop(10).Row(row=>{
     row.RelativeItem().AlignMiddle().Column(v=>{
      v.Item().Text("VERIFY AUTHENTICITY").SemiBold();v.Item().PaddingTop(5).Text("Scan to check the current validity of this record. Verification does not disclose the candidate photograph.").FontSize(9);
      v.Item().PaddingTop(7).Text($"Certificate issue date: {s.IssuedAtUtc:dd MMM yyyy} UTC").FontSize(9);
     });row.ConstantItem(10);row.ConstantItem(88).Height(88).Element(b=>{if(qr!=null)b.Image(qr).FitArea();else b.Text("Verification unavailable").FontSize(8);});
    });
   });
   page.Footer().PaddingTop(8).AlignCenter().Text(t=>{t.Span("AI Based Online Examination System | Private candidate copy | ");t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
  })).GeneratePdf();
 }
}
