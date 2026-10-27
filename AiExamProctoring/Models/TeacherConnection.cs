using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Models;
[Index(nameof(PairKey), IsUnique = true)]
public sealed class TeacherConnection
{
    public int Id { get; set; }
    [MaxLength(64)] public string PairKey { get; set; } = "";
    [MaxLength(450)] public string SenderId { get; set; } = "";
    [MaxLength(450)] public string RecipientId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
}
[Index(nameof(ConnectionId), nameof(Id))]
public sealed class TeacherPrivateMessage
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    [MaxLength(450)] public string SenderId { get; set; } = "";
    [MaxLength(2000)] public string Body { get; set; } = "";
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
