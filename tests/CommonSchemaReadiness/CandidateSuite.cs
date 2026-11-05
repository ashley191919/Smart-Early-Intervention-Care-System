extern alias Union;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class CandidateSuite
{
    public static List<string> Run(Action<string, Action> check, string root)
    {
        var blocked = new List<string>();
        void Assert(bool value, string why = "Candidate assertion failed") { if (!value) throw new InvalidOperationException(why); }
        var basePath = Path.Combine(root, "tests/CommonSchemaReadiness");
        using var sources = JsonDocument.Parse(File.ReadAllText(Path.Combine(basePath, "union-sources.json")));
        foreach (var p in sources.RootElement.GetProperty("sources").EnumerateArray())
            check("C source blob " + p.GetProperty("source").GetString(), () => {
                var path = p.GetProperty("path").GetString()!;
                var full = Path.Combine(p.GetProperty("kind").GetString() == "linked" ? root : basePath, path);
                var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(full)).Replace("\r\n", "\n");
                var bytes = Encoding.UTF8.GetBytes(text);
                var hash = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {bytes.Length}\0").Concat(bytes).ToArray())).ToLowerInvariant();
                Assert(hash == p.GetProperty("blob").GetString(), "Fixed-source drift");
            });
        check("C reference is fixed d268b7d", () => Assert(sources.RootElement.GetProperty("reference11").GetString() == "d268b7d70be33e04d44bdfa044b13339448815f6"));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(basePath, "candidate-review.json")));
        var expected = report.RootElement.GetProperty("historyTargetToUnion").EnumerateArray().Select(e => e.Clone()).ToArray();
        var operations = new List<MigrationOperation>();
        var stale = new List<MigrationOperation>();
        check("C actual pinned EF union differ (no network/SQL) reproduces 40 and 43", () => {
            var result = Union::UnionCandidateProbe.Read();
            operations.AddRange(result.History); stale.AddRange(result.Stale);
            Assert(CandidateContract.Exact(operations, expected), "Actual typed operations differ from candidate contract");
            Assert(stale.Count == 43 && operations.Count == 40);
        });
        check("C 43-minus-40 is only stale History table and its two indexes", () => {
            var extra = stale.Where(o => o is CreateTableOperation t && t.Name == "case_status_histories" ||
                o is CreateIndexOperation i && i.Table == "case_status_histories").ToArray();
            Assert(extra.Length == 3 && extra.OfType<CreateTableOperation>().Count() == 1 && extra.OfType<CreateIndexOperation>().Count() == 2);
            Assert(CandidateContract.Exact(stale.Except(extra).ToArray(), expected));
        });
        var overlap = new HashSet<string>(SchemaWhitelist.Expected().Select(CandidateContract.Key), StringComparer.Ordinal);
        var candidate8 = new HashSet<string>(StringComparer.Ordinal) {
            "C:users.email", "C:users.email_verified_at_utc", "C:users.normalized_email", "C:users.phone_verified_at_utc",
            "I:users.ix_users_normalized_email", "C:cases.organization_id", "I:cases.IX_cases_organization_id", "F:cases.FK_cases_organizations_organization_id"
        };
        var policy23 = new HashSet<string>(StringComparer.Ordinal) {
            "C:cases.address", "C:cases.national_id", "C:cases.revision",
            "T:consent_document_versions", "T:guardians", "T:parent_consents", "T:case_guardians", "T:parent_binding_invitations",
            "I:case_guardians.IX_case_guardians_guardian_id", "I:case_guardians.uq_case_guardians_active_case",
            "I:consent_document_versions.IX_consent_document_versions_document_code_version",
            "I:guardians.IX_guardians_organization_id_parent_user_id", "I:guardians.IX_guardians_parent_user_id", "I:guardians.uq_guardians_organization_phone",
            "I:parent_binding_invitations.IX_parent_binding_invitations_case_id_guardian_id", "I:parent_binding_invitations.IX_parent_binding_invitations_code_hash",
            "I:parent_binding_invitations.IX_parent_binding_invitations_consumed_by_user_id", "I:parent_binding_invitations.IX_parent_binding_invitations_issued_by_user_id",
            "I:parent_consents.IX_parent_consents_actor_user_id_organization_id_document_versi~", "I:parent_consents.IX_parent_consents_actor_user_id_request_id",
            "I:parent_consents.IX_parent_consents_case_id", "I:parent_consents.IX_parent_consents_document_version_id", "I:parent_consents.IX_parent_consents_organization_id"
        };
        check("C classification is disjoint and exhaustively 9+8+23", () => {
            Assert(overlap.Count == 9 && candidate8.Count == 8 && policy23.Count == 23);
            Assert(!overlap.Overlaps(candidate8) && !overlap.Overlaps(policy23) && !candidate8.Overlaps(policy23));
            Assert(new HashSet<string>(operations.Select(CandidateContract.Key), StringComparer.Ordinal).SetEquals(overlap.Concat(candidate8).Concat(policy23)));
        });
        check("C original author nine occur exactly once and keep exact typed metadata", () =>
            Assert(SchemaWhitelist.IsExact(operations.Where(o => overlap.Contains(CandidateContract.Key(o))).ToArray(), out _)));
        var ordinal = 0;
        foreach (var op in operations)
        {
            ordinal++;
            var key = CandidateContract.Key(op);
            var row = expected.Single(e => CandidateContract.Key(e) == key);
            if (overlap.Contains(key) || candidate8.Contains(key))
                check($"C {ordinal:D2} {(overlap.Contains(key) ? "OVERLAP" : "REVIEW_ONLY")} {key}", () => Assert(CandidateContract.Matches(op, row)));
            else
                blocked.Add($"POLICY {ordinal:D2} {key}: metadata traced, NOT approved; Case default/Guardian/Consent history and uniqueness decision required");
        }
        check("C Candidate eight are nullable/no defaults and exact nonunique indexes/Restrict FK", () => {
            var eight = operations.Where(o => candidate8.Contains(CandidateContract.Key(o))).ToArray();
            Assert(eight.Length == 8 && eight.OfType<AddColumnOperation>().Count() == 5);
            Assert(eight.OfType<AddColumnOperation>().All(c => c.IsNullable && c.DefaultValue is null && c.DefaultValueSql is null));
            Assert(eight.OfType<CreateIndexOperation>().Count() == 2 && eight.OfType<CreateIndexOperation>().All(i => !i.IsUnique));
            Assert(eight.OfType<AddForeignKeyOperation>().Single().OnDelete == ReferentialAction.Restrict);
        });
        check("C observation only: three policy defaults identified, never normalized away", () => {
            var cols = operations.OfType<AddColumnOperation>().Where(c => c.Table == "cases").ToDictionary(c => c.Name);
            Assert(cols["address"].DefaultValue is string a && a.Length == 0 && !cols["address"].IsNullable);
            Assert(cols["national_id"].DefaultValue is string n && n.Length == 0 && !cols["national_id"].IsNullable);
            Assert(cols["revision"].DefaultValue is uint u && u == 0 && !cols["revision"].IsNullable);
        });
        void Reject(string name, Action<List<MigrationOperation>> mutate)
        {
            check("C negative " + name, () => {
                var fresh = Union::UnionCandidateProbe.Read().History.ToList(); mutate(fresh);
                Assert(!CandidateContract.Exact(fresh, expected), "Unsafe candidate mutation accepted");
            });
        }
        Reject("duplicate History", o => o.Add(new CreateTableOperation { Name = "case_status_histories" }));
        Reject("History at unchanged count", o => o[0] = new CreateTableOperation { Name = "case_status_histories" });
        Reject("additional DDL", o => o.Add(new AddColumnOperation { Name = "unknown", Table = "cases", ClrType = typeof(string) }));
        Reject("outside table at same count", o => ((AddColumnOperation)o[0]).Table = "unapproved");
        Reject("wrong FK", o => o.OfType<AddForeignKeyOperation>().First().PrincipalTable = "roles");
        Reject("nested Cascade", o => o.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys).First().OnDelete = ReferentialAction.Cascade);
        Reject("FK Cascade update omitted by JSON", o => o.OfType<AddForeignKeyOperation>().First().OnUpdate = ReferentialAction.Cascade);
        Reject("nullable user column given default", o => o.OfType<AddColumnOperation>().First(c => c.Table == "users").DefaultValue = "fake");
        Reject("Case default changed", o => o.OfType<AddColumnOperation>().First(c => c.Name == "address").DefaultValue = "fake");
        Reject("revision default type changed", o => o.OfType<AddColumnOperation>().First(c => c.Name == "revision").DefaultValue = 0);
        Reject("MaxLength omitted by JSON", o => o.OfType<AddColumnOperation>().First(c => c.Name == "email").MaxLength = 255);
        Reject("computed not stored omitted by JSON", o => o.OfType<CreateTableOperation>().SelectMany(t => t.Columns).Single(c => c.Name == "active_case_id").IsStored = false);
        Reject("table charset omitted by JSON", o => o.OfType<CreateTableOperation>().First()["MySql:CharSet"] = "latin1");
        Reject("table collation omitted by JSON", o => o.OfType<CreateTableOperation>().First()["Relational:Collation"] = "utf8mb4_bin");
        Reject("table provider annotation omitted by JSON", o => o.OfType<CreateTableOperation>().First()["Unapproved"] = true);
        Reject("extra column in CreateTable", o => o.OfType<CreateTableOperation>().First().Columns.Add(new AddColumnOperation { Name = "fake", Table = "consent_document_versions", ClrType = typeof(string), ColumnType = "text" }));
        Reject("Consent unique relaxed", o => o.OfType<CreateIndexOperation>().Single(i => i.Name == "IX_parent_consents_actor_user_id_organization_id_document_versi~").IsUnique = false);
        Reject("Email index becomes unique", o => o.OfType<CreateIndexOperation>().Single(i => i.Name == "ix_users_normalized_email").IsUnique = true);
        Reject("SQL backfill", o => o[0] = new SqlOperation { Sql = "UPDATE synthetic_only SET x=1" });
        Reject("seed insert", o => o[0] = new InsertDataOperation { Table = "synthetic_only" });
        Reject("data update", o => o[0] = new UpdateDataOperation { Table = "synthetic_only" });
        Reject("data delete", o => o[0] = new DeleteDataOperation { Table = "synthetic_only" });
        Reject("drop", o => o[0] = new DropTableOperation { Name = "cases" });
        Reject("duplicate operation", o => o[1] = o[0]);
        Reject("missing operation", o => o.RemoveAt(0));
        Reject("index before new column", o => { var i = o.FindIndex(x => x is CreateIndexOperation); var op = o[i]; o.RemoveAt(i); o.Insert(0, op); });
        Reject("child table before parent", o => { var i = o.FindIndex(x => x is CreateTableOperation t && t.Name == "case_guardians"); var op = o[i]; o.RemoveAt(i); o.Insert(0, op); });
        return blocked;
    }
}
