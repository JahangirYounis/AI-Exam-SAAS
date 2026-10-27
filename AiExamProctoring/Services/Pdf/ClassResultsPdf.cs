using AiExamProctoring.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AiExamProctoring.Services.Pdf;

public static class ClassResultsPdf
{
    public static byte[] Generate(string title, IEnumerable<ExamResult> results)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var ordered = results.OrderByDescending(x => x.TotalMarksObtained)
            .ThenBy(x => x.GeneratedDate).ThenBy(x => x.ResultId).ToList();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(s => s.FontSize(10));
            page.Header().Column(c =>
            {
                c.Item().Text("EXAMINATION RESULTS").FontSize(20).Bold().FontColor("165D50");
                c.Item().Text(title).FontSize(14).Bold();
                c.Item().PaddingBottom(15).Text($"Highest marks first | {ordered.Count} completed attempts | Generated {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(9);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c => { c.ConstantColumn(30); c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var label in new[] { "#", "Candidate", "Candidate ID", "Marks", "%" })
                        h.Cell().Background("165D50").Padding(7).Text(label).FontColor(Colors.White).Bold();
                });
                var rank = 0;
                foreach (var result in ordered)
                {
                    rank++;
                    foreach (var value in new[] { rank.ToString(), result.Attempt?.Student?.FullName ?? "Unknown candidate",
                        result.Attempt?.Student?.CandidateNumber ?? $"Attempt {result.AttemptId}",
                        $"{result.TotalMarksObtained:0.##} / {result.TotalMarks:0.##}", $"{result.Percentage:0.##}" })
                        table.Cell().Background(rank % 2 == 0 ? "F1F6F5" : "FFFFFF").BorderBottom(.5f).BorderColor("DCE5E2").Padding(7).Text(value);
                }
            });
            page.Footer().AlignCenter().Text(t => { t.Span("AI Based Online Examination System | Page "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        })).GeneratePdf();
    }
}
