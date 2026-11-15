using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Models;

[PrimaryKey(nameof(Kind), nameof(RoomId), nameof(MessageId))]
public sealed class TeacherChatReplyLink {
    [MaxLength(16)] public string Kind { get; set; } = "";
    public int RoomId { get; set; }
    public int MessageId { get; set; }
    public int ReplyToId { get; set; }
}
[PrimaryKey(nameof(Kind), nameof(RoomId), nameof(MessageId), nameof(UserId))]
public sealed class TeacherChatReaction {
    [MaxLength(16)] public string Kind { get; set; } = "";
    public int RoomId { get; set; }
    public int MessageId { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    [MaxLength(16)] public string Emoji { get; set; } = "";
}
[PrimaryKey(nameof(UserId), nameof(Kind), nameof(EntryId))]
public sealed class TeacherActivityRead {
    [MaxLength(450)] public string UserId { get; set; } = "";
    [MaxLength(16)] public string Kind { get; set; } = "";
    public int EntryId { get; set; }
}
