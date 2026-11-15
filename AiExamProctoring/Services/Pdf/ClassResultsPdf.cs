using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
namespace AiExamProctoring.Services.Pdf;
public sealed record ExaminationReportRow(string Name,string RollNumber,decimal? Obtained,decimal? Total,decimal? Percentage,bool? Passed,string Publication);
public static class ClassResultsPdf
{
 public static byte[] Generate(string title,DateTime examinationDate,IReadOnlyCollection<ExaminationReportRow> rows)
 {
  QuestPDF.Settings.License=LicenseType.Community;
  return Document.Create(doc=>doc.Page(page=>{
   page.Size(PageSizes.A4.Landscape());page.Margin(30);page.DefaultTextStyle(s=>s.FontSize(9));
   page.Header().PaddingBottom(12).Column(c=>{
    c.Item().Text("EXAMINATION RESULT REPORT").FontSize(20).Bold().FontColor("165D50");
    c.Item().Text(title).FontSize(13).SemiBold();
    c.Item().Text($"Examination date: {examinationDate:dd MMM yyyy} | {rows.Count} candidate records | Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(9);
    c.Item().Text("Published marks only. Withheld and unfinalized marks are not disclosed.").FontSize(8);
   });
   page.Content().Table(t=>{
    t.ColumnsDefinition(c=>{c.ConstantColumn(40);c.RelativeColumn(3);c.RelativeColumn(2);c.RelativeColumn(1.5f);c.RelativeColumn();c.RelativeColumn();c.RelativeColumn(2);});
    t.Header(h=>{foreach(var label in new[]{"#","Candidate","Exam roll number","Marks","%","Result","Publication"})h.Cell().Background("165D50").Padding(7).Text(label).FontColor(Colors.White).SemiBold();});
    int n=0;foreach(var r in rows){n++;foreach(var value in new[]{n.ToString(),r.Name,r.RollNumber,r.Obtained.HasValue?$"{r.Obtained:0.##} / {r.Total:0.##}":"Not disclosed",r.Percentage?.ToString("0.##")??"-",r.Passed.HasValue?(r.Passed.Value?"Pass":"Fail"):"-",r.Publication})
     t.Cell().Background(n%2==0?"F1F6F5":"FFFFFF").BorderBottom(.5f).BorderColor("DCE5E2").Padding(7).Text(value);
    }
   });
   page.Footer().AlignCenter().Text(t=>{t.Span("AI Based Online Examination System | Owner export | Page ");t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
  })).GeneratePdf();
 }
}

