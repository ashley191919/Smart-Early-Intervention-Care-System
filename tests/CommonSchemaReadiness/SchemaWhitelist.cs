using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Tests only. Compares relational operations, never generates or executes SQL.
internal static class SchemaWhitelist
{
    public static List<MigrationOperation> Expected()
    {
        var result = new List<MigrationOperation>();
        foreach (var table in new[] { "questionnaire_drafts", "questionnaire_responses" })
        {
            var author = new AddColumnOperation {
                Table = table, Name = "parent_author_user_id", ClrType = typeof(Guid),
                ColumnType = "char(36)", IsNullable = true, Collation = "ascii_bin"
            };
            author["MySql:CharSet"] = "ascii";
            result.Add(author);
            var relationship = new AddColumnOperation {
                Table = table, Name = "respondent_relationship", ClrType = typeof(string),
                ColumnType = "varchar(32)", MaxLength = 32, IsNullable = true,
                Collation = "utf8mb4_unicode_ci"
            };
            relationship["MySql:CharSet"] = "utf8mb4";
            result.Add(relationship);
        }
        var hashVersion = new AddColumnOperation {
            Table = "questionnaire_responses", Name = "hash_version", ClrType = typeof(string),
            ColumnType = "varchar(64)", MaxLength = 64, IsNullable = true,
            Collation = "utf8mb4_unicode_ci"
        };
        hashVersion["MySql:CharSet"] = "utf8mb4";
        result.Add(hashVersion);
        result.Add(new CreateIndexOperation {
            Table = "questionnaire_drafts", Name = "ix_drafts_parent_author",
            Columns = ["parent_author_user_id"], IsUnique = false
        });
        result.Add(new CreateIndexOperation {
            Table = "questionnaire_responses", Name = "uq_responses_parent_key",
            Columns = ["parent_author_user_id", "idempotency_key"], IsUnique = true
        });
        foreach (var (table, name) in new[] {
            ("questionnaire_drafts", "fk_drafts_parent_author"),
            ("questionnaire_responses", "fk_responses_parent_author") })
            result.Add(new AddForeignKeyOperation {
                Table = table, Name = name, Columns = ["parent_author_user_id"],
                PrincipalTable = "users", PrincipalColumns = ["user_id"],
                OnDelete = ReferentialAction.Restrict, OnUpdate = ReferentialAction.NoAction
            });
        return result;
    }

    public static bool IsExact(IReadOnlyList<MigrationOperation> operations, out string reason)
    {
        if (operations.Count != 9) { reason = $"Expected 9 operations; got {operations.Count}"; return false; }
        if (operations.Any(o => o is not (AddColumnOperation or CreateIndexOperation or AddForeignKeyOperation)))
        { reason = "Operation type outside whitelist"; return false; }
        var actual = operations.Select(Signature).Order(StringComparer.Ordinal).ToArray();
        var expected = Expected().Select(Signature).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            reason = "Operation metadata differs: " + string.Join("; ", actual.Except(expected, StringComparer.Ordinal));
            return false;
        }
        // Dependencies must precede consumers, even though independent operation order can vary.
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            var dependency = op switch {
                CreateIndexOperation x => (x.Table, x.Columns),
                AddForeignKeyOperation x => (x.Table, x.Columns),
                _ => ((string?)null, Array.Empty<string>())
            };
            if (dependency.Item1 is null) continue;
            foreach (var column in dependency.Item2)
            {
                var added = operations.Select((x, n) => (x, n)).FirstOrDefault(v =>
                    v.x is AddColumnOperation c && c.Table == dependency.Item1 && c.Name == column);
                if (added.x is not null && added.n >= i)
                { reason = "Index/FK precedes its added column"; return false; }
            }
        }
        reason = "Exact five-column/two-index/two-FK additive whitelist";
        return true;
    }

    private static string Signature(MigrationOperation op)
    {
        object details = op switch {
            AddColumnOperation x => new {
                x.Table, x.Schema, x.Name, ClrType = x.ClrType.FullName, x.ColumnType,
                x.IsNullable, x.MaxLength, x.IsUnicode, x.IsFixedLength, x.Precision, x.Scale,
                x.IsRowVersion, x.DefaultValue, x.DefaultValueSql, x.ComputedColumnSql,
                x.IsStored, x.Comment, x.Collation
            },
            CreateIndexOperation x => new {
                x.Table, x.Schema, x.Name, x.Columns, x.IsUnique, x.Filter, x.IsDescending
            },
            AddForeignKeyOperation x => new {
                x.Table, x.Schema, x.Name, x.Columns, x.PrincipalTable, x.PrincipalSchema,
                x.PrincipalColumns, x.OnDelete, x.OnUpdate
            },
            _ => throw new InvalidOperationException("Unsupported operation")
        };
        return JsonSerializer.Serialize(new {
            Type = op.GetType().Name, op.IsDestructiveChange, Details = details,
            Annotations = op.GetAnnotations().OrderBy(a => a.Name, StringComparer.Ordinal)
                .Select(a => new { a.Name, a.Value }).ToArray()
        });
    }
}
