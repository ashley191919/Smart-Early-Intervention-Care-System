using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Stage-one model contract. author-v1's nine operations remain independently exact.
internal static class UserStageOne
{
    public static bool IsUserOperation(MigrationOperation op) => op is AddColumnOperation c && c.Table == "users" ||
        op is CreateIndexOperation i && i.Table == "users";

    public static List<MigrationOperation> Expected()
    {
        var result = new List<MigrationOperation>();
        foreach (var (name, collation) in new[] { ("email", "utf8mb4_unicode_ci"), ("normalized_email", "utf8mb4_bin") })
        {
            var column = new AddColumnOperation { Table = "users", Name = name, ClrType = typeof(string),
                ColumnType = "varchar(254)", MaxLength = 254, IsNullable = true, Collation = collation };
            column["MySql:CharSet"] = "utf8mb4";
            result.Add(column);
        }
        foreach (var name in new[] { "phone_verified_at_utc", "email_verified_at_utc" })
            result.Add(new AddColumnOperation { Table = "users", Name = name, ClrType = typeof(DateTime),
                ColumnType = "datetime(6)", IsNullable = true });
        result.Add(new CreateIndexOperation { Table = "users", Name = "ix_users_normalized_email", Columns = ["normalized_email"], IsUnique = false });
        return result;
    }
    public static bool ExactFive(IReadOnlyList<MigrationOperation> operations)
    {
        if (operations.Count != 5 || operations.Any(o => o is not (AddColumnOperation or CreateIndexOperation))) return false;
        if (!operations.Select(SchemaWhitelist.Signature).Order(StringComparer.Ordinal)
            .SequenceEqual(Expected().Select(SchemaWhitelist.Signature).Order(StringComparer.Ordinal), StringComparer.Ordinal)) return false;
        var array = operations.ToList();
        return array.FindIndex(o => o is AddColumnOperation c && c.Name == "normalized_email") <
            array.FindIndex(o => o is CreateIndexOperation);
    }
    public static bool ExactFourteen(IReadOnlyList<MigrationOperation> operations) => operations.Count == 14 &&
        SchemaWhitelist.IsExact(operations.Where(o => !IsUserOperation(o)).ToArray(), out _) &&
        ExactFive(operations.Where(IsUserOperation).ToArray());

    public static void Run(Action<string, Action> check, IReadOnlyList<MigrationOperation>? actual, IModel? model, string root)
    {
        void Assert(bool value) { if (!value) throw new InvalidOperationException("User stage-one contract mismatch"); }
        check("U model-v2 exact 14 = author-v1 nine + user-v1 five", () => Assert(actual is not null && ExactFourteen(actual)));
        check("U user-v1 independently exact five operations", () => Assert(actual is not null && ExactFive(actual.Where(IsUserOperation).ToArray())));
        foreach (var (name, sql, length, collation) in new[] {
            ("PhoneVerifiedAtUtc", "datetime(6)", (int?)null, (string?)null),
            ("Email", "varchar(254)", (int?)254, "utf8mb4_unicode_ci"),
            ("NormalizedEmail", "varchar(254)", (int?)254, "utf8mb4_bin"),
            ("EmailVerifiedAtUtc", "datetime(6)", (int?)null, (string?)null) })
            check("U nullable/no-default property " + name, () => {
                Assert(model is not null);
                var property = model!.FindEntityType(typeof(User))!.FindProperty(name)!;
                Assert(property.IsNullable && property.GetColumnType() == sql && property.GetMaxLength() == length);
                if (collation is not null)
                    Assert((property.GetCollation() ?? model!.FindEntityType(typeof(User))!.GetCollation() ?? model.GetCollation()) == collation);
                Assert(property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is null &&
                    property.GetDefaultValueSql() is null && property.GetComputedColumnSql() is null && property.ValueGenerated == ValueGenerated.Never);
            });
        check("U new users never gain inferred email/phone verification", () => {
            var user = new User();
            Assert(user.PhoneVerifiedAtUtc is null && user.Email is null && user.NormalizedEmail is null && user.EmailVerifiedAtUtc is null);
        });
        check("U original username/password/status/organization/role model preserved", () => {
            var entity = model!.FindEntityType(typeof(User))!;
            Assert(entity.FindProperty("Username")!.GetMaxLength() == 100 && !entity.FindProperty("Username")!.IsNullable);
            Assert(entity.FindProperty("PasswordHash")!.GetMaxLength() == 255 && !entity.FindProperty("PasswordHash")!.IsNullable);
            Assert((string?)entity.FindProperty("Status")!.GetDefaultValue() == "ACTIVE");
            Assert(entity.GetIndexes().Any(i => i.GetDatabaseName() == "uq_users_username" && i.IsUnique));
            Assert(entity.GetIndexes().Any(i => i.GetDatabaseName() == "ix_users_normalized_email" && !i.IsUnique));
            Assert(entity.GetForeignKeys().Count() == 2 && entity.GetForeignKeys().All(f => f.DeleteBehavior == DeleteBehavior.Restrict));
        });
        foreach (var (path, expected) in new[] {
            ("EarlyInterventionCare.Api/Models/Authentication/User.cs", "41d1e9ee9e5297dce3c61245ab0e16154a232c9a"),
            ("EarlyInterventionCare.Api/Data/IdentityModelConfiguration.cs", "b467d9e575125f522e66ecd2e71316624794dfa8") })
            check("U exact reviewed 11 source " + path, () => {
                var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(root, path))).Replace("\r\n", "\n");
                var bytes = Encoding.UTF8.GetBytes(text);
                var hash = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {bytes.Length}\0").Concat(bytes).ToArray())).ToLowerInvariant();
                Assert(hash == expected);
            });
        void Reject(string name, Action<List<MigrationOperation>> mutate)
        {
            check("U negative " + name, () => { var ops = Expected(); mutate(ops); Assert(!ExactFive(ops)); });
        }
        Reject("unique Email index", o => o.OfType<CreateIndexOperation>().Single().IsUnique = true);
        Reject("wrong binary collation", o => o.OfType<AddColumnOperation>().Single(c => c.Name == "normalized_email").Collation = "utf8mb4_unicode_ci");
        Reject("default email", o => o.OfType<AddColumnOperation>().First().DefaultValue = "synthetic");
        Reject("inferred verification time", o => o.OfType<AddColumnOperation>().Single(c => c.Name == "phone_verified_at_utc").DefaultValueSql = "CURRENT_TIMESTAMP");
        Reject("non-null verification", o => o.OfType<AddColumnOperation>().Last().IsNullable = false);
        Reject("wrong maximum length", o => o.OfType<AddColumnOperation>().First().MaxLength = 255);
        Reject("extra sixth operation", o => o.Add(new AddColumnOperation { Table = "cases", Name = "unapproved", ClrType = typeof(string) }));
        Reject("backfill", o => o[0] = new SqlOperation { Sql = "UPDATE synthetic_only SET x=1" });
        Reject("index before added field", o => { var index = o[^1]; o.RemoveAt(o.Count - 1); o.Insert(0, index); });
        check("U no Case/Guardian/Consent operations in actual stage-one differ", () => Assert(actual is not null && actual.All(o =>
            SchemaWhitelist.Expected().Select(CandidateContract.Key).Contains(CandidateContract.Key(o)) || IsUserOperation(o))));
    }
}
