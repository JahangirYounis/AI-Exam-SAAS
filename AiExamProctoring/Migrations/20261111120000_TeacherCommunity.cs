using System;
using AiExamProctoring.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiExamProctoring.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111120000_TeacherCommunity")]
public partial class TeacherCommunity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TeacherHeadline", table: "AspNetUsers", type: "nvarchar(140)", maxLength: 140, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TeacherBio", table: "AspNetUsers", type: "nvarchar(700)", maxLength: 700, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ClosedAtUtc", table: "TeacherConnections", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "DeclinedAtUtc", table: "TeacherConnections", type: "datetime2", nullable: true);
        migrationBuilder.CreateTable(
            name: "TeacherCommunityPosts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AuthorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Visibility = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_TeacherCommunityPosts", x => x.Id));
        migrationBuilder.CreateTable(
            name: "TeacherConnectionEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ConnectionId = table.Column<int>(type: "int", nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_TeacherConnectionEvents", x => x.Id));
        migrationBuilder.CreateTable(
            name: "TeacherPostComments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                PostId = table.Column<int>(type: "int", nullable: false),
                AuthorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Body = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_TeacherPostComments", x => x.Id);
                table.ForeignKey("FK_TeacherPostComments_TeacherCommunityPosts_PostId", x => x.PostId,
                    "TeacherCommunityPosts", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateTable(
            name: "TeacherPostLikes",
            columns: table => new
            {
                PostId = table.Column<int>(type: "int", nullable: false),
                TeacherId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_TeacherPostLikes", x => new { x.PostId, x.TeacherId });
                table.ForeignKey("FK_TeacherPostLikes_TeacherCommunityPosts_PostId", x => x.PostId,
                    "TeacherCommunityPosts", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_TeacherCommunityPosts_IsDeleted_CreatedAtUtc", "TeacherCommunityPosts", new[] { "IsDeleted", "CreatedAtUtc" });
        migrationBuilder.CreateIndex("IX_TeacherCommunityPosts_AuthorId_CreatedAtUtc", "TeacherCommunityPosts", new[] { "AuthorId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex("IX_TeacherConnectionEvents_ConnectionId_OccurredAtUtc", "TeacherConnectionEvents", new[] { "ConnectionId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_TeacherPostComments_PostId_CreatedAtUtc", "TeacherPostComments", new[] { "PostId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This project retains teacher collaboration history. A rollback that would discard posts,
        // reactions and request events must be reviewed against a database backup first.
        throw new NotSupportedException("Teacher Community contains history. Restore an approved backup before rolling back.");
    }
}
