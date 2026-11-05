using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.ParentModule;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Runtime-model assertions only. Matching a policy candidate is NOT approval to execute it.
internal static class ModelStageTwo
{
    public static void Run(Action<string, Action> check, IReadOnlyList<MigrationOperation>? actual, IModel? model, string root)
    {
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Stage-two runtime contract mismatch"); }
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/CommonSchemaReadiness/candidate-review.json")));
        var expected = report.RootElement.GetProperty("historyTargetToUnion").EnumerateArray().Select(e => e.Clone()).ToArray();
        check("S2 actual production differ is precisely fixed eleven's 40 typed candidate operations", () =>
            Assert(actual is not null && CandidateContract.Exact(actual, expected)));
        check("S2 original author nine occur once, including provider metadata", () => {
            var keys = SchemaWhitelist.Expected().Select(CandidateContract.Key).ToHashSet(StringComparer.Ordinal);
            Assert(actual is not null && SchemaWhitelist.IsExact(actual.Where(o => keys.Contains(CandidateContract.Key(o))).ToArray(), out _));
        });
        check("S2 original user five remain exact", () => Assert(actual is not null && UserStageOne.ExactFive(actual.Where(UserStageOne.IsUserOperation).ToArray())));
        foreach (var table in new[] { "guardians", "case_guardians", "parent_binding_invitations", "consent_document_versions", "parent_consents" })
            check("S2 candidate table mapped once " + table, () => Assert(model!.GetEntityTypes().Count(e => e.GetTableName() == table) == 1));
        check("S2 all old DbSets retained and three guardian DbSets added", () => {
            var sets = typeof(ApplicationDbContext).GetProperties().Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)).Select(p => p.Name).ToHashSet();
            Assert(sets.SetEquals(new[] { "Cases", "Questionnaires", "QuestionnaireVersions", "QuestionnaireTasks", "TeacherGrants", "TeacherGrantTasks", "TeacherSessions", "QuestionnaireDrafts", "QuestionnaireResponses", "AuditLogs", "CaseStatusHistories", "Users", "Roles", "Permissions", "RolePermissions", "Organizations", "Guardians", "CaseGuardians", "ParentBindingInvitations" }));
        });
        check("S2 nullable Case organization, required strings and concurrency revision match candidate", () => {
            var e = model!.FindEntityType(typeof(CaseRecord))!;
            Assert(e.FindProperty("OrganizationId")!.IsNullable && e.FindProperty("OrganizationId")!.ClrType == typeof(Guid?));
            Assert(e.FindProperty("Revision")!.IsConcurrencyToken && e.FindProperty("Revision")!.ClrType == typeof(uint));
            foreach (var (name, length) in new[] { ("NationalId", 10), ("Address", 300) })
                Assert(!e.FindProperty(name)!.IsNullable && e.FindProperty(name)!.GetMaxLength() == length);
        });
        check("S2 Guardian revision is concurrency metadata, not eligibility generation", () => {
            var e = model!.FindEntityType(typeof(GuardianContact))!;
            Assert(e.FindProperty("Revision")!.IsConcurrencyToken && e.FindProperty("ParentUserId")!.IsNullable);
            Assert(e.GetProperties().All(p => !p.Name.Contains("Generation", StringComparison.Ordinal) && !p.Name.Contains("Epoch", StringComparison.Ordinal)));
        });
        check("S2 stored ActiveCaseId computed expression and composite binding PK", () => {
            var e = model!.FindEntityType(typeof(CaseGuardian))!; var p = e.FindProperty("ActiveCaseId")!;
            Assert(p.IsNullable && p.GetIsStored() == true && p.ValueGenerated == ValueGenerated.OnAddOrUpdate);
            Assert(p.GetComputedColumnSql() == "CASE WHEN link_status = 'ACTIVE' AND is_primary_contact = 1 THEN case_id ELSE NULL END");
            Assert(e.FindPrimaryKey()!.Properties.Select(x => x.Name).SequenceEqual(new[] { "CaseId", "GuardianId" }));
        });
        check("S2 every new relationship is Restrict, preserving old teacher and History FKs", () =>
            Assert(model!.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).All(f => f.DeleteBehavior == DeleteBehavior.Restrict)));
        check("S2 candidate consent unique keys preserved, re-consent policy NOT approved", () => {
            var e = model!.FindEntityType(typeof(ParentConsentRecord))!;
            Assert(e.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ActorUserId", "RequestId" })));
            Assert(e.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ActorUserId", "OrganizationId", "DocumentVersionId", "ScopeKey" })));
        });
        check("S2 no inferred organization or approved Consent on new objects", () => {
            Assert(new CaseRecord().OrganizationId is null);
            Assert(new GuardianContact().ParentUserId is null);
            var d = new ConsentDocumentVersion(); var c = new ParentConsentRecord();
            Assert(d.ApprovalStatus == "DRAFT" && d.ScopeCode == "UNCONFIRMED" && d.ApprovedAtUtc is null);
            Assert(c.DataUseDecision == "UNDECIDED" && c.TeacherParticipation == "UNDECIDED");
        });
        check("S2 no History recreation, destructive operations, backfill or extra DDL", () => {
            Assert(actual is not null && CandidateContract.Exact(actual, expected));
            Assert(!actual!.OfType<CreateTableOperation>().Any(t => t.Name == "case_status_histories"));
        });
        foreach (var relative in new[] { "Data/Entities/GuardianContact.cs", "Data/GuardianModelConfiguration.cs", "ParentModule/ConsentModels.cs" })
            check("S2 production new source equals fixed pinned source " + relative, () => {
                string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");
                Assert(Read(Path.Combine(root, "EarlyInterventionCare.Api", relative)) == Read(Path.Combine(root, "tests/CommonSchemaReadiness/UnionModel/Pinned11", relative)));
            });
    }
}
