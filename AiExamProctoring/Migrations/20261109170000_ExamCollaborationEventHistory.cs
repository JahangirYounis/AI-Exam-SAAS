using AiExamProctoring.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiExamProctoring.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261109170000_ExamCollaborationEventHistory")]
public partial class ExamCollaborationEventHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExamCollaborationEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                InvitationId = table.Column<int>(type: "int", nullable: false),
                ExamId = table.Column<int>(type: "int", nullable: false),
                TeacherId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CanEditQuestions = table.Column<bool>(type: "bit", nullable: false),
                CanMonitor = table.Column<bool>(type: "bit", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ExamCollaborationEvents", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ExamCollaborationEvents_InvitationId_OccurredAtUtc",
            table: "ExamCollaborationEvents",
            columns: new[] { "InvitationId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ExamCollaborationEvents_TeacherId_ExamId_OccurredAtUtc",
            table: "ExamCollaborationEvents",
            columns: new[] { "TeacherId", "ExamId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ExamCollaborationEvents");
}
