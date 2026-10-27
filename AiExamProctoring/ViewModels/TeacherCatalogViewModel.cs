using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherCatalogViewModel
{
    public IReadOnlyList<Department> Departments { get; init; } = Array.Empty<Department>();
    public IReadOnlyList<Subject> Subjects { get; init; } = Array.Empty<Subject>();
    public string? Search { get; init; }

    public int ActiveDepartmentCount => Departments.Count(x => x.IsActive);
    public int ActivePostCount => Departments.Sum(x => x.Posts.Count(p => p.IsActive));
    public int ActiveSubjectCount => Subjects.Count(x => x.IsActive);
}
