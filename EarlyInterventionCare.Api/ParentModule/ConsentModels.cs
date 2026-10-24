using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.ParentModule;

// Model proposal owned by 11. No approved hospital text, migration or seed is supplied.
public sealed class ConsentDocumentVersion
{
    public Guid DocumentVersionId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ScopeCode { get; set; } = "UNCONFIRMED";
    public string ApprovalStatus { get; set; } = "DRAFT";
    public string ApprovalReference { get; set; } = "";
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
}

public sealed class ParentConsentRecord
{
    public Guid ConsentId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? CaseId { get; set; }
    public string ScopeKey { get; set; } = "";
    public string DocumentContentHash { get; set; } = "";
    public string DataUseDecision { get; set; } = "UNDECIDED";
    public string TeacherParticipation { get; set; } = "UNDECIDED";
    public Guid RequestId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

internal sealed class ConsentDocumentConfiguration : IEntityTypeConfiguration<ConsentDocumentVersion>
{
    public void Configure(EntityTypeBuilder<ConsentDocumentVersion> e)
    {
        e.ToTable("consent_document_versions"); e.HasKey(x => x.DocumentVersionId);
        e.Property(x => x.DocumentCode).HasMaxLength(64).IsRequired();
        e.Property(x => x.Version).HasMaxLength(32).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Content).HasColumnType("longtext").IsRequired();
        e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.ScopeCode).HasMaxLength(32).IsRequired();
        e.Property(x => x.ApprovalStatus).HasMaxLength(16).IsRequired();
        e.Property(x => x.ApprovalReference).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.DocumentCode, x.Version }).IsUnique();
        Data.GuardianMapping.Columns(e);
    }
}

internal sealed class ParentConsentConfiguration : IEntityTypeConfiguration<ParentConsentRecord>
{
    public void Configure(EntityTypeBuilder<ParentConsentRecord> e)
    {
        e.ToTable("parent_consents"); e.HasKey(x => x.ConsentId);
        e.Property(x => x.ScopeKey).HasMaxLength(40).IsRequired();
        e.Property(x => x.DocumentContentHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.DataUseDecision).HasMaxLength(16).IsRequired();
        e.Property(x => x.TeacherParticipation).HasMaxLength(16).IsRequired();
        e.HasIndex(x => new { x.ActorUserId, x.RequestId }).IsUnique();
        // ScopeKey makes ACCOUNT (CaseId NULL) uniqueness explicit on MySQL.
        e.HasIndex(x => new { x.ActorUserId, x.OrganizationId, x.DocumentVersionId, x.ScopeKey }).IsUnique();
        e.HasOne<ConsentDocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<CaseRecord>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        Data.GuardianMapping.Columns(e);
    }
}
