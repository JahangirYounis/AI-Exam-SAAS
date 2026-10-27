using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiExamProctoring.Migrations
{
    /// <inheritdoc />
    public partial class TeacherCollaborationAndMonitoringAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionByName",
                table: "ProctoringLogs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionByUserId",
                table: "ProctoringLogs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatorFirstMonitoringAtUtc",
                table: "Exams",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatorLastMonitoringAtUtc",
                table: "Exams",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanEditQuestions",
                table: "ExamInvigilators",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanMonitor",
                table: "ExamInvigilators",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstMonitoringAtUtc",
                table: "ExamInvigilators",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMonitoringAtUtc",
                table: "ExamInvigilators",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "ExamInvigilators",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeacherConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PairKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SenderId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeacherPrivateMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherPrivateMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherConnections_PairKey",
                table: "TeacherConnections",
                column: "PairKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPrivateMessages_ConnectionId_Id",
                table: "TeacherPrivateMessages",
                columns: new[] { "ConnectionId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeacherConnections");

            migrationBuilder.DropTable(
                name: "TeacherPrivateMessages");

            migrationBuilder.DropColumn(
                name: "ActionByName",
                table: "ProctoringLogs");

            migrationBuilder.DropColumn(
                name: "ActionByUserId",
                table: "ProctoringLogs");

            migrationBuilder.DropColumn(
                name: "CreatorFirstMonitoringAtUtc",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CreatorLastMonitoringAtUtc",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CanEditQuestions",
                table: "ExamInvigilators");

            migrationBuilder.DropColumn(
                name: "CanMonitor",
                table: "ExamInvigilators");

            migrationBuilder.DropColumn(
                name: "FirstMonitoringAtUtc",
                table: "ExamInvigilators");

            migrationBuilder.DropColumn(
                name: "LastMonitoringAtUtc",
                table: "ExamInvigilators");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "ExamInvigilators");
        }
    }
}
