using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class ParentAuthorSchemaProposal
{
    // Exact, explicit proposal mode only. Default deployment checks must still reject drift.
    public static bool IsExact(ApplicationDbContext db)
    {
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var oldModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var newModel = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(oldModel.GetRelationalModel(), newModel.GetRelationalModel());
        if (differences.Count != 6) return false;
        var columns = differences.OfType<AddColumnOperation>().ToArray();
        var fks = differences.OfType<AddForeignKeyOperation>().ToArray();
        var indexes = differences.OfType<CreateIndexOperation>().ToArray();
        return columns.Length == 2 && columns.All(c => c.Name == "parent_author_user_id" && c.IsNullable && c.ColumnType == "char(36)") &&
            columns.Select(c => c.Table).Order().SequenceEqual(new[] { "questionnaire_drafts", "questionnaire_responses" }) &&
            fks.Length == 2 && fks.All(f => f.PrincipalTable == "users" && f.PrincipalColumns is not null && f.PrincipalColumns.SequenceEqual(new[] { "user_id" }) &&
                f.Columns.SequenceEqual(new[] { "parent_author_user_id" }) && f.OnDelete == ReferentialAction.Restrict) &&
            indexes.Length == 2 && indexes.Any(i => i.Name == "ix_drafts_parent_author" && !i.IsUnique) &&
            indexes.Any(i => i.Name == "uq_responses_parent_key" && i.IsUnique && i.Columns.SequenceEqual(new[] { "parent_author_user_id", "idempotency_key" }));
    }
}
