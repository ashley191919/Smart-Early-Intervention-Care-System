using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.Data;

internal sealed class CaseRecordConfiguration : IEntityTypeConfiguration<CaseRecord>
{
    public void Configure(EntityTypeBuilder<CaseRecord> entity)
    {
        entity.ToTable("cases");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.CaseId).HasColumnName("case_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.CaseCode).HasColumnName("case_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ChildName).HasColumnName("child_name").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        entity.Property(e => e.BirthDate).HasColumnName("birth_date").HasColumnType("date").IsRequired();
        entity.Property(e => e.Sex).HasColumnName("sex").HasColumnType("ENUM('MALE', 'FEMALE', 'UNKNOWN')").IsRequired();
        entity.Property(e => e.CaseStatus).HasColumnName("case_status").HasColumnType("ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED')").IsRequired().HasDefaultValue("NEW");
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasKey(e => e.CaseId);
        entity.HasIndex(e => e.CaseCode).IsUnique().HasDatabaseName("uq_cases_code");
    }
}

internal sealed class QuestionnaireConfiguration : IEntityTypeConfiguration<Questionnaire>
{
    public void Configure(EntityTypeBuilder<Questionnaire> entity)
    {
        entity.ToTable("questionnaires");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.QuestionnaireId).HasColumnName("questionnaire_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireCode).HasColumnName("questionnaire_code").HasColumnType("varchar(32)").HasCharSet("ascii").UseCollation("ascii_bin").HasMaxLength(32).IsRequired();
        entity.Property(e => e.Title).HasColumnName("title").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasKey(e => e.QuestionnaireId);
        entity.HasIndex(e => e.QuestionnaireCode).IsUnique().HasDatabaseName("uq_questionnaires_code");
    }
}

internal sealed class QuestionnaireVersionConfiguration : IEntityTypeConfiguration<QuestionnaireVersion>
{
    public void Configure(EntityTypeBuilder<QuestionnaireVersion> entity)
    {
        entity.ToTable("questionnaire_versions");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.QuestionnaireVersionId).HasColumnName("questionnaire_version_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireId).HasColumnName("questionnaire_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentRole).HasColumnName("respondent_role").HasColumnType("ENUM('PARENT', 'TEACHER')").IsRequired();
        entity.Property(e => e.VersionNumber).HasColumnName("version_number").HasColumnType("varchar(32)").HasCharSet("ascii").UseCollation("ascii_bin").HasMaxLength(32).IsRequired();
        entity.Property(e => e.VersionStatus).HasColumnName("version_status").HasColumnType("ENUM('DRAFT', 'PUBLISHED', 'RETIRED')").IsRequired().HasDefaultValue("DRAFT");
        entity.Property(e => e.DefinitionSnapshot).HasColumnName("definition_snapshot").HasColumnType("json").IsRequired();
        entity.Property(e => e.ScoringDefinition).HasColumnName("scoring_definition").HasColumnType("json");
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.PublishedAtUtc).HasColumnName("published_at_utc").HasColumnType("datetime(6)");
        entity.HasKey(e => e.QuestionnaireVersionId);
        entity.HasIndex(e => new { e.QuestionnaireId, e.RespondentRole, e.VersionNumber }).IsUnique().HasDatabaseName("uq_versions_number");
        entity.HasAlternateKey(e => new { e.QuestionnaireVersionId, e.QuestionnaireId, e.RespondentRole }).HasName("uq_versions_type");
        entity.HasOne<Questionnaire>().WithMany()
            .HasForeignKey(e => e.QuestionnaireId).HasPrincipalKey(e => e.QuestionnaireId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_versions_questionnaire");
    }
}

internal sealed class QuestionnaireTaskConfiguration : IEntityTypeConfiguration<QuestionnaireTask>
{
    public void Configure(EntityTypeBuilder<QuestionnaireTask> entity)
    {
        entity.ToTable("case_questionnaires");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.TaskId).HasColumnName("task_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.CaseId).HasColumnName("case_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireId).HasColumnName("questionnaire_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireVersionId).HasColumnName("questionnaire_version_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentRole).HasColumnName("respondent_role").HasColumnType("ENUM('PARENT', 'TEACHER')").IsRequired();
        entity.Property(e => e.AssignmentRound).HasColumnName("assignment_round").HasColumnType("int unsigned").IsRequired().HasDefaultValue(1u);
        entity.Property(e => e.IsRequired).HasColumnName("is_required").HasColumnType("tinyint(1)").IsRequired().HasDefaultValue(true).HasSentinel(true);
        entity.Property(e => e.TaskStatus).HasColumnName("task_status").HasColumnType("ENUM('PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED')").IsRequired().HasDefaultValue("PENDING");
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.SubmittedAtUtc).HasColumnName("submitted_at_utc").HasColumnType("datetime(6)");
        entity.Property(e => e.CancelledAtUtc).HasColumnName("cancelled_at_utc").HasColumnType("datetime(6)");
        entity.HasKey(e => e.TaskId);
        entity.HasIndex(e => new { e.CaseId, e.QuestionnaireId, e.RespondentRole, e.AssignmentRound }).IsUnique().HasDatabaseName("uq_tasks_assignment");
        entity.HasAlternateKey(e => new { e.TaskId, e.CaseId, e.RespondentRole }).HasName("uq_tasks_scope");
        entity.HasAlternateKey(e => new { e.TaskId, e.QuestionnaireVersionId }).HasName("uq_tasks_version");
        entity.HasOne<CaseRecord>().WithMany()
            .HasForeignKey(e => e.CaseId).HasPrincipalKey(e => e.CaseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_tasks_case");
        entity.HasOne<QuestionnaireVersion>().WithMany()
            .HasForeignKey(e => new { e.QuestionnaireVersionId, e.QuestionnaireId, e.RespondentRole }).HasPrincipalKey(e => new { e.QuestionnaireVersionId, e.QuestionnaireId, e.RespondentRole })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_tasks_version_type");
    }
}

internal sealed class TeacherGrantConfiguration : IEntityTypeConfiguration<TeacherGrant>
{
    public void Configure(EntityTypeBuilder<TeacherGrant> entity)
    {
        entity.ToTable("teacher_grants");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.GrantId).HasColumnName("grant_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.CaseId).HasColumnName("case_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentRole).HasColumnName("respondent_role").HasColumnType("ENUM('PARENT', 'TEACHER')").IsRequired().HasDefaultValue("TEACHER");
        entity.Property(e => e.CodeHash).HasColumnName("code_hash").HasColumnType("binary(32)").IsRequired();
        entity.Property(e => e.GrantStatus).HasColumnName("grant_status").HasColumnType("ENUM('ACTIVE', 'USED', 'REVOKED')").IsRequired().HasDefaultValue("ACTIVE");
        entity.Property(e => e.IssuedByUserId).HasColumnName("issued_by_user_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.ConsentReferenceId).HasColumnName("consent_reference_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.IsDevelopment).HasColumnName("is_development").HasColumnType("tinyint(1)").IsRequired().HasDefaultValue(false);
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.UsedAtUtc).HasColumnName("used_at_utc").HasColumnType("datetime(6)");
        entity.Property(e => e.RevokedAtUtc).HasColumnName("revoked_at_utc").HasColumnType("datetime(6)");
        entity.HasKey(e => e.GrantId);
        entity.HasIndex(e => e.CodeHash).IsUnique().HasDatabaseName("uq_grants_code_hash");
        entity.HasAlternateKey(e => new { e.GrantId, e.CaseId, e.RespondentRole }).HasName("uq_grants_scope");
        entity.HasIndex(e => new { e.CaseId, e.GrantStatus }).HasDatabaseName("ix_grants_case_status");
        entity.HasOne<CaseRecord>().WithMany()
            .HasForeignKey(e => e.CaseId).HasPrincipalKey(e => e.CaseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_grants_case");
    }
}

internal sealed class TeacherGrantTaskConfiguration : IEntityTypeConfiguration<TeacherGrantTask>
{
    public void Configure(EntityTypeBuilder<TeacherGrantTask> entity)
    {
        entity.ToTable("teacher_grant_tasks");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.GrantId).HasColumnName("grant_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.TaskId).HasColumnName("task_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.CaseId).HasColumnName("case_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentRole).HasColumnName("respondent_role").HasColumnType("ENUM('PARENT', 'TEACHER')").IsRequired().HasDefaultValue("TEACHER");
        entity.HasKey(e => new { e.GrantId, e.TaskId });
        entity.HasIndex(e => new { e.TaskId, e.CaseId, e.RespondentRole }).HasDatabaseName("ix_grant_tasks_task_scope");
        entity.HasOne<TeacherGrant>().WithMany()
            .HasForeignKey(e => new { e.GrantId, e.CaseId, e.RespondentRole }).HasPrincipalKey(e => new { e.GrantId, e.CaseId, e.RespondentRole })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_grant_tasks_grant_scope");
        entity.HasOne<QuestionnaireTask>().WithMany()
            .HasForeignKey(e => new { e.TaskId, e.CaseId, e.RespondentRole }).HasPrincipalKey(e => new { e.TaskId, e.CaseId, e.RespondentRole })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_grant_tasks_task_scope");
    }
}

internal sealed class TeacherSessionConfiguration : IEntityTypeConfiguration<TeacherSession>
{
    public void Configure(EntityTypeBuilder<TeacherSession> entity)
    {
        entity.ToTable("teacher_sessions");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.SessionHash).HasColumnName("session_hash").HasColumnType("binary(32)").IsRequired();
        entity.Property(e => e.GrantId).HasColumnName("grant_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.SessionStatus).HasColumnName("session_status").HasColumnType("ENUM('ACTIVE', 'REVOKED')").IsRequired().HasDefaultValue("ACTIVE");
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.LastSeenAtUtc).HasColumnName("last_seen_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.SessionExpiresAtUtc).HasColumnName("session_expires_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.ReceiptExpiresAtUtc).HasColumnName("receipt_expires_at_utc").HasColumnType("datetime(6)");
        entity.Property(e => e.RevokedAtUtc).HasColumnName("revoked_at_utc").HasColumnType("datetime(6)");
        entity.HasKey(e => e.SessionHash);
        entity.HasIndex(e => new { e.GrantId, e.SessionStatus }).HasDatabaseName("ix_sessions_grant_status");
        entity.HasOne<TeacherGrant>().WithMany()
            .HasForeignKey(e => e.GrantId).HasPrincipalKey(e => e.GrantId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_grant");
    }
}

internal sealed class QuestionnaireDraftConfiguration : IEntityTypeConfiguration<QuestionnaireDraft>
{
    public void Configure(EntityTypeBuilder<QuestionnaireDraft> entity)
    {
        entity.ToTable("questionnaire_drafts");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.TaskId).HasColumnName("task_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireVersionId).HasColumnName("questionnaire_version_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentName).HasColumnName("respondent_name").HasColumnType("varchar(100)").HasMaxLength(100);
        entity.Property(e => e.FilledOn).HasColumnName("filled_on").HasColumnType("date");
        entity.Property(e => e.AnswersJson).HasColumnName("answers_json").HasColumnType("json").IsRequired();
        entity.Property(e => e.Observation).HasColumnName("observation").HasColumnType("text");
        entity.Property(e => e.Revision).HasColumnName("revision").HasColumnType("bigint unsigned").IsRequired().IsConcurrencyToken();
        entity.Property(e => e.LastSavedByGrantId).HasColumnName("last_saved_by_grant_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasKey(e => e.TaskId);
        entity.HasOne<QuestionnaireTask>().WithMany()
            .HasForeignKey(e => new { e.TaskId, e.QuestionnaireVersionId }).HasPrincipalKey(e => new { e.TaskId, e.QuestionnaireVersionId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_drafts_task_version");
        entity.HasOne<TeacherGrantTask>().WithMany()
            .HasForeignKey(e => new { e.LastSavedByGrantId, e.TaskId }).HasPrincipalKey(e => new { e.GrantId, e.TaskId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_drafts_grant_scope");
    }
}

internal sealed class QuestionnaireResponseConfiguration : IEntityTypeConfiguration<QuestionnaireResponse>
{
    public void Configure(EntityTypeBuilder<QuestionnaireResponse> entity)
    {
        entity.ToTable("questionnaire_responses");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.Property(e => e.ResponseId).HasColumnName("response_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.TaskId).HasColumnName("task_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.QuestionnaireVersionId).HasColumnName("questionnaire_version_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.RespondentName).HasColumnName("respondent_name").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        entity.Property(e => e.FilledOn).HasColumnName("filled_on").HasColumnType("date").IsRequired();
        entity.Property(e => e.AnswersJson).HasColumnName("answers_json").HasColumnType("json").IsRequired();
        entity.Property(e => e.Observation).HasColumnName("observation").HasColumnType("text");
        entity.Property(e => e.SubmittedByGrantId).HasColumnName("submitted_by_grant_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.PayloadHash).HasColumnName("payload_hash").HasColumnType("binary(32)").IsRequired();
        entity.Property(e => e.SubmittedAtUtc).HasColumnName("submitted_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasKey(e => e.ResponseId);
        entity.HasIndex(e => e.TaskId).IsUnique().HasDatabaseName("uq_responses_task");
        entity.HasIndex(e => new { e.SubmittedByGrantId, e.IdempotencyKey }).IsUnique().HasDatabaseName("uq_responses_grant_key");
        entity.HasOne<QuestionnaireTask>().WithMany()
            .HasForeignKey(e => new { e.TaskId, e.QuestionnaireVersionId }).HasPrincipalKey(e => new { e.TaskId, e.QuestionnaireVersionId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_responses_task_version");
        entity.HasOne<TeacherGrantTask>().WithMany()
            .HasForeignKey(e => new { e.SubmittedByGrantId, e.TaskId }).HasPrincipalKey(e => new { e.GrantId, e.TaskId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_responses_grant_scope");
    }
}
