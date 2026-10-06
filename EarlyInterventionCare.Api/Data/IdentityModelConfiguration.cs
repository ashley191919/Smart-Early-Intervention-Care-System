using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Models.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EarlyInterventionCare.Api.Data;

internal static class IdentityMapping
{
    public static void Table<T>(EntityTypeBuilder<T> entity, string name) where T : class =>
        entity.ToTable(name).HasCharSet("utf8mb4").UseCollation("utf8mb4_unicode_ci");

    public static void Uuid(PropertyBuilder<Guid> property, string name) =>
        property.HasColumnName(name).HasColumnType("char(36)").HasCharSet("ascii")
            .UseCollation("ascii_bin").ValueGeneratedNever().IsRequired();
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        IdentityMapping.Table(entity, "users");
        entity.HasKey(e => e.Id);
        IdentityMapping.Uuid(entity.Property(e => e.Id), "user_id");
        IdentityMapping.Uuid(entity.Property(e => e.OrganizationId), "organization_id");
        IdentityMapping.Uuid(entity.Property(e => e.RoleId), "role_id");
        entity.Property(e => e.Username).HasColumnName("username").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsRequired();
        entity.Property(e => e.Phone).HasColumnName("phone").HasColumnType("varchar(20)").HasMaxLength(20);
        entity.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).HasDefaultValue("ACTIVE").IsRequired();
        entity.Property(e => e.LastLoginAtUtc).HasColumnName("last_login_at_utc").HasColumnType("datetime(6)");
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasIndex(e => e.Username).IsUnique().HasDatabaseName("uq_users_username");
        entity.HasIndex(e => e.OrganizationId).HasDatabaseName("ix_users_organization");
        entity.HasIndex(e => e.RoleId).HasDatabaseName("ix_users_role");
        entity.HasOne(e => e.Organization).WithMany(e => e.Users).HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_users_organization");
        entity.HasOne(e => e.Role).WithMany().HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_users_role");
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> entity)
    {
        IdentityMapping.Table(entity, "organizations");
        entity.HasKey(e => e.Id);
        IdentityMapping.Uuid(entity.Property(e => e.Id), "organization_id");
        entity.Property(e => e.Code).HasColumnName("code").HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
        entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        entity.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).HasDefaultValue("ACTIVE").IsRequired();
        entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)").IsRequired();
        entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("uq_organizations_code");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> entity)
    {
        IdentityMapping.Table(entity, "roles");
        entity.HasKey(e => e.Id);
        IdentityMapping.Uuid(entity.Property(e => e.Id), "role_id");
        entity.Property(e => e.Name).HasColumnName("name").HasColumnType("longtext").IsRequired();
        entity.Property(e => e.Description).HasColumnName("description").HasColumnType("longtext");
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> entity)
    {
        IdentityMapping.Table(entity, "permissions");
        entity.HasKey(e => e.Id);
        IdentityMapping.Uuid(entity.Property(e => e.Id), "permission_id");
        entity.Property(e => e.Name).HasColumnName("name").HasColumnType("longtext").IsRequired();
        entity.Property(e => e.Description).HasColumnName("description").HasColumnType("longtext");
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> entity)
    {
        IdentityMapping.Table(entity, "role_permissions");
        entity.HasKey(e => new { e.RoleId, e.PermissionId });
        IdentityMapping.Uuid(entity.Property(e => e.RoleId), "role_id");
        IdentityMapping.Uuid(entity.Property(e => e.PermissionId), "permission_id");
        entity.HasIndex(e => e.PermissionId).HasDatabaseName("ix_role_permissions_permission");
        entity.HasOne(e => e.Role).WithMany(e => e.RolePermissions).HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_role_permissions_role");
        entity.HasOne(e => e.Permission).WithMany(e => e.RolePermissions).HasForeignKey(e => e.PermissionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_role_permissions_permission");
    }
}
