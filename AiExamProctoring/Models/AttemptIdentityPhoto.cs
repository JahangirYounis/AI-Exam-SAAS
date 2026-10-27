using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace AiExamProctoring.Models;
public sealed class AttemptIdentityPhoto {
 [Key, ForeignKey(nameof(Attempt))] public int AttemptId {get;set;}
 public ExamAttempt? Attempt {get;set;}
 [Required, MaxLength(5000000)] public byte[] ImageBytes {get;set;} = Array.Empty<byte>();
 [Required, StringLength(64)] public string Sha256 {get;set;} = "";
 public DateTime CapturedAtUtc {get;set;}
}
