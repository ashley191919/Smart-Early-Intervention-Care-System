using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.ParentModule;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

int passed = 0, failed = 0;
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/ParentModelUnion/source-manifest.json")));
string Blob(string relative)
{
    var bytes = File.ReadAllBytes(Path.Combine(root, relative));
    // Git text checkout may use CRLF. Upstream snapshots are UTF-8/LF with no header edits.
    var text = new UTF8Encoding(false, true).GetString(bytes).Replace("\r\n", "\n");
    var normalized = Encoding.UTF8.GetBytes(text);
    return Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {normalized.Length}\0").Concat(normalized).ToArray())).ToLowerInvariant();
}
foreach (var pin in manifest.RootElement.GetProperty("sources11").EnumerateArray())
    Check("verbatim 11 source " + pin.GetProperty("upstreamPath").GetString(), () =>
        Assert(Blob(pin.GetProperty("localPath").GetString()!) == pin.GetProperty("blob").GetString()));
foreach (var pin in manifest.RootElement.GetProperty("sources16").EnumerateArray())
    Check("unchanged linked 16 source " + pin.GetProperty("path").GetString(), () =>
        Assert(Blob(pin.GetProperty("path").GetString()!) == pin.GetProperty("blob").GetString()));
foreach (var pin in manifest.RootElement.GetProperty("history16").EnumerateArray())
    Check("history/Snapshot preserved " + pin.GetProperty("path").GetString(), () =>
        Assert(Blob(pin.GetProperty("path").GetString()!) == pin.GetProperty("blob").GetString()));

var events = new List<string>();
using var connection = new OfflineConnection(events) { FailBegin = false, FailCommit = false };
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 46))).Options);
IModel? model = null;
Check("actual unmodified 11 Context builds union EF/Pomelo design-time and relational model", () => {
    model = db.GetService<IDesignTimeModel>().Model;
    Assert(model.GetRelationalModel().Tables.Any());
});
if (model is not null)
{
    foreach (var (type, table) in new[] {
        (typeof(CaseRecord), "cases"), (typeof(GuardianContact), "guardians"),
        (typeof(CaseGuardian), "case_guardians"), (typeof(ParentBindingInvitation), "parent_binding_invitations"),
        (typeof(ConsentDocumentVersion), "consent_document_versions"), (typeof(ParentConsentRecord), "parent_consents"),
        (typeof(CaseStatusHistory), "case_status_histories"), (typeof(QuestionnaireDraft), "questionnaire_drafts"),
        (typeof(QuestionnaireResponse), "questionnaire_responses") })
        Check("real Entity/Configuration retained " + table, () => Assert(model.FindEntityType(type)?.GetTableName() == table));

    foreach (var (type, name, column, sqlType, length) in new[] {
        (typeof(QuestionnaireDraft), "ParentAuthorUserId", "parent_author_user_id", "char(36)", (int?)null),
        (typeof(QuestionnaireResponse), "ParentAuthorUserId", "parent_author_user_id", "char(36)", (int?)null),
        (typeof(QuestionnaireDraft), "RespondentRelationship", "respondent_relationship", "varchar(32)", (int?)32),
        (typeof(QuestionnaireResponse), "RespondentRelationship", "respondent_relationship", "varchar(32)", (int?)32),
        (typeof(QuestionnaireResponse), "HashVersion", "hash_version", "varchar(64)", (int?)64) })
        Check($"five-column union: {type.Name}.{name}", () => {
            var e = model.FindEntityType(type)!; var p = e.FindProperty(name)!;
            var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
            Assert(p.IsNullable && p.GetColumnName(table) == column && p.GetColumnType() == sqlType);
            Assert(length is null || p.GetMaxLength() == length);
            Assert(p.FindAnnotation(RelationalAnnotationNames.DefaultValue) is null && p.GetDefaultValueSql() is null && p.GetComputedColumnSql() is null && p.ValueGenerated == ValueGenerated.Never);
        });
    foreach (var type in new[] { typeof(QuestionnaireDraft), typeof(QuestionnaireResponse) })
        Check(type.Name + " parent author FK and ASCII binary mapping preserved", () => {
            var e = model.FindEntityType(type)!; var p = e.FindProperty("ParentAuthorUserId")!;
            var fk = e.GetForeignKeys().Single(x => x.Properties.Count == 1 && x.Properties[0] == p);
            Assert(fk.PrincipalEntityType.ClrType == typeof(User) && fk.PrincipalKey.Properties.Single().Name == "Id" && fk.DeleteBehavior == DeleteBehavior.Restrict);
            Assert((string?)p.FindAnnotation("MySql:CharSet")?.Value == "ascii" && p.GetCollation() == "ascii_bin");
        });
    foreach (var (type, constraint, properties) in new[] {
        (typeof(QuestionnaireDraft), "fk_drafts_task_version", new[] { "TaskId", "QuestionnaireVersionId" }),
        (typeof(QuestionnaireResponse), "fk_responses_task_version", new[] { "TaskId", "QuestionnaireVersionId" }),
        (typeof(QuestionnaireDraft), "fk_drafts_grant_scope", new[] { "LastSavedByGrantId", "TaskId" }),
        (typeof(QuestionnaireResponse), "fk_responses_grant_scope", new[] { "SubmittedByGrantId", "TaskId" }) })
        Check("shared teacher/task FK " + constraint, () => Assert(model.FindEntityType(type)!.GetForeignKeys().Any(f =>
            f.GetConstraintName() == constraint && f.Properties.Select(p => p.Name).SequenceEqual(properties) && f.DeleteBehavior == DeleteBehavior.Restrict)));
    foreach (var (type, name, unique, columns) in new[] {
        (typeof(QuestionnaireDraft), "ix_drafts_parent_author", false, new[] { "ParentAuthorUserId" }),
        (typeof(QuestionnaireResponse), "uq_responses_parent_key", true, new[] { "ParentAuthorUserId", "IdempotencyKey" }),
        (typeof(QuestionnaireResponse), "uq_responses_grant_key", true, new[] { "SubmittedByGrantId", "IdempotencyKey" }),
        (typeof(QuestionnaireResponse), "uq_responses_task", true, new[] { "TaskId" }),
        (typeof(GuardianContact), "uq_guardians_organization_phone", true, new[] { "OrganizationId", "Phone" }),
        (typeof(CaseGuardian), "uq_case_guardians_active_case", true, new[] { "ActiveCaseId" }),
        (typeof(CaseStatusHistory), "ix_case_status_histories_case_time", false, new[] { "CaseId", "ChangedAtUtc", "HistoryId" }) })
        Check("union index " + name, () => Assert(model.FindEntityType(type)!.GetIndexes().Any(i =>
            i.GetDatabaseName() == name && i.IsUnique == unique && i.Properties.Select(p => p.Name).SequenceEqual(columns))));
    Check("Case ownership and concurrency remain 11's real model", () => {
        var e = model.FindEntityType(typeof(CaseRecord))!;
        Assert(e.FindProperty("OrganizationId")!.IsNullable && e.FindProperty("Revision")!.IsConcurrencyToken);
        Assert(e.FindProperty("NationalId")!.GetMaxLength() == 10 && !e.FindProperty("NationalId")!.IsNullable);
        Assert(e.FindProperty("Address")!.GetMaxLength() == 300 && !e.FindProperty("Address")!.IsNullable);
        Assert(e.GetForeignKeys().Any(x => x.Properties.SingleOrDefault()?.Name == "OrganizationId" && x.DeleteBehavior == DeleteBehavior.Restrict));
    });
    Check("existing teacher Draft revision remains concurrency token", () => Assert(model.FindEntityType(typeof(QuestionnaireDraft))!.FindProperty("Revision")!.IsConcurrencyToken));
    Check("Consent scope uniqueness and all four Restrict FKs survive", () => {
        var e = model.FindEntityType(typeof(ParentConsentRecord))!;
        Assert(e.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ActorUserId", "RequestId" })));
        Assert(e.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ActorUserId", "OrganizationId", "DocumentVersionId", "ScopeKey" })));
        Assert(e.GetForeignKeys().Count() == 4 && e.GetForeignKeys().All(f => f.DeleteBehavior == DeleteBehavior.Restrict));
    });
    Check("CaseGuardian computed primary-contact expression survives", () => {
        var p = model.FindEntityType(typeof(CaseGuardian))!.FindProperty("ActiveCaseId")!;
        Assert(p.GetComputedColumnSql() == "CASE WHEN link_status = 'ACTIVE' AND is_primary_contact = 1 THEN case_id ELSE NULL END" && p.GetIsStored() == true);
    });
    Check("History retains Case and nullable User FKs", () => {
        var e = model.FindEntityType(typeof(CaseStatusHistory))!;
        Assert(e.GetForeignKeys().Count() == 2 && e.GetForeignKeys().All(f => f.DeleteBehavior == DeleteBehavior.Restrict));
        Assert(e.FindProperty("ChangedByUserId")!.IsNullable);
    });
    Check("History mapped through scanning even though 11 Context has no History DbSet", () => {
        Assert(typeof(ApplicationDbContext).GetProperty("CaseStatusHistories") is null);
        Assert(model.FindEntityType(typeof(CaseStatusHistory)) is not null);
        Assert(typeof(ApplicationDbContext).GetProperty("Guardians") is not null && typeof(ApplicationDbContext).GetProperty("CaseGuardians") is not null);
    });
    Check("no invented eligibility generation or effective-round columns", () => {
        Assert(!model.GetEntityTypes().SelectMany(e => e.GetProperties()).Any(p => p.Name.Contains("Generation") || p.Name.Contains("Epoch") || p.Name.Contains("EffectiveRound")));
    });
}
Check("model-only execution never opens a connection, transaction or writes data", () => Assert(events.Count == 0 && connection.State == System.Data.ConnectionState.Closed));
Console.WriteLine("BLOCKED full API/service source union: 11 Context has no CaseStatusHistories DbSet required by 16 stores; full application not compiled here");
Console.WriteLine("BLOCKED combined model/Snapshot/Migration alignment and legacy Case/Consent data policies");
Console.WriteLine("BLOCKED real MySQL DDL, FK/unique/computed behavior, isolation and recovery acceptance");
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL, 3 BLOCKED; real selected source compilation + EF model only; no database");
return failed > 0 ? 1 : 2;
