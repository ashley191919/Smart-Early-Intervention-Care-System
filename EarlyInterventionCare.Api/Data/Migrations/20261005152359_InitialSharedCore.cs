using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarlyInterventionCare.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSharedCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "cases",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    case_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sex = table.Column<string>(type: "ENUM('MALE', 'FEMALE', 'UNKNOWN')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    case_status = table.Column<string>(type: "ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED')", nullable: false, defaultValue: "NEW", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cases", x => x.case_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "questionnaires",
                columns: table => new
                {
                    questionnaire_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaires", x => x.questionnaire_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "teacher_grants",
                columns: table => new
                {
                    grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_role = table.Column<string>(type: "ENUM('PARENT', 'TEACHER')", nullable: false, defaultValue: "TEACHER", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    code_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    grant_status = table.Column<string>(type: "ENUM('ACTIVE', 'USED', 'REVOKED')", nullable: false, defaultValue: "ACTIVE", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issued_by_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    consent_reference_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    is_development = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_grants", x => x.grant_id);
                    table.UniqueConstraint("uq_grants_scope", x => new { x.grant_id, x.case_id, x.respondent_role });
                    table.ForeignKey(
                        name: "fk_grants_case",
                        column: x => x.case_id,
                        principalTable: "cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "questionnaire_versions",
                columns: table => new
                {
                    questionnaire_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_role = table.Column<string>(type: "ENUM('PARENT', 'TEACHER')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version_number = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    version_status = table.Column<string>(type: "ENUM('DRAFT', 'PUBLISHED', 'RETIRED')", nullable: false, defaultValue: "DRAFT", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    definition_snapshot = table.Column<string>(type: "json", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scoring_definition = table.Column<string>(type: "json", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaire_versions", x => x.questionnaire_version_id);
                    table.UniqueConstraint("uq_versions_type", x => new { x.questionnaire_version_id, x.questionnaire_id, x.respondent_role });
                    table.ForeignKey(
                        name: "fk_versions_questionnaire",
                        column: x => x.questionnaire_id,
                        principalTable: "questionnaires",
                        principalColumn: "questionnaire_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "teacher_sessions",
                columns: table => new
                {
                    session_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    session_status = table.Column<string>(type: "ENUM('ACTIVE', 'REVOKED')", nullable: false, defaultValue: "ACTIVE", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    session_expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_sessions", x => x.session_hash);
                    table.ForeignKey(
                        name: "fk_sessions_grant",
                        column: x => x.grant_id,
                        principalTable: "teacher_grants",
                        principalColumn: "grant_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "case_questionnaires",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_role = table.Column<string>(type: "ENUM('PARENT', 'TEACHER')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignment_round = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 1u),
                    is_required = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    task_status = table.Column<string>(type: "ENUM('PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED')", nullable: false, defaultValue: "PENDING", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_questionnaires", x => x.task_id);
                    table.UniqueConstraint("uq_tasks_scope", x => new { x.task_id, x.case_id, x.respondent_role });
                    table.UniqueConstraint("uq_tasks_version", x => new { x.task_id, x.questionnaire_version_id });
                    table.ForeignKey(
                        name: "fk_tasks_case",
                        column: x => x.case_id,
                        principalTable: "cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_version_type",
                        columns: x => new { x.questionnaire_version_id, x.questionnaire_id, x.respondent_role },
                        principalTable: "questionnaire_versions",
                        principalColumns: new[] { "questionnaire_version_id", "questionnaire_id", "respondent_role" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "teacher_grant_tasks",
                columns: table => new
                {
                    grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    task_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_role = table.Column<string>(type: "ENUM('PARENT', 'TEACHER')", nullable: false, defaultValue: "TEACHER", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_grant_tasks", x => new { x.grant_id, x.task_id });
                    table.ForeignKey(
                        name: "fk_grant_tasks_grant_scope",
                        columns: x => new { x.grant_id, x.case_id, x.respondent_role },
                        principalTable: "teacher_grants",
                        principalColumns: new[] { "grant_id", "case_id", "respondent_role" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_grant_tasks_task_scope",
                        columns: x => new { x.task_id, x.case_id, x.respondent_role },
                        principalTable: "case_questionnaires",
                        principalColumns: new[] { "task_id", "case_id", "respondent_role" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "questionnaire_drafts",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    filled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    answers_json = table.Column<string>(type: "json", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    observation = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    last_saved_by_grant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaire_drafts", x => x.task_id);
                    table.ForeignKey(
                        name: "fk_drafts_grant_scope",
                        columns: x => new { x.last_saved_by_grant_id, x.task_id },
                        principalTable: "teacher_grant_tasks",
                        principalColumns: new[] { "grant_id", "task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drafts_task_version",
                        columns: x => new { x.task_id, x.questionnaire_version_id },
                        principalTable: "case_questionnaires",
                        principalColumns: new[] { "task_id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "questionnaire_responses",
                columns: table => new
                {
                    response_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    task_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    questionnaire_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    respondent_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    filled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    answers_json = table.Column<string>(type: "json", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    observation = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    submitted_by_grant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    idempotency_key = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    payload_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaire_responses", x => x.response_id);
                    table.ForeignKey(
                        name: "fk_responses_grant_scope",
                        columns: x => new { x.submitted_by_grant_id, x.task_id },
                        principalTable: "teacher_grant_tasks",
                        principalColumns: new[] { "grant_id", "task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_responses_task_version",
                        columns: x => new { x.task_id, x.questionnaire_version_id },
                        principalTable: "case_questionnaires",
                        principalColumns: new[] { "task_id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_case_questionnaires_questionnaire_version_id_questionnaire_i~",
                table: "case_questionnaires",
                columns: new[] { "questionnaire_version_id", "questionnaire_id", "respondent_role" });

            migrationBuilder.CreateIndex(
                name: "uq_tasks_assignment",
                table: "case_questionnaires",
                columns: new[] { "case_id", "questionnaire_id", "respondent_role", "assignment_round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_cases_code",
                table: "cases",
                column: "case_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_questionnaire_drafts_last_saved_by_grant_id_task_id",
                table: "questionnaire_drafts",
                columns: new[] { "last_saved_by_grant_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_questionnaire_drafts_task_id_questionnaire_version_id",
                table: "questionnaire_drafts",
                columns: new[] { "task_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_questionnaire_responses_submitted_by_grant_id_task_id",
                table: "questionnaire_responses",
                columns: new[] { "submitted_by_grant_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_questionnaire_responses_task_id_questionnaire_version_id",
                table: "questionnaire_responses",
                columns: new[] { "task_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "uq_responses_grant_key",
                table: "questionnaire_responses",
                columns: new[] { "submitted_by_grant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_responses_task",
                table: "questionnaire_responses",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_versions_number",
                table: "questionnaire_versions",
                columns: new[] { "questionnaire_id", "respondent_role", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_questionnaires_code",
                table: "questionnaires",
                column: "questionnaire_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grant_tasks_task_scope",
                table: "teacher_grant_tasks",
                columns: new[] { "task_id", "case_id", "respondent_role" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_grant_tasks_grant_id_case_id_respondent_role",
                table: "teacher_grant_tasks",
                columns: new[] { "grant_id", "case_id", "respondent_role" });

            migrationBuilder.CreateIndex(
                name: "ix_grants_case_status",
                table: "teacher_grants",
                columns: new[] { "case_id", "grant_status" });

            migrationBuilder.CreateIndex(
                name: "uq_grants_code_hash",
                table: "teacher_grants",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_grant_status",
                table: "teacher_sessions",
                columns: new[] { "grant_id", "session_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "questionnaire_drafts");

            migrationBuilder.DropTable(
                name: "questionnaire_responses");

            migrationBuilder.DropTable(
                name: "teacher_sessions");

            migrationBuilder.DropTable(
                name: "teacher_grant_tasks");

            migrationBuilder.DropTable(
                name: "teacher_grants");

            migrationBuilder.DropTable(
                name: "case_questionnaires");

            migrationBuilder.DropTable(
                name: "cases");

            migrationBuilder.DropTable(
                name: "questionnaire_versions");

            migrationBuilder.DropTable(
                name: "questionnaires");
        }
    }
}
