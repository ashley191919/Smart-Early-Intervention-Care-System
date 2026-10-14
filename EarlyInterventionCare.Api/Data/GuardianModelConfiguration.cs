using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EarlyInterventionCare.Api.Data;
internal static class GuardianMapping
{
    internal static void Columns<T>(EntityTypeBuilder<T> e) where T : class
    {
        foreach (var p in e.Metadata.GetProperties())
        {
            var name = System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
            var b = e.Property(p.Name).HasColumnName(name);
            if (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)) b.HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin").ValueGeneratedNever();
            if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) b.HasColumnType("datetime(6)");
        }
    }
}
internal sealed class GuardianConfiguration : IEntityTypeConfiguration<GuardianContact>
{
    public void Configure(EntityTypeBuilder<GuardianContact> e)
    {
        IdentityMapping.Table(e, "guardians"); e.HasKey(x => x.GuardianId);
        e.Property(x => x.DisplayName).HasMaxLength(100).IsRequired(); e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        e.Property(x => x.Email).HasMaxLength(254).IsRequired();
        e.HasIndex(x => new { x.OrganizationId, x.Phone }).IsUnique().HasDatabaseName("uq_guardians_organization_phone");
        e.Property(x => x.Revision).IsConcurrencyToken();
        e.HasIndex(x => new { x.OrganizationId, x.ParentUserId });
        e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Restrict);
        GuardianMapping.Columns(e);
    }
}
internal sealed class CaseGuardianConfiguration : IEntityTypeConfiguration<CaseGuardian>
{
    public void Configure(EntityTypeBuilder<CaseGuardian> e)
    {
        IdentityMapping.Table(e, "case_guardians"); e.HasKey(x => new { x.CaseId, x.GuardianId });
        e.Property(x => x.Relationship).HasMaxLength(32).IsRequired(); e.Property(x => x.LinkStatus).HasMaxLength(20).IsRequired();
        e.HasOne<CaseRecord>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<GuardianContact>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
        GuardianMapping.Columns(e);
        e.Property(x => x.ActiveCaseId).HasComputedColumnSql("CASE WHEN link_status = 'ACTIVE' AND is_primary_contact = 1 THEN case_id ELSE NULL END", stored: true).ValueGeneratedOnAddOrUpdate();
        e.HasIndex(x => x.ActiveCaseId).IsUnique().HasDatabaseName("uq_case_guardians_active_case");
    }
}
internal sealed class BindingInvitationConfiguration : IEntityTypeConfiguration<ParentBindingInvitation>
{
    public void Configure(EntityTypeBuilder<ParentBindingInvitation> e)
    {
        IdentityMapping.Table(e, "parent_binding_invitations"); e.HasKey(x => x.InvitationId);
        e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired(); e.HasIndex(x => x.CodeHash).IsUnique();
        e.Property(x => x.TargetPhone).HasMaxLength(20).IsRequired();
        e.HasOne<CaseGuardian>().WithMany().HasForeignKey(x => new { x.CaseId, x.GuardianId }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ConsumedByUserId).OnDelete(DeleteBehavior.Restrict);
        GuardianMapping.Columns(e);
    }
}
internal sealed class CaseOwnershipConfiguration : IEntityTypeConfiguration<CaseRecord>
{
    public void Configure(EntityTypeBuilder<CaseRecord> e)
    {
        e.Property(x => x.OrganizationId).HasColumnName("organization_id").HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin");
        e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.NationalId).HasColumnName("national_id").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        e.Property(x => x.Address).HasColumnName("address").HasColumnType("varchar(300)").HasMaxLength(300).IsRequired();
        e.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken(); e.HasIndex(x => x.OrganizationId);
    }
}
