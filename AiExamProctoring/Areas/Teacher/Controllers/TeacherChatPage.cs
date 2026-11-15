namespace AiExamProctoring.Areas.Teacher.Controllers;
public sealed class TeacherChatPage
{
 public string Kind {get;set;} = "team";
 public int RoomId {get;set;}
 public string Heading {get;set;} = "Discussion";
 public string ActorId {get;set;} = "";
}
