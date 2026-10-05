using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
