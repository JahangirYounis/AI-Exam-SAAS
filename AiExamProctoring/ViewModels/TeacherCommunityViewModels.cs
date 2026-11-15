using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherPostCardViewModel
{
    public int Id { get; init; }
    public string AuthorId { get; init; } = "";
    public string AuthorName { get; init; } = "Teacher";
    public string? AuthorPhoto { get; init; }
    public string? Department { get; init; }
    public string Body { get; init; } = "";
    public TeacherPostVisibility Visibility { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int LikeCount { get; init; }
    public int CommentCount { get; init; }
    public bool LikedByMe { get; init; }
    public bool IsMine { get; init; }
}

public sealed class TeacherCommunityIndexViewModel
{
    public string CurrentTeacherId { get; init; } = "";
    public string Filter { get; init; } = "all";
    public int Page { get; init; } = 1;
    public bool HasNext { get; init; }
    public int PendingRequests { get; init; }
    public int ConnectionCount { get; init; }
    public List<TeacherPostCardViewModel> Posts { get; init; } = new();
}

public sealed class TeacherPostDetailsViewModel
{
    public TeacherPostCardViewModel Post { get; init; } = new();
    public List<TeacherPostCommentViewModel> Comments { get; init; } = new();
    public int TotalComments { get; init; }
}

public sealed class TeacherPostCommentViewModel
{
    public int Id { get; init; }
    public string AuthorId { get; init; } = "";
    public string AuthorName { get; init; } = "Teacher";
    public string Body { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public bool IsMine { get; init; }
}

public sealed class TeacherPublicCommunityProfileViewModel
{
    public string Id { get; init; } = "";
    public string FullName { get; init; } = "Teacher";
    public string? ProfileImage { get; init; }
    public string? Department { get; init; }
    public string? OrganizationName { get; init; }
    public string? HighestQualification { get; init; }
    public string? TeacherHeadline { get; init; }
    public string? TeacherBio { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string ConnectionStatus { get; init; } = "none";
    public int? ConnectionId { get; init; }
    public bool IsOwnProfile { get; init; }
    public List<TeacherPostCardViewModel> Posts { get; init; } = new();
}

public sealed class TeacherDirectoryPersonViewModel
{
    public string Id { get; init; } = "";
    public string FullName { get; init; } = "Teacher";
    public string? ProfileImage { get; init; }
    public string? Department { get; init; }
    public string? OrganizationName { get; init; }
    public string? City { get; init; }
    public int? ConnectionId { get; init; }
    public string Status { get; init; } = "none";
}

public sealed class TeacherDirectoryViewModel
{
    public string Search { get; init; } = "";
    public string Tab { get; init; } = "directory";
    public List<TeacherDirectoryPersonViewModel> People { get; init; } = new();
    public int ReceivedCount { get; init; }
    public int SentCount { get; init; }
    public int ConnectedCount { get; init; }
}
