using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services;

// A separate scope prevents an audit failure from affecting the caller's committed transaction.
public sealed class MySqlAuditLogService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<MySqlAuditLogService> logger) : IAuditLogService
{
    public bool TryWrite(AuditLogRequest request)
    {
        try
        {
            // Do not accept free text, credentials, URLs or answer content as metadata.
            if (request.ResourceType is not ("TeacherGrant" or "TeacherWorkspaceGrant") ||
                !(Guid.TryParse(request.ResourceId, out _) || request.ResourceId == "unknown") ||
                request.ActorType is not ("DevelopmentTestOperator" or "TeacherGrantBearer" or "Anonymous") ||
                !(Guid.TryParse(request.ActorId, out _) || request.ActorId is "development-test" or "unknown") ||
                request.Result is not ("Success" or "Denied" or "Rejected:USED" or "Rejected:REVOKED") ||
                !AllowedActions.Contains(request.Action) ||
                request.RequestCorrelationId.Length is < 1 or > 128 ||
                request.RequestCorrelationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not (':' or '-' or '_')))
                return false;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(new AuditRecord { EventId = Guid.NewGuid(), OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
                ActorType = request.ActorType, ActorId = request.ActorId, Action = request.Action,
                ResourceType = request.ResourceType, ResourceId = request.ResourceId,
                Result = request.Result, RequestCorrelationId = request.RequestCorrelationId });
            db.SaveChanges();
            return true;
        }
        catch (Exception)
        {
            logger.LogWarning("AUDIT_WRITE_UNAVAILABLE: operation committed but its audit event was not saved.");
            return false;
        }
    }

    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    {
        "TeacherWorkspace.Create", "TeacherWorkspace.Replace", "TeacherWorkspace.Verify",
        "TeacherWorkspace.Revoke", "TeacherWorkspace.Logout", "TeacherWorkspace.VerifyDenied",
        "TeacherWorkspace.Submit",
        "TeacherGrant.Create", "TeacherGrant.Verify", "TeacherGrant.Revoke", "TeacherResponse.Submit", "TeacherResponse.SubmitDenied"
    };

    public IReadOnlyList<AuditLogEvent> Query(string? resourceType = null, string? resourceId = null, int limit = 50)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.AuditLogs.AsNoTracking()
            .Where(e => (resourceType == null || e.ResourceType == resourceType) && (resourceId == null || e.ResourceId == resourceId))
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.EventId).Take(limit).AsEnumerable()
            .Select(e => new AuditLogEvent(e.EventId, new DateTimeOffset(DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc)),
                e.ActorType, e.ActorId, e.Action, e.ResourceType, e.ResourceId, e.Result, e.RequestCorrelationId)).ToArray();
    }
}
