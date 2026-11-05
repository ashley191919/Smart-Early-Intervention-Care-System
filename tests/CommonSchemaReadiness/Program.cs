using System.Data;
using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

int pass = 0, fail = 0;
void Check(string name, Action action)
{
    try { action(); pass++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { fail++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}
void Assert(bool condition, string message = "Assertion failed")
{ if (!condition) throw new InvalidOperationException(message); }
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var ids = new[] {
    "20261005152359_InitialSharedCore", "20261005180101_AddAuditLogs",
    "20261005184434_AddSubmissionReceiptWindow", "20261006074424_IntegrateIdentityAndAuthorization",
    "20261019131342_AddCaseStatusHistories"
};
var hashes = new[] {
    "19d7eb0c62c30c764532d8e351f08d3a3034625f", "05d8bc6042acc4244f7801b99744d7f693c53cac",
    "987f295a6e97b56d361e4f99a4933e9fb89cead9", "0b028bfa5906488ac2dca2434b51b20cd493304a",
    "e082aac5119bf5c374c43d531607ccb046f4631c", "06cb9c143ba32dd350f8d1dec7dfe4e37a6475b0",
    "4b9f09faf9898f955c9995944c775d4db71ba244", "43a7b481b8d95ca03d8e0772c4d6255e332e4e99",
    "8b7b38b820be0dffe3159821edd5dfd28c3cf8b3", "9928bd4ad7b88b1f56fbc97a095315835b050e44"
};
string Blob(string path)
{
    var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n");
    var bytes = Encoding.UTF8.GetBytes(text);
    return Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {bytes.Length}\0").Concat(bytes).ToArray())).ToLowerInvariant();
}
for (var i = 0; i < ids.Length; i++)
for (var j = 0; j < 2; j++)
{
    var index = i * 2 + j;
    var file = ids[i] + (j == 0 ? ".cs" : ".Designer.cs");
    Check("V01 historical blob " + file, () => Assert(Blob(Path.Combine(root,
        "EarlyInterventionCare.Api/Data/Migrations", file)) == hashes[index]));
}
Check("V01 fixed Snapshot blob", () => Assert(Blob(Path.Combine(root,
    "EarlyInterventionCare.Api/Data/Migrations/ApplicationDbContextModelSnapshot.cs")) == "5156015938053ea679d2e05c060c89b63e898e61"));

var events = new List<string>();
using var connection = new OfflineConnection(events) { FailBegin = false, FailCommit = false };
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 46))).Options);
Check("V01 compiled Migration IDs/order and no duplicates", () => {
    var assembly = db.GetService<IMigrationsAssembly>();
    Assert(assembly.Migrations.Keys.SequenceEqual(ids, StringComparer.Ordinal));
});
IModel? baseline = null, current = null;
IReadOnlyList<MigrationOperation>? actual = null;
Check("V02 fifth TargetModel and Snapshot agree, including History", () => {
    var assembly = db.GetService<IMigrationsAssembly>();
    var initializer = db.GetService<IModelRuntimeInitializer>();
    baseline = initializer.Initialize(assembly.ModelSnapshot!.Model, designTime: true);
    var fifth = assembly.CreateMigration(assembly.Migrations[ids[^1]], db.Database.ProviderName!);
    var target = initializer.Initialize(fifth.TargetModel, designTime: true);
    Assert(target.GetEntityTypes().Any(e => e.GetTableName() == "case_status_histories"));
    Assert(baseline.GetEntityTypes().Any(e => e.GetTableName() == "case_status_histories"));
    Assert(db.GetService<IMigrationsModelDiffer>().GetDifferences(target.GetRelationalModel(), baseline.GetRelationalModel()).Count == 0);
});
Check("V03 actual Snapshot to real design model is exact 9 operations", () => {
    Assert(baseline is not null, "Baseline unavailable");
    current = db.GetService<IDesignTimeModel>().Model;
    actual = db.GetService<IMigrationsModelDiffer>().GetDifferences(baseline!.GetRelationalModel(), current.GetRelationalModel());
    Assert(SchemaWhitelist.IsExact(actual, out var reason), reason);
});
Check("V07 retained teacher/Task/History relational metadata", () => {
    Assert(current is not null && actual is not null, "Differ unavailable");
    // An exact additive differ proves no baseline table/key/FK/index alteration.
    Assert(SchemaWhitelist.IsExact(actual!, out var reason), reason);
    foreach (var name in new[] { "uq_responses_task", "uq_responses_grant_key", "uq_tasks_assignment" })
        Assert(current!.GetEntityTypes().SelectMany(e => e.GetIndexes()).Any(i => i.GetDatabaseName() == name && i.IsUnique));
    foreach (var name in new[] { "fk_drafts_grant_scope", "fk_responses_grant_scope", "fk_drafts_task_version", "fk_responses_task_version", "fk_case_status_histories_case", "fk_case_status_histories_changed_by_user" })
        Assert(current!.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).Any(f => f.GetConstraintName() == name && f.DeleteBehavior == DeleteBehavior.Restrict));
});
Check("V14 whitelist positive control", () => Assert(SchemaWhitelist.IsExact(SchemaWhitelist.Expected(), out _)));
void Reject(string name, Action<List<MigrationOperation>> mutate)
{
    Check("V14 reject " + name, () => {
        var ops = SchemaWhitelist.Expected(); mutate(ops);
        Assert(!SchemaWhitelist.IsExact(ops, out _), "Unsafe mutation accepted");
    });
}
Reject("duplicate History CreateTable", o => o.Add(new CreateTableOperation { Name = "case_status_histories" }));
Reject("History replacing allowed column at same count", o => o[0] = new CreateTableOperation { Name = "case_status_histories" });
Reject("extra field", o => o.Add(new AddColumnOperation { Table = "cases", Name = "unapproved", ClrType = typeof(string), IsNullable = true }));
Reject("wrong FK table", o => o.OfType<AddForeignKeyOperation>().First().PrincipalTable = "roles");
Reject("wrong FK principal column", o => o.OfType<AddForeignKeyOperation>().First().PrincipalColumns = ["username"]);
Reject("wrong FK name", o => o.OfType<AddForeignKeyOperation>().First().Name = "wrong");
Reject("Cascade delete", o => o.OfType<AddForeignKeyOperation>().First().OnDelete = ReferentialAction.Cascade);
Reject("Cascade update", o => o.OfType<AddForeignKeyOperation>().First().OnUpdate = ReferentialAction.Cascade);
Reject("NOT NULL", o => o.OfType<AddColumnOperation>().First().IsNullable = false);
Reject("wrong SQL type", o => o.OfType<AddColumnOperation>().First().ColumnType = "varchar(36)");
Reject("wrong CLR type", o => o.OfType<AddColumnOperation>().First().ClrType = typeof(string));
Reject("wrong max length", o => o.OfType<AddColumnOperation>().First(c => c.Name == "hash_version").MaxLength = 65);
Reject("unapproved charset", o => o.OfType<AddColumnOperation>().First()["MySql:CharSet"] = "utf8mb4");
Reject("unapproved collation", o => o.OfType<AddColumnOperation>().First().Collation = "ascii_general_ci");
Reject("constant default", o => o.OfType<AddColumnOperation>().First().DefaultValue = Guid.Empty);
Reject("SQL default", o => o.OfType<AddColumnOperation>().First().DefaultValueSql = "UUID()");
Reject("computed column", o => o.OfType<AddColumnOperation>().First().ComputedColumnSql = "NULL");
Reject("extra provider annotation", o => o.OfType<AddColumnOperation>().First()["Unapproved"] = true);
Reject("nonunique parent key", o => o.OfType<CreateIndexOperation>().Single(i => i.IsUnique).IsUnique = false);
Reject("reordered index columns", o => o.OfType<CreateIndexOperation>().Single(i => i.IsUnique).Columns = ["idempotency_key", "parent_author_user_id"]);
Reject("filtered index", o => o.OfType<CreateIndexOperation>().First().Filter = "parent_author_user_id IS NOT NULL");
Reject("SQL backfill", o => o[0] = new SqlOperation { Sql = "UPDATE synthetic_only SET unknown_value = 1" });
Reject("InsertData", o => o[0] = new InsertDataOperation { Table = "synthetic_only", Columns = ["x"], Values = new object[,] { { 1 } } });
Reject("UpdateData", o => o[0] = new UpdateDataOperation { Table = "synthetic_only", KeyColumns = ["id"], KeyValues = new object[,] { { 1 } }, Columns = ["x"], Values = new object[,] { { 1 } } });
Reject("DeleteData", o => o[0] = new DeleteDataOperation { Table = "synthetic_only", KeyColumns = ["id"], KeyValues = new object[,] { { 1 } } });
Reject("DropTable", o => o[0] = new DropTableOperation { Name = "case_status_histories" });
Reject("missing operation", o => o.RemoveAt(0));
Reject("duplicate operation", o => o[1] = o[0]);
Reject("index before column", o => { var index = o.FindIndex(x => x is CreateIndexOperation); var op = o[index]; o.RemoveAt(index); o.Insert(0, op); });
Reject("FK before column", o => { var index = o.FindIndex(x => x is AddForeignKeyOperation); var op = o[index]; o.RemoveAt(index); o.Insert(0, op); });
Check("V15 no connection, transaction, SQL or tracked rows", () => Assert(events.Count == 0 && connection.State == ConnectionState.Closed && !db.ChangeTracker.Entries().Any()));
var candidateBlocked = CandidateSuite.Run(Check, root);
var blocked = new[] {
    "V09/V10 complete 11 model union not present in this production tree; existing Pinned11 is not production integration",
    "Case legacy required fields and Consent re-consent/history policy need 11/team/hospital decisions",
    "Eligibility generations, ABA, Stamp and global locking protocol remain unapproved/unimplemented",
    "V12/V13 future incremental Migration/Designer/Snapshot/SQL not generated or reviewed; only existing IDs checked",
    "Real MySQL 8.0.46 constraints, collation, concurrent transactions, persistence and recovery NOT tested"
}.Concat(candidateBlocked).ToArray();
foreach (var reason in blocked) Console.WriteLine("BLOCKED " + reason);
Console.WriteLine($"RESULT: {pass} PASS, {fail} FAIL, {blocked.Length} BLOCKED");
return fail > 0 ? 1 : 2;
