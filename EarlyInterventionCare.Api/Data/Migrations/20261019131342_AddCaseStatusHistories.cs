using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarlyInterventionCare.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseStatusHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_status_histories",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    old_status = table.Column<string>(type: "ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_status = table.Column<string>(type: "ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    changed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    trigger_source = table.Column<string>(type: "ENUM('MANUAL', 'QUESTIONNAIRE_COMPLETION', 'SYSTEM_WORKFLOW')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_status_histories", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_case_status_histories_case",
                        column: x => x.case_id,
                        principalTable: "cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_status_histories_changed_by_user",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "ix_case_status_histories_case_time",
                table: "case_status_histories",
                columns: new[] { "case_id", "changed_at_utc", "history_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_status_histories_changed_by_user",
                table: "case_status_histories",
                column: "changed_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_status_histories");
        }
    }
}
