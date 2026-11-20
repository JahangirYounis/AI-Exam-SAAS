using AiExamProctoring.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AiExamProctoring.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261101120000_RestoreTeacherChatMetadata")]
public partial class RestoreTeacherChatMetadata : Migration {
 protected override void Up(MigrationBuilder migrationBuilder) {
  migrationBuilder.Sql(@"IF OBJECT_ID(N'dbo.TeacherChatReplyLinks', N'U') IS NULL
 CREATE TABLE dbo.TeacherChatReplyLinks ([Kind] nvarchar(16) NOT NULL, [RoomId] int NOT NULL, [MessageId] int NOT NULL, [ReplyToId] int NOT NULL, CONSTRAINT PK_TeacherChatReplyLinks PRIMARY KEY CLUSTERED ([Kind], [RoomId], [MessageId]));");
  migrationBuilder.Sql(@"IF OBJECT_ID(N'dbo.TeacherChatReactions', N'U') IS NULL
 CREATE TABLE dbo.TeacherChatReactions ([Kind] nvarchar(16) NOT NULL, [RoomId] int NOT NULL, [MessageId] int NOT NULL, [UserId] nvarchar(450) NOT NULL, [Emoji] nvarchar(16) NOT NULL, CONSTRAINT PK_TeacherChatReactions PRIMARY KEY NONCLUSTERED ([Kind], [RoomId], [MessageId], [UserId]));");
  migrationBuilder.Sql(@"IF OBJECT_ID(N'dbo.TeacherActivityReads', N'U') IS NULL
 CREATE TABLE dbo.TeacherActivityReads ([UserId] nvarchar(450) NOT NULL, [Kind] nvarchar(16) NOT NULL, [EntryId] int NOT NULL, CONSTRAINT PK_TeacherActivityReads PRIMARY KEY NONCLUSTERED ([UserId], [Kind], [EntryId]));");
 }
 protected override void Down(MigrationBuilder migrationBuilder) { throw new System.NotSupportedException("Chat history metadata must be preserved. Restore from an approved backup for a schema rollback."); }
}
