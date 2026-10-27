using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherQuestionBankViewModel
{
    public IReadOnlyList<TeacherQuestionSubjectFolderViewModel> Subjects { get; init; } = Array.Empty<TeacherQuestionSubjectFolderViewModel>();
    public IReadOnlyList<TeacherQuestionRowViewModel> Questions { get; init; } = Array.Empty<TeacherQuestionRowViewModel>();
    public int? SubjectId { get; init; }
    public string? Search { get; init; }
    public string Source { get; init; } = "all";
    public bool? Active { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalMatching { get; init; }
    public int MyQuestionCount { get; init; }
    public int SharedQuestionCount { get; init; }
    public int ActiveSubjectCount { get; init; }
    public string SelectedSubjectName => SubjectId.HasValue
        ? Subjects.FirstOrDefault(x => x.SubjectId == SubjectId.Value)?.SubjectName ?? "Subject"
        : "All subjects";
}

public sealed class TeacherQuestionSubjectFolderViewModel
{
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SharedQuestionCount { get; init; }
    public int MyQuestionCount { get; init; }
    public int TotalVisibleCount => SharedQuestionCount + MyQuestionCount;
}

public sealed class TeacherQuestionRowViewModel
{
    public required Question Question { get; init; }
    public bool IsMine { get; init; }
    public bool IsSystemStarter { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
}
