namespace Earlycare.CloudMigration;

public sealed record ColumnShape(string Table, string Name, string Type, bool Nullable,
    string? Default, string? Charset, string? Collation);
public sealed record SchemaShape(IReadOnlyList<ColumnShape> Columns,
    IReadOnlyList<string> ForeignKeys, IReadOnlyList<string> Indexes);

public static class SchemaComparison
{
    public static void RequireEqual(SchemaShape expected, SchemaShape actual)
    {
        // Exact structural fingerprints; ambiguity is a refusal, never automatic repair.
        if (!expected.Columns.OrderBy(c => c.Table).ThenBy(c => c.Name).SequenceEqual(
                actual.Columns.OrderBy(c => c.Table).ThenBy(c => c.Name)) ||
            !expected.ForeignKeys.Order().SequenceEqual(actual.ForeignKeys.Order()) ||
            !expected.Indexes.Order().SequenceEqual(actual.Indexes.Order()))
            throw new DeploymentException("SCHEMA_DRIFT");
    }
}
