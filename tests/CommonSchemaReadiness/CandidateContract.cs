using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class CandidateContract
{
    public static string Key(MigrationOperation op) => op switch {
        AddColumnOperation x => "C:" + x.Table + "." + x.Name,
        CreateIndexOperation x => "I:" + x.Table + "." + x.Name,
        AddForeignKeyOperation x => "F:" + x.Table + "." + x.Name,
        CreateTableOperation x => "T:" + x.Name,
        _ => throw new InvalidOperationException("DDL/DML type outside candidate review")
    };
    public static string Key(JsonElement op)
    {
        var d = op.GetProperty("details");
        return op.GetProperty("kind").GetString() switch {
            "AddColumnOperation" => "C:" + d.GetProperty("Table").GetString() + "." + d.GetProperty("Name").GetString(),
            "CreateIndexOperation" => "I:" + d.GetProperty("Table").GetString() + "." + d.GetProperty("Name").GetString(),
            "AddForeignKeyOperation" => "F:" + d.GetProperty("Table").GetString() + "." + d.GetProperty("Name").GetString(),
            "CreateTableOperation" => "T:" + d.GetProperty("Name").GetString(),
            _ => throw new InvalidOperationException("Unclassified report operation")
        };
    }
    public static object Column(ColumnOperation c) => new { c.Name, c.Table, c.ColumnType, c.IsNullable,
        defaultValue = c.DefaultValue?.ToString(), c.DefaultValueSql, c.ComputedColumnSql, c.Collation,
        charset = c.FindAnnotation("MySql:CharSet")?.Value };
    public static object ForeignKey(AddForeignKeyOperation f) => new { f.Name, f.Table, f.Columns,
        f.PrincipalTable, f.PrincipalColumns, onDelete = f.OnDelete.ToString() };
    public static object Describe(MigrationOperation op) => new { kind = op.GetType().Name, op.IsDestructiveChange,
        details = op switch {
            CreateTableOperation t => (object)new { t.Name, columns = t.Columns.Select(Column).ToArray(), primaryKey = t.PrimaryKey?.Columns,
                foreignKeys = t.ForeignKeys.Select(ForeignKey).ToArray(), uniqueConstraints = t.UniqueConstraints.Select(u => new { u.Name, u.Columns }).ToArray() },
            AddColumnOperation c => Column(c),
            CreateIndexOperation i => new { i.Name, i.Table, i.Columns, i.IsUnique, i.Filter },
            AddForeignKeyOperation f => ForeignKey(f),
            _ => throw new InvalidOperationException("Forbidden operation")
        } };

    // Extra typed/provider assertions supplement fields omitted by 11's JSON serializer.
    public static bool MetadataSafe(MigrationOperation op)
    {
        if (op.IsDestructiveChange) return false;
        IEnumerable<ColumnOperation> columns = op switch {
            AddColumnOperation c => [c], CreateTableOperation t => t.Columns, _ => []
        };
        foreach (var c in columns)
        {
            var type = c.ColumnType;
            var expectedType = type switch {
                "char(36)" => typeof(Guid), "datetime(6)" => typeof(DateTime),
                "int unsigned" => typeof(uint), "tinyint(1)" => typeof(bool), _ => typeof(string)
            };
            if (c.ClrType != expectedType || c.IsRowVersion || c.Comment is not null || c.DefaultValueSql is not null ||
                c.Schema is not null || c.IsUnicode is not null || c.IsFixedLength is not null || c.Precision is not null || c.Scale is not null) return false;
            var match = Regex.Match(type ?? "", @"^varchar\((\d+)\)$", RegexOptions.CultureInvariant);
            int? expectedLength = match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            if (c.MaxLength != expectedLength) return false;
            if (c.ComputedColumnSql is not null)
            {
                if (c.Table != "case_guardians" || c.Name != "active_case_id" || c.IsStored != true || c.DefaultValue is not null) return false;
            }
            else if (c.IsStored is not null) return false;
            if (c.DefaultValue is not null && !(c.Table == "cases" &&
                ((c.Name is "address" or "national_id" && c.DefaultValue is string s && s == "") ||
                 (c.Name == "revision" && c.DefaultValue is uint u && u == 0)))) return false;
            if (c.GetAnnotations().Any(a => a.Name != "MySql:CharSet" || a.Value is not string)) return false;
        }
        IEnumerable<AddForeignKeyOperation> fks = op switch {
            AddForeignKeyOperation f => [f], CreateTableOperation t => t.ForeignKeys, _ => []
        };
        if (fks.Any(f => f.OnDelete != ReferentialAction.Restrict || f.OnUpdate != ReferentialAction.NoAction ||
            f.Schema is not null || f.PrincipalSchema is not null || f.GetAnnotations().Any())) return false;
        if (op is CreateIndexOperation i && (i.Schema is not null || i.IsDescending is not null || i.Filter is not null || i.GetAnnotations().Any())) return false;
        if (op is CreateTableOperation table)
        {
            if (table.Schema is not null || table.Comment is not null || table.CheckConstraints.Count != 0 || table.PrimaryKey is null ||
                table.PrimaryKey.Name != "PK_" + table.Name || table.PrimaryKey.GetAnnotations().Any() ||
                table.PrimaryKey.Schema is not null || table.UniqueConstraints.Any(u => u.Schema is not null || u.GetAnnotations().Any()) ||
                table.GetAnnotations().Any(a => a.Name switch {
                    "MySql:CharSet" => a.Value is not string charset || charset != "utf8mb4",
                    "Relational:Collation" => a.Value is not string collation || collation != "utf8mb4_unicode_ci",
                    _ => true
                })) return false;
        }
        return true;
    }
    public static bool Matches(MigrationOperation actual, JsonElement expected) => MetadataSafe(actual) &&
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(Describe(actual)), JsonNode.Parse(expected.GetRawText()));

    public static bool Exact(IReadOnlyList<MigrationOperation> ops, IReadOnlyList<JsonElement> expected)
    {
        try {
            if (ops.Count != 40 || expected.Count != 40) return false;
            var map = ops.ToDictionary(Key, StringComparer.Ordinal);
            if (!expected.All(e => map.TryGetValue(Key(e), out var op) && Matches(op, e))) return false;
            for (var n = 0; n < ops.Count; n++)
            {
                var op = ops[n];
                if (op is CreateTableOperation t && t.ForeignKeys.Any(f =>
                    ops.Select((x, i) => (x, i)).Any(x => x.x is CreateTableOperation p && p.Name == f.PrincipalTable && x.i >= n))) return false;
                if (op is CreateIndexOperation ix)
                {
                    if (ops.Select((x, i) => (x, i)).Any(x =>
                        (x.x is CreateTableOperation p && p.Name == ix.Table || x.x is AddColumnOperation c && c.Table == ix.Table && ix.Columns.Contains(c.Name)) && x.i >= n)) return false;
                }
                if (op is AddForeignKeyOperation fk && ops.Select((x, i) => (x, i)).Any(x =>
                    (x.x is CreateTableOperation p && (p.Name == fk.Table || p.Name == fk.PrincipalTable) || x.x is AddColumnOperation c && c.Table == fk.Table && fk.Columns.Contains(c.Name)) && x.i >= n)) return false;
            }
            return true;
        } catch (InvalidOperationException) { return false; }
          catch (ArgumentException) { return false; }
    }
}
