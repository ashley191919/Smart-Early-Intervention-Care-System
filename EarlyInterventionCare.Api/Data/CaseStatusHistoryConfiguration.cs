using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.Data;

internal sealed class CaseStatusHistoryConfiguration : IEntityTypeConfiguration<CaseStatusHistory>
{
    private const string CaseStatusColumnType =
        $"ENUM('{CaseStatusPolicy.New}', '{CaseStatusPolicy.ToContact}', '{CaseStatusPolicy.Appointed}', " +
        $"'{CaseStatusPolicy.WaitingForm}', '{CaseStatusPolicy.FormCompleted}', '{CaseStatusPolicy.Completed}')";

    private const string TriggerSourceColumnType =
        $"ENUM('{CaseStatusHistoryPolicy.Manual}', '{CaseStatusHistoryPolicy.QuestionnaireCompletion}', " +
        $"'{CaseStatusHistoryPolicy.SystemWorkflow}')";

    public void Configure(EntityTypeBuilder<CaseStatusHistory> entity)
    {
        entity.ToTable("case_status_histories");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");

        entity.Property(e => e.HistoryId).HasColumnName("history_id").HasColumnType("char(36)")
            .HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.CaseId).HasColumnName("case_id").HasColumnType("char(36)")
            .HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
        entity.Property(e => e.OldStatus).HasColumnName("old_status").HasColumnType(CaseStatusColumnType).IsRequired();
        entity.Property(e => e.NewStatus).HasColumnName("new_status").HasColumnType(CaseStatusColumnType).IsRequired();
        entity.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("char(36)")
            .HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.TriggerSource).HasColumnName("trigger_source").HasColumnType(TriggerSourceColumnType).IsRequired();
        entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("text");

        entity.HasKey(e => e.HistoryId);
        entity.HasIndex(e => new { e.CaseId, e.ChangedAtUtc, e.HistoryId })
            .HasDatabaseName("ix_case_status_histories_case_time");
        entity.HasIndex(e => e.ChangedByUserId).HasDatabaseName("ix_case_status_histories_changed_by_user");

        entity.HasOne<CaseRecord>().WithMany()
            .HasForeignKey(e => e.CaseId).HasPrincipalKey(e => e.CaseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_case_status_histories_case");
        entity.HasOne(e => e.ChangedByUser).WithMany().HasForeignKey(e => e.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_case_status_histories_changed_by_user");
    }
}
