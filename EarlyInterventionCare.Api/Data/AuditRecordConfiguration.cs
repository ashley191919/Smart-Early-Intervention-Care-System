using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.Data;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> entity)
    {
        entity.ToTable("audit_logs");
        entity.HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");
        entity.HasKey(e => e.EventId);
        entity.Property(e => e.EventId).HasColumnName("event_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("datetime(6)");
        entity.Property(e => e.ActorType).HasColumnName("actor_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ActorId).HasColumnName("actor_id").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ResourceType).HasColumnName("resource_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ResourceId).HasColumnName("resource_id").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Result).HasColumnName("result").HasMaxLength(32).IsRequired();
        entity.Property(e => e.RequestCorrelationId).HasColumnName("request_correlation_id").HasMaxLength(128).IsRequired();
        entity.HasIndex(e => new { e.ResourceType, e.ResourceId, e.OccurredAtUtc }).HasDatabaseName("ix_audit_resource_time");
        entity.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("ix_audit_time");
    }
}
