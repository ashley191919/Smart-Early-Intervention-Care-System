using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Models.Authorization;

namespace EarlyInterventionCare.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<CaseRecord> Cases => Set<CaseRecord>();
    public DbSet<Questionnaire> Questionnaires => Set<Questionnaire>();
    public DbSet<QuestionnaireVersion> QuestionnaireVersions => Set<QuestionnaireVersion>();
    public DbSet<QuestionnaireTask> QuestionnaireTasks => Set<QuestionnaireTask>();
    public DbSet<TeacherGrant> TeacherGrants => Set<TeacherGrant>();
    public DbSet<TeacherGrantTask> TeacherGrantTasks => Set<TeacherGrantTask>();
    public DbSet<TeacherSession> TeacherSessions => Set<TeacherSession>();
    public DbSet<QuestionnaireDraft> QuestionnaireDrafts => Set<QuestionnaireDraft>();
    public DbSet<QuestionnaireResponse> QuestionnaireResponses => Set<QuestionnaireResponse>();
    public DbSet<AuditRecord> AuditLogs => Set<AuditRecord>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        AuthorizationSeed.Seed(modelBuilder);
    }
}
