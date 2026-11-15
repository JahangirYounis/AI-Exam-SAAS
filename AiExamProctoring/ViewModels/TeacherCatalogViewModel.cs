using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherCatalogViewModel
{
    public IReadOnlyList<Department> Departments { get; init; } = Array.Empty<Department>();
    public IReadOnlyList<TeacherCatalogSubjectViewModel> Subjects { get; init; } = Array.Empty<TeacherCatalogSubjectViewModel>();
    public string? Search { get; init; }
    public string Status { get; init; } = "all";
    public int VisibleQuestionCount => Subjects.Sum(x => x.VisibleActiveQuestionCount);
    public int LockedSubjectCount => Subjects.Count(x => x.IsLocked);

    public int ActiveDepartmentCount => Departments.Count(x => x.IsActive);
    public int ActivePostCount => Departments.Sum(x => x.Posts.Count(p => p.IsActive));
    public int ActiveSubjectCount => Subjects.Count(x => x.IsActive);
}

// Lightweight catalog summary. Counts exclude another teacher's private questions.
public sealed class TeacherCatalogSubjectViewModel
{
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public int MyQuestionCount { get; init; }
    public int SharedQuestionCount { get; init; }
    public int VisibleActiveQuestionCount { get; init; }
}
