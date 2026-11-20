using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiExamProctoring.Migrations
{
    /// <inheritdoc />
    public partial class SecureCertificatesAndEvidenceSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CertificateExpiresAtUtc",
                table: "ExamResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificateIssuedAtUtc",
                table: "ExamResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateReference",
                table: "ExamResults",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateRevocationReason",
                table: "ExamResults",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificateRevokedAtUtc",
                table: "ExamResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateRevokedBy",
                table: "ExamResults",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSnapshotJson",
                table: "ExamResults",
                type: "nvarchar(max)",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvidenceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogId = table.Column<int>(type: "int", nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceGrants_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceGrants_AspNetUsers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceGrants_ProctoringLogs_LogId",
                        column: x => x.LogId,
                        principalTable: "ProctoringLogs",
                        principalColumn: "LogId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_CertificateReference",
                table: "ExamResults",
                column: "CertificateReference",
                unique: true,
                filter: "[CertificateReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceGrants_LogId",
                table: "EvidenceGrants",
                column: "LogId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceGrants_OwnerId",
                table: "EvidenceGrants",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceGrants_RecipientId_ExpiresAtUtc",
                table: "EvidenceGrants",
                columns: new[] { "RecipientId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvidenceGrants");

            migrationBuilder.DropIndex(
                name: "IX_ExamResults_CertificateReference",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateExpiresAtUtc",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateIssuedAtUtc",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateReference",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateRevocationReason",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateRevokedAtUtc",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateRevokedBy",
                table: "ExamResults");

            migrationBuilder.DropColumn(
                name: "CertificateSnapshotJson",
                table: "ExamResults");
        }
    }
}
