using AiExamProctoring.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AiExamProctoring.Services.Pdf;

public sealed class CertificatePdfService
{
    public byte[] Generate(ExamResult result, IReadOnlyCollection<ProctoringLog> logs,
        byte[]? profileImage, string? examRollNumber, string? verificationUrl = null,
        string? teacherName = null, byte[]? agencyLogo = null, string? agencyName = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var attempt = result.Attempt ?? throw new InvalidOperationException("Attempt data is required.");
        var student = attempt.Student ?? throw new InvalidOperationException("Candidate data is required.");
        var exam = attempt.Exam ?? throw new InvalidOperationException("Exam data is required.");
        var reference = result.CertificateNumber ?? $"EXAM-{result.ResultId:D8}";
        var photo = ReadImage(profileImage);
        var logo = ReadImage(agencyLogo);
        byte[]? qr = null;
        if (!string.IsNullOrWhiteSpace(verificationUrl))
        {
            using var data = QRCodeGenerator.GenerateQrCode(verificationUrl, QRCodeGenerator.ECCLevel.M);
            using var renderer = new PngByteQRCode(data);
            qr = renderer.GetGraphic(8);
        }
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(25);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor("203B42"));
            page.Background().AlignCenter().AlignMiddle().Text("EXAMINATION").FontSize(46).Bold().FontColor("F2F5F3");
            page.Content().Border(2).BorderColor("B89751").Padding(5).Border(.5f).BorderColor("165D50").Padding(18).Column(column =>
            {
                column.Spacing(12);
                column.Item().AlignCenter().Width(44).Height(44).Element(box =>
                {
                    if (logo is not null) box.Image(logo).FitArea();
                    else box.Background("165D50").AlignMiddle().AlignCenter().Text("AI").FontColor(Colors.White).FontSize(22).Bold();
                });
                column.Item().AlignCenter().Text(agencyName ?? "AI Based Online Examination System").FontSize(12).SemiBold();
                column.Item().AlignCenter().Text("OFFICIAL CERTIFICATE OF\nEXAMINATION RESULT").FontSize(20).Bold().FontColor("165D50");
                column.Item().AlignCenter().Text($"Certificate No. {reference}").FontSize(9);
                column.Item().LineHorizontal(1).LineColor("B89751");
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(identity =>
                    {
                        void Field(string label, string? value) => identity.Item().PaddingBottom(5).Text(t =>
                        { t.Span(label + ": ").SemiBold(); t.Span(string.IsNullOrWhiteSpace(value) ? "Not provided" : value); });
                        Field("Candidate name", student.FullName);
                        Field("Candidate ID", student.CandidateNumber);
                        Field("Exam roll number", examRollNumber);
                        if (!string.IsNullOrWhiteSpace(student.CNIC)) Field("CNIC", student.CNIC);
                        Field("Examination", exam.ExamTitle);
                        Field("Exam started", attempt.StartedAt.ToString("dd MMM yyyy, hh:mm tt"));
                        Field("Completed", (attempt.CompletedAt ?? result.GeneratedDate).ToString("dd MMM yyyy, hh:mm tt"));
                    });
                    row.ConstantItem(12);
                    row.ConstantItem(88).Column(picture =>
                    {
                        picture.Item().Height(105).Border(1).BorderColor("CBD8D2").Padding(3).Element(box =>
                        {
                            if (photo is not null) box.Image(photo).FitArea();
                            else box.Background("F1F5F3").AlignCenter().AlignMiddle().Text("Photo unavailable").FontSize(9);
                        });
                        picture.Item().PaddingTop(4).AlignCenter().Text("Registered photo").FontSize(8);
                    });
                });
                column.Item().Background("EDF4F1").Padding(12).Row(row =>
                {
                    void Metric(string label, string value) => row.RelativeItem().AlignCenter().Column(c =>
                    { c.Item().AlignCenter().Text(label).FontSize(8); c.Item().AlignCenter().PaddingTop(5).Text(value).FontSize(15).Bold(); });
                    Metric("MARKS OBTAINED", $"{result.TotalMarksObtained:0.##}");
                    Metric("TOTAL MARKS", $"{result.TotalMarks:0.##}");
                    Metric("PERCENTAGE", $"{result.Percentage:0.##}%");
                    Metric("RESULT", result.IsPassed ? "PASSED" : "FAILED");
                });
                column.Item().AlignCenter().Text($"Correct: {result.CorrectAnswers}    |    Wrong: {result.WrongAnswers}    |    Skipped: {result.SkippedAnswers}").SemiBold();
                column.Item().Text(t => { t.Span("Exam created by: ").SemiBold(); t.Span(teacherName ?? "Teacher record unavailable"); });
                column.Item().PaddingTop(25).Row(row =>
                {
                    row.RelativeItem().Column(c => { c.Item().LineHorizontal(1).LineColor("B89751"); c.Item().PaddingTop(6).Text("Authorized Examiner Signature & Stamp").FontSize(8); });
                    row.ConstantItem(25);
                    row.RelativeItem().Column(c => { c.Item().LineHorizontal(1).LineColor("B89751"); c.Item().PaddingTop(6).Text("Instructor/Teacher Signature").FontSize(8); });
                });
                column.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Column(c =>
                    {
                        c.Item().Text("VERIFY THIS RESULT").FontSize(9).Bold().FontColor("165D50");
                        c.Item().PaddingTop(4).Text("Scan the QR code to check the saved examination record.").FontSize(9);
                        c.Item().PaddingTop(4).Text($"Issued: {result.GeneratedDate:dd MMM yyyy}").FontSize(8);
                    });
                    row.ConstantItem(82).Height(82).Element(code =>
                    { if (qr is not null) code.Image(qr).FitArea(); else code.AlignMiddle().Text("Verification link unavailable").FontSize(8); });
                });
                column.Item().PaddingTop(5).AlignCenter().Text("Generated by AI Based Online Examination System - Valid Official Certificate").FontSize(7).FontColor("687B72");
            });
        })).GeneratePdf();
    }

    private static QuestPDF.Infrastructure.Image? ReadImage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try { return QuestPDF.Infrastructure.Image.FromBinaryData(bytes); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
}
