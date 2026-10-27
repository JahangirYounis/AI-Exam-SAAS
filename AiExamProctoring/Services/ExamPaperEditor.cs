using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services;

// Shared paper updates for bank selection and newly authored questions.
public sealed class ExamPaperEditor(ApplicationDbContext db)
{
    private readonly ApplicationDbContext _db = db;
    public async Task AddAsync(AiExamProctoring.Models.Exam exam, Question question)
    {
        if (exam.IsPublished || exam.IsLocked)
            throw new InvalidOperationException("Published or locked examinations cannot be changed.");
        if (await _db.ExamAttempts.AnyAsync(x => x.ExamId == exam.ExamId))
            throw new InvalidOperationException("An examination with attempts cannot change its questions.");
        if (exam.ExamQuestions.Any(x => x.QuestionId == question.QuestionId && x.IsActive)) return;
        if (exam.QuestionLimit.HasValue && exam.ExamQuestions.Count(x => x.IsActive) >= exam.QuestionLimit.Value)
            throw new InvalidOperationException("The requested question limit has been reached. Remove a question before adding another.");
        if (exam.TotalMarks < (exam.ExamQuestions.Count(x => x.IsActive) + 1) * .01m)
            throw new InvalidOperationException("Total Marks are too small for another question.");
        var examId = exam.ExamId;
        var questionId = question.QuestionId;
        var nextOrder = exam.ExamQuestions.Where(x => x.IsActive).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
        exam.ExamQuestions.Add(new ExamQuestion
        {
            ExamId = examId,
            QuestionId = questionId,
            Marks = 0.01m,
            NegativeMarks = exam.NegativeMarksPerWrongAnswer,
            DisplayOrder = nextOrder,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        var part = exam.SubjectParts.FirstOrDefault(x => x.SubjectId == question.SubjectId);
        if (part is null)
        {
            part = new ExamSubjectPart
            {
                ExamId = examId,
                SubjectId = question.SubjectId,
                DisplayOrder = exam.SubjectParts.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1,
                DefaultMarksPerQuestion = 0m,
                DefaultNegativeMarks = exam.NegativeMarksPerWrongAnswer,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            exam.SubjectParts.Add(part);
        }
        else
        {
            part.IsActive = true;
        }

        await SavePaperChangeAsync(exam);
    }

    public async Task SavePaperChangeAsync(AiExamProctoring.Models.Exam exam)
    {
        // Any paper change invalidates old session distribution.
        foreach (var question in exam.ExamQuestions)
            question.SessionId = null;

        await _db.SaveChangesAsync();

        if (exam.Sessions.Count > 0)
        {
            var hasAttempts = await _db.ExamSessionAttempts.AsNoTracking()
                .AnyAsync(x => x.Session != null && x.Session.ExamId == exam.ExamId);
            if (!hasAttempts)
            {
                _db.ExamSessions.RemoveRange(exam.Sessions);
            }
        }

        var active = exam.ExamQuestions.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ToList();
        RedistributeMarks(active, exam.TotalMarks, exam.NegativeMarksPerWrongAnswer);

        exam.TotalQuestions = active.Count;
        exam.Status = active.Count == 0 ? ExamStatus.Draft : ExamStatus.Building;
        exam.IsPublished = false;
        exam.IsLocked = false;
        exam.UpdatedDate = DateTime.Now;

        var activeSubjectCounts = await _db.ExamQuestions.AsNoTracking()
            .Where(x => x.ExamId == exam.ExamId && x.IsActive)
            .Join(_db.Questions.AsNoTracking(), eq => eq.QuestionId, q => q.QuestionId, (eq, q) => q.SubjectId)
            .GroupBy(subjectId => subjectId)
            .Select(g => new { SubjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count);

        foreach (var part in exam.SubjectParts)
        {
            part.IsActive = activeSubjectCounts.ContainsKey(part.SubjectId);
            if (part.IsActive)
            {
                part.DefaultMarksPerQuestion = active.Count > 0 ? Math.Round(exam.TotalMarks / active.Count, 2) : 0m;
                part.DefaultNegativeMarks = exam.NegativeMarksPerWrongAnswer;
            }
        }

        await _db.SaveChangesAsync();
    }

    private static void RedistributeMarks(List<ExamQuestion> questions, decimal totalMarks, decimal negativeMarks)
    {
        if (questions.Count == 0) return;
        if (totalMarks < questions.Count * 0.01m)
            throw new InvalidOperationException("Total Marks are too small for the number of selected questions.");

        var totalUnits = (long)Math.Round(totalMarks * 100m, MidpointRounding.AwayFromZero);
        var baseUnits = totalUnits / questions.Count;
        var remainder = totalUnits % questions.Count;

        for (var i = 0; i < questions.Count; i++)
        {
            var units = baseUnits + (i < remainder ? 1 : 0);
            questions[i].Marks = units / 100m;
            questions[i].NegativeMarks = negativeMarks;
            questions[i].DisplayOrder = i + 1;
        }
    }

}
