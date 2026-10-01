using EarlyInterventionCare.Api.Models.Authorization;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Models.TeacherTokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EarlyInterventionCare.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<FormAccessToken> FormAccessTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci");

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnType("int").UseMySqlIdentityColumn();
            entity.Property(r => r.Name).HasColumnType("longtext").IsRequired();
            entity.Property(r => r.Description).HasColumnType("longtext").IsRequired(false);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnType("int").UseMySqlIdentityColumn();
            entity.Property(p => p.Name).HasColumnType("longtext").IsRequired();
            entity.Property(p => p.Description).HasColumnType("longtext").IsRequired(false);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            entity.Property(rp => rp.RoleId).HasColumnType("int").IsRequired();
            entity.Property(rp => rp.PermissionId).HasColumnType("int").IsRequired();
            entity.HasIndex(rp => rp.PermissionId).HasDatabaseName("FK_RolePermissions_Permissions");
            entity.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId)
                .HasConstraintName("FK_RolePermissions_Roles").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId)
                .HasConstraintName("FK_RolePermissions_Permissions").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnType("int").UseMySqlIdentityColumn();
            entity.Property(u => u.Username).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
            entity.Property(u => u.PasswordHash).HasColumnType("varchar(255)").HasMaxLength(255).IsRequired();
            entity.Property(u => u.RoleId).HasColumnType("int").IsRequired();
            entity.Property(u => u.Phone).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired(false);
            entity.Property(u => u.Status).HasColumnType("varchar(20)").HasMaxLength(20)
                .IsRequired().HasDefaultValue("ACTIVE");
            entity.Property(u => u.LastLoginAt).HasColumnType("datetime").IsRequired(false);
            entity.Property(u => u.CreatedAt).HasColumnType("datetime").IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();

            // MySQL owns the existing DEFAULT and ON UPDATE CURRENT_TIMESTAMP behavior.
            var updatedAt = entity.Property(u => u.UpdatedAt).HasColumnType("datetime").IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();
            updatedAt.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            updatedAt.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

            entity.HasIndex(u => u.Username).IsUnique().HasDatabaseName("Username");
            entity.HasIndex(u => u.RoleId).HasDatabaseName("FK_Users_Roles");
            entity.HasOne(u => u.Role).WithMany().HasForeignKey(u => u.RoleId)
                .HasConstraintName("FK_Users_Roles").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FormAccessToken>(entity =>
        {
            entity.ToTable("form_access_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnType("int").UseMySqlIdentityColumn();
            entity.Property(t => t.TokenHash).HasColumnType("varchar(255)").HasMaxLength(255).IsRequired();
            entity.Property(t => t.CaseId).HasColumnType("int").IsRequired();
            entity.Property(t => t.QuestionnaireId).HasColumnType("int").IsRequired();
            entity.Property(t => t.IssuedByUserId).HasColumnType("int").IsRequired();
            entity.Property(t => t.ExpiresAt).HasColumnType("datetime").IsRequired();
            entity.Property(t => t.UsedAt).HasColumnType("datetime").IsRequired(false);
            entity.Property(t => t.Status).HasColumnType("varchar(20)").HasMaxLength(20)
                .IsRequired().HasDefaultValue("ACTIVE");
            entity.Property(t => t.CreatedAt).HasColumnType("datetime").IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            entity.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("TokenHash");
            entity.HasIndex(t => t.IssuedByUserId).HasDatabaseName("FK_FormAccessTokens_Users");
            entity.HasOne(t => t.IssuedByUser).WithMany().HasForeignKey(t => t.IssuedByUserId)
                .HasConstraintName("FK_FormAccessTokens_Users").OnDelete(DeleteBehavior.Restrict);
        });

        AuthorizationSeed.Seed(modelBuilder);
    }
}
