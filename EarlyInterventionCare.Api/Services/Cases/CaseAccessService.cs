using System.Security.Claims;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
namespace EarlyInterventionCare.Api.Services.Cases;
public sealed class CaseAccessException(string code, int status, string message) : Exception(message)
{ public string Code { get; } = code; public int Status { get; } = status; }
public sealed record CaseActor(Guid UserId, Guid OrganizationId, string Role, string? Phone, bool PhoneVerified)
{ public bool IsMedical => Role is "ADMIN" or "CASE_MANAGER" or "MEDICAL_STAFF"; }
public sealed class CaseAccessService(ApplicationDbContext db)
{
    public async Task<CaseActor> ActorAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var ids = principal.FindAll("userId").ToArray();
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var id) || id == Guid.Empty) throw new CaseAccessException("UNAUTHENTICATED", 401, "請重新登入。");
        var row = await (from u in db.Users.AsNoTracking() join r in db.Roles on u.RoleId equals r.Id join o in db.Organizations on u.OrganizationId equals o.Id where u.Id == id && u.Status == "ACTIVE" && o.Status == "ACTIVE" select new { u, r.Name }).SingleOrDefaultAsync(ct);
        if (row == null) throw new CaseAccessException("UNAUTHENTICATED", 401, "請重新登入。");
        return new(id, row.u.OrganizationId, row.Name, row.u.Phone, row.u.PhoneVerifiedAtUtc != null);
    }
    public IQueryable<CaseRecord> ReadableCases(CaseActor actor)
    {
        var cases = db.Cases.AsNoTracking().Where(c => c.OrganizationId == actor.OrganizationId);
        if (actor.IsMedical) return cases;
        if (actor.Role != "PARENT" || !actor.PhoneVerified) return cases.Where(c => false);
        return cases.Where(c => db.CaseGuardians.Any(l => l.CaseId == c.CaseId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc != null && db.Guardians.Any(g => g.GuardianId == l.GuardianId && g.OrganizationId == actor.OrganizationId && g.ParentUserId == actor.UserId)));
    }
    // Shared with 16: query current DB account/link, never trust role/userId supplied by clients.
    public async Task<bool> CanReadAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct = default)
        => await ReadableCases(await ActorAsync(principal, ct)).AnyAsync(c => c.CaseId == caseId, ct);
    // Task read scope only. Response/Draft APIs must separately verify the saved author's userId.
    public async Task<bool> CanReadParentTaskAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(principal, ct);
        if (actor.Role != "PARENT" || !actor.PhoneVerified) return false;
        return await db.QuestionnaireTasks.AnyAsync(t => t.TaskId == taskId && t.RespondentRole == "PARENT" && t.TaskStatus != "CANCELLED" && ReadableCases(actor).Any(c => c.CaseId == t.CaseId), ct);
    }
    public async Task<bool> CanWriteParentTaskAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(principal, ct);
        if (actor.Role != "PARENT" || !actor.PhoneVerified) return false;
        return await db.QuestionnaireTasks.AnyAsync(t => t.TaskId == taskId && t.RespondentRole == "PARENT" && (t.TaskStatus == "PENDING" || t.TaskStatus == "IN_PROGRESS") && ReadableCases(actor).Any(c => c.CaseId == t.CaseId), ct);
    }
    public static void Medical(CaseActor actor) { if (!actor.IsMedical) throw new CaseAccessException("FORBIDDEN", 403, "無法執行此操作。"); }
    public static void Parent(CaseActor actor) { if (actor.Role != "PARENT" || !actor.PhoneVerified) throw new CaseAccessException("PHONE_VERIFICATION_REQUIRED", 403, "請先完成家長手機驗證。"); }
}
