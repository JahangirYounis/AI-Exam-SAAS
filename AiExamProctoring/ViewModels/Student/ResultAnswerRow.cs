using AiExamProctoring.Models;
namespace AiExamProctoring.ViewModels.Student;

public sealed record ResultAnswerRow(string Question, string Selected, string Correct, string Status,
    decimal Marks, string? Explanation)
{
    public static ResultAnswerRow From(Question? question, StudentAnswer? answer, int sessionAttemptId, bool shuffled)
    {
        if (question is null) return new("Question no longer available", answer?.SelectedOption ?? "Not answered",
            "Not available", answer?.IsCorrect == true ? "Correct" : answer?.IsCorrect == false ? "Wrong" : "Skipped", answer?.MarksAwarded ?? 0, null);
        var options = new List<(string Letter, string Text)> { ("A", question.OptionA), ("B", question.OptionB), ("C", question.OptionC), ("D", question.OptionD) };
        if (!string.IsNullOrWhiteSpace(question.OptionE)) options.Add(("E", question.OptionE));
        if (shuffled)
        {
            var random = new Random(unchecked(2719 + sessionAttemptId * 47 + question.QuestionId));
            for (var i = options.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (options[i], options[j]) = (options[j], options[i]); }
        }
        string Display(int i) => i < 0 || i >= options.Count ? "Not available" : $"{(char)('A' + i)}. {options[i].Text}";
        var selected = answer?.SelectedOption?.Trim().ToUpperInvariant();
        return new(question.QuestionText, string.IsNullOrEmpty(selected) ? "Not answered" : Display(selected[0] - 'A'),
            Display(options.FindIndex(x => x.Letter == question.CorrectAnswer)),
            answer?.IsCorrect == true ? "Correct" : answer?.IsCorrect == false ? "Wrong" : "Skipped", answer?.MarksAwarded ?? 0, question.Explanation);
    }
}
