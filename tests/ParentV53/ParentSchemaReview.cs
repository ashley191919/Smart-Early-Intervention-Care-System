using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

public static class ParentSchemaReview
{
 public static void Write(string path)
 {
  using var db=new ApplicationDbContextFactory().CreateDbContext([]); // Connection interceptor forbids network.
  var assembly=db.GetService<IMigrationsAssembly>();
  var initializer=db.GetService<IModelRuntimeInitializer>();
  IModel Prepare(IModel model)=>initializer.Initialize(model,designTime:true);
  var snapshot=Prepare(assembly.ModelSnapshot!.Model);
  var last=assembly.Migrations.OrderBy(x=>x.Key).Last();
  if(last.Key!="20261019131342_AddCaseStatusHistories")throw new Exception("Unexpected history baseline");
  var history=Prepare(assembly.CreateMigration(last.Value,db.Database.ProviderName!).TargetModel);
  var current=db.GetService<IDesignTimeModel>().Model;
  var differ=db.GetService<IMigrationsModelDiffer>();
  var raw=differ.GetDifferences(snapshot.GetRelationalModel(),current.GetRelationalModel());
  var afterHistory=differ.GetDifferences(history.GetRelationalModel(),current.GetRelationalModel());
  var historyCreates=raw.OfType<CreateTableOperation>().Count(o=>o.Name=="case_status_histories");
  if(historyCreates!=1 || afterHistory.OfType<CreateTableOperation>().Any(o=>o.Name=="case_status_histories"))throw new Exception("History baseline proof failed");
  var report=new {status="REVIEW_REQUIRED_NOT_AUTHORIZED",modelSource="c213e05",provider=db.Database.ProviderName,
   providerVersion="explicit offline factory MySQL 8.0.46; not an actual server",historyBaseline=last.Key,
   historicalMigrations=assembly.Migrations.Keys.Order().ToArray(),snapshotToUnion=raw.Select(Describe).ToArray(),
   historyTargetToUnion=afterHistory.Select(Describe).ToArray(),
   proof=new{historyCreatesFromStaleSnapshot=historyCreates,historyCreatesAfterHistoryTarget=0,connectionState=db.Database.GetDbConnection().State.ToString(),
    generatedMigration=false,executedMigration=false,readDatabase=false}};
  File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine);
  Console.WriteLine($"REVIEW: snapshot {raw.Count} operations; history baseline {afterHistory.Count} operations. No DB/SQL/Migration execution.");
 }
 static object Column(ColumnOperation c)=>new {c.Name,c.Table,c.ColumnType,c.IsNullable,defaultValue=c.DefaultValue?.ToString(),c.DefaultValueSql,c.ComputedColumnSql,c.Collation,charset=c.FindAnnotation("MySql:CharSet")?.Value};
 static object ForeignKey(AddForeignKeyOperation f)=>new {f.Name,f.Table,f.Columns,f.PrincipalTable,f.PrincipalColumns,onDelete=f.OnDelete.ToString()};
 static object Describe(MigrationOperation operation)=>new {kind=operation.GetType().Name,operation.IsDestructiveChange,
  details=operation switch {
   CreateTableOperation t=>(object)new{t.Name,columns=t.Columns.Select(Column).ToArray(),primaryKey=t.PrimaryKey?.Columns,
    foreignKeys=t.ForeignKeys.Select(ForeignKey).ToArray(),uniqueConstraints=t.UniqueConstraints.Select(u=>new{u.Name,u.Columns}).ToArray()},
   AddColumnOperation c=>Column(c),
   AlterColumnOperation c=>new{current=Column(c),old=Column(c.OldColumn)},
   CreateIndexOperation i=>new{i.Name,i.Table,i.Columns,i.IsUnique,i.Filter},
   AddForeignKeyOperation f=>ForeignKey(f),
   DropColumnOperation c=>new{c.Name,c.Table},DropTableOperation t=>new{t.Name},DropIndexOperation i=>new{i.Name,i.Table},
   _=>new{review="UNCLASSIFIED: must not auto-approve"}
  }};
}
