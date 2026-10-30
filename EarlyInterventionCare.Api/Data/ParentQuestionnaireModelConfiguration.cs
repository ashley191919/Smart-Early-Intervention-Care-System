using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.Data;

internal sealed class ParentDraftConfiguration : IEntityTypeConfiguration<QuestionnaireDraft>
{
    public void Configure(EntityTypeBuilder<QuestionnaireDraft> entity)
    {
        entity.Property(e => e.RespondentRelationship).HasColumnName("respondent_relationship")
            .HasColumnType("varchar(32)").HasMaxLength(32).IsRequired(false);
        entity.Property(e => e.ParentAuthorUserId).HasColumnName("parent_author_user_id")
            .HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.HasOne<User>().WithMany().HasForeignKey(e => e.ParentAuthorUserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_drafts_parent_author");
        entity.HasIndex(e => e.ParentAuthorUserId).HasDatabaseName("ix_drafts_parent_author");
    }
}

internal sealed class ParentResponseConfiguration : IEntityTypeConfiguration<QuestionnaireResponse>
{
    public void Configure(EntityTypeBuilder<QuestionnaireResponse> entity)
    {
        entity.Property(e => e.HashVersion).HasColumnName("hash_version")
            .HasColumnType("varchar(64)").HasMaxLength(64).IsRequired(false);
        entity.Property(e => e.RespondentRelationship).HasColumnName("respondent_relationship")
            .HasColumnType("varchar(32)").HasMaxLength(32).IsRequired(false);
        entity.Property(e => e.ParentAuthorUserId).HasColumnName("parent_author_user_id")
            .HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
        entity.HasOne<User>().WithMany().HasForeignKey(e => e.ParentAuthorUserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_responses_parent_author");
        entity.HasIndex(e => new { e.ParentAuthorUserId, e.IdempotencyKey }).IsUnique()
            .HasDatabaseName("uq_responses_parent_key");
    }
}
