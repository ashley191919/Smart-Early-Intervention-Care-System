using System.Data.Common;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

public static class ParentModelIntegrationSuite
{
 public static void Run(Action<bool,string> check)
 {
  // Explicit server version: no AutoDetect, no connection or schema operations.
  var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseMySql(
   "Server=127.0.0.1;Port=1;Database=offline_model;User ID=unused;Password=unused;",new MySqlServerVersion(new Version(8,4,11))).Options;
  using var db = new ApplicationDbContext(options);
  var model=db.GetService<IDesignTimeModel>().Model;
  foreach(var pair in new[]{(typeof(QuestionnaireDraft),"ParentAuthorUserId"),(typeof(QuestionnaireDraft),"RespondentRelationship"),
   (typeof(QuestionnaireResponse),"ParentAuthorUserId"),(typeof(QuestionnaireResponse),"RespondentRelationship"),(typeof(QuestionnaireResponse),"HashVersion")})
  {
   var p=model.FindEntityType(pair.Item1)!.FindProperty(pair.Item2)!;
   check(p.IsNullable && p.GetDefaultValueSql() is null,"nullable additive field with no SQL default: "+pair.Item2);
  }
  foreach(var type in new[]{typeof(QuestionnaireDraft),typeof(QuestionnaireResponse)})
  {
   var entity=model.FindEntityType(type)!;
   check(entity.GetForeignKeys().Any(f=>f.Properties.Any(p=>p.Name=="ParentAuthorUserId")&&f.DeleteBehavior==DeleteBehavior.Restrict),"parent FK Restrict retained");
   check(entity.GetForeignKeys().Any(f=>f.GetConstraintName()!.Contains("grant_scope")),"teacher grant-scope FK retained");
   check(entity.GetForeignKeys().Any(f=>f.GetConstraintName()!.Contains("task_version")),"task-version FK retained");
  }
  var response=model.FindEntityType(typeof(QuestionnaireResponse))!;
  foreach(var name in new[]{"uq_responses_task","uq_responses_grant_key","uq_responses_parent_key"})
   check(response.GetIndexes().Any(i=>i.GetDatabaseName()==name&&i.IsUnique),"unique key retained: "+name);
  var caseEntity=model.FindEntityType(typeof(CaseRecord))!;
  foreach(var field in new[]{"OrganizationId","Revision","NationalId","Address"})check(caseEntity.FindProperty(field)!=null,"11 Case field preserved: "+field);
  foreach(var type in new[]{typeof(GuardianContact),typeof(CaseGuardian),typeof(ParentBindingInvitation),typeof(ParentConsentRecord),typeof(ConsentDocumentVersion),typeof(CaseStatusHistory)})
   check(model.FindEntityType(type)!=null,"union entity preserved: "+type.Name);
  check(db.CaseStatusHistories is not null,"History DbSet compiles against union context");
  var migrations=db.Database.GetMigrations().ToArray();
  check(migrations.Length==5 && migrations.Last()=="20261019131342_AddCaseStatusHistories","five historical migrations discoverable offline");
  check(db.Database.GetDbConnection().State==System.Data.ConnectionState.Closed,"Pomelo model inspection never opens connection");
  var services=new ServiceCollection(); services.AddParentV53();
  check(services.Single(x=>x.ServiceType==typeof(IParentAuthorMetadataReader)).ImplementationType==typeof(EfParentAuthorMetadataReader),"module registers real EF reader");
  check(services.Single(x=>x.ServiceType==typeof(IParentQuestionnaireGateway)).ImplementationType==typeof(UnavailableParentQuestionnaireGateway),"formal answer gateway still unavailable");
 }
}
public sealed class ReaderSqlRecorder : DbCommandInterceptor
{
 public List<string> Commands {get;}=new();
 public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
 {Commands.Add(command.CommandText);return ValueTask.FromResult(result);}
}
