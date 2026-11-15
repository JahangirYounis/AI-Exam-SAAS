using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Models;

// Separate from Models.Post, which represents recruitment positions.
public enum TeacherPostVisibility
{
    Connections = 0,
    Teachers = 1
}

public sealed class TeacherCommunityPost
{
    public int Id { get; set; }
    [Required, MaxLength(450)] public string AuthorId { get; set; } = "";
    [Required, MaxLength(2000)] public string Body { get; set; } = "";
    public TeacherPostVisibility Visibility { get; set; } = TeacherPostVisibility.Connections;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

public sealed class TeacherPostComment
{
    public int Id { get; set; }
    public int PostId { get; set; }
    [Required, MaxLength(450)] public string AuthorId { get; set; } = "";
    [Required, MaxLength(600)] public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

public sealed class TeacherPostLike
{
    public int PostId { get; set; }
    [Required, MaxLength(450)] public string TeacherId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class TeacherConnectionEvent
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    [Required, MaxLength(450)] public string ActorId { get; set; } = "";
    [Required, MaxLength(32)] public string Action { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
