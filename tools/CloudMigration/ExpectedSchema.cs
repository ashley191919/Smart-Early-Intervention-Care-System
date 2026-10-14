using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Earlycare.CloudMigration;

public static class ExpectedSchema
{
    public static string? Default(object? value) => value is null ? null : value is bool flag ? (flag ? "1" : "0") :
        Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    public static string Type(string value) => value.Replace(" ", "").ToLowerInvariant();
    public static SchemaShape FromModel(IModel model)
    {
        var columns = new List<ColumnShape>(); var foreignKeys = new List<string>(); var indexes = new List<string>();
        foreach (var entity in model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            foreach (var p in entity.GetProperties())
            {
                var type = Type(p.GetColumnType()!);
                var text = type.StartsWith("char") || type.StartsWith("varchar") || type.StartsWith("enum") || type.Contains("text");
                columns.Add(new(table, p.GetColumnName()!, type, p.IsNullable,
                    p.GetDefaultValueSql() ?? (p.TryGetDefaultValue(out var value) ? Default(value) : null),
                    text ? p.GetCharSet() ?? entity.GetCharSet() ?? model.GetCharSet() : null,
                    text ? p.GetCollation() ?? entity.GetCollation() ?? model.GetCollation() : null));
            }
            foreach (var fk in entity.GetForeignKeys()) foreignKeys.Add(string.Join('|', table, fk.GetConstraintName(),
                string.Join(',', fk.Properties.Select(p => p.GetColumnName())), fk.PrincipalEntityType.GetTableName(),
                string.Join(',', fk.PrincipalKey.Properties.Select(p => p.GetColumnName())), "RESTRICT", "RESTRICT"));
            foreach (var key in entity.GetKeys()) indexes.Add(string.Join('|', table, key.IsPrimaryKey() ? "PRIMARY" : key.GetName(),
                "1", string.Join(',', key.Properties.Select(p => p.GetColumnName()))));
            foreach (var index in entity.GetIndexes()) indexes.Add(string.Join('|', table, index.GetDatabaseName(),
                index.IsUnique ? "1" : "0", string.Join(',', index.Properties.Select(p => p.GetColumnName()))));
        }
        return new(columns, foreignKeys, indexes.Distinct().ToArray());
    }
}
