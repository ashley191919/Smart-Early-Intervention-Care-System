using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EarlyInterventionCare.Api.ParentModule;

public sealed class ParentModuleOptions
{
    public bool ConsentEnabled { get; set; } // False until approved scope/text AND migration are ready.
    public Guid DocumentVersionId { get; set; }
    public string ScopeCode { get; set; } = "UNCONFIRMED";
}
public sealed record ConsentDocumentResponse(Guid DocumentVersionId, string Version, string Title,
    string Content, string ContentHash, string ScopeCode);
public sealed record ConsentTargetResponse(Guid? CaseId, Guid? ConsentId, string DataUseDecision, string TeacherParticipation);
public sealed record ConsentContextResponse(ConsentDocumentResponse Document, IReadOnlyList<ConsentTargetResponse> Targets);
public sealed record RecordParentConsentRequest(Guid DocumentVersionId, string ContentHash, Guid? CaseId,
    string DataUseDecision, string TeacherParticipation, Guid RequestId);
public sealed record ParentConsentReceipt(Guid ConsentId, Guid DocumentVersionId, Guid? CaseId,
    string DataUseDecision, string TeacherParticipation, DateTime RecordedAtUtc, bool Replayed);

public interface IParentActorProvider
{
    Task<CaseActor> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
public sealed class ParentActorProvider(CaseAccessService access) : IParentActorProvider
{
    public async Task<CaseActor> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (!principal.Identities.Any(x => x.IsAuthenticated)) throw Error("UNAUTHENTICATED", 401, "請重新登入。");
        var actor = await access.ActorAsync(principal, ct);
        CaseAccessService.Parent(actor); return actor;
    }
    internal static CaseAccessException Error(string code, int status, string message) => new(code, status, message);
}

public sealed class ParentConsentService(ApplicationDbContext db, IParentActorProvider actors,
    CaseAccessService access, IOptions<ParentModuleOptions> settings, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static CaseAccessException Error(string code, int status, string message) => ParentActorProvider.Error(code, status, message);
    static string Key(Guid? caseId) => caseId?.ToString("N") ?? "ACCOUNT";
    public async Task<ConsentDocumentVersion> DocumentAsync(CancellationToken ct = default)
    {
        var o = settings.Value;
        if (!o.ConsentEnabled || o.DocumentVersionId == Guid.Empty || o.ScopeCode is not ("ACCOUNT" or "CASE"))
            throw Error("CONSENT_NOT_CONFIGURED", 503, "同意文件暫時無法提供，請聯絡醫療團隊。");
        var doc = await db.Set<ConsentDocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.DocumentVersionId == o.DocumentVersionId, ct);
        if (doc is null || doc.ApprovalStatus != "APPROVED" || doc.ScopeCode != o.ScopeCode ||
            doc.ApprovedAtUtc is null || doc.ApprovedAtUtc > Now || doc.EffectiveFromUtc is null || doc.EffectiveFromUtc > Now ||
            doc.RetiredAtUtc != null || string.IsNullOrWhiteSpace(doc.ApprovalReference) ||
            string.IsNullOrWhiteSpace(doc.Title) || string.IsNullOrWhiteSpace(doc.Version) ||
            string.IsNullOrWhiteSpace(doc.Content) || doc.Content.Length > 50000 || doc.ContentHash != Hash(doc.Content))
            throw Error("CONSENT_NOT_CONFIGURED", 503, "同意文件暫時無法提供，請聯絡醫療團隊。");
        return doc;
    }
    async Task CheckCase(CaseActor actor, Guid caseId, CancellationToken ct)
    {
        if (!await access.ReadableCases(actor).AnyAsync(x => x.CaseId == caseId, ct))
            throw Error("CASE_NOT_FOUND", 404, "找不到資料，或目前已無法存取。");
    }
    public async Task<ConsentContextResponse> ContextAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var doc = await DocumentAsync(ct); var actor = await actors.ResolveAsync(principal, ct);
        // Bootstrap discloses no child names, birthdates, addresses, profiles or answers.
        var ids = doc.ScopeCode == "ACCOUNT" ? new Guid?[] { null } :
            (await access.ReadableCases(actor).OrderBy(x => x.CaseId).Select(x => (Guid?)x.CaseId).ToListAsync(ct)).ToArray();
        var records = await db.Set<ParentConsentRecord>().AsNoTracking().Where(x => x.ActorUserId == actor.UserId &&
            x.OrganizationId == actor.OrganizationId && x.DocumentVersionId == doc.DocumentVersionId && x.RevokedAtUtc == null && x.DocumentContentHash == doc.ContentHash).ToListAsync(ct);
        return new(new(doc.DocumentVersionId, doc.Version, doc.Title, doc.Content, doc.ContentHash, doc.ScopeCode),
            ids.Select(id => { var r = records.SingleOrDefault(x => x.ScopeKey == Key(id));
                return new ConsentTargetResponse(id, r?.ConsentId, r?.DataUseDecision ?? "UNDECIDED", r?.TeacherParticipation ?? "UNDECIDED"); }).ToArray());
    }
    public async Task<ParentConsentReceipt> RecordAsync(ClaimsPrincipal principal, RecordParentConsentRequest input, CancellationToken ct = default)
    {
        var doc = await DocumentAsync(ct); var actor = await actors.ResolveAsync(principal, ct);
        if (input.RequestId == Guid.Empty || input.DataUseDecision is not ("AGREED" or "DECLINED") ||
            input.TeacherParticipation is not ("UNDECIDED" or "AGREED" or "DECLINED"))
            throw Error("INVALID_INPUT", 400, "請確認同意選項。");
        if (input.DocumentVersionId != doc.DocumentVersionId || input.ContentHash != doc.ContentHash)
            throw Error("CONSENT_VERSION_CHANGED", 409, "同意文件已更新，請重新閱讀。");
        if ((doc.ScopeCode == "ACCOUNT" && input.CaseId != null) || (doc.ScopeCode == "CASE" && (input.CaseId == null || input.CaseId == Guid.Empty)))
            throw Error("CONSENT_SCOPE_REQUIRED", 400, "請重新取得適用的同意文件。");
        if (input.CaseId is Guid id) await CheckCase(actor, id, ct);
        var scope = Key(input.CaseId);
        var existing = await db.Set<ParentConsentRecord>().AsNoTracking().Where(x => x.ActorUserId == actor.UserId &&
            (x.RequestId == input.RequestId || (x.OrganizationId == actor.OrganizationId && x.DocumentVersionId == doc.DocumentVersionId && x.ScopeKey == scope))).ToListAsync(ct);
        foreach (var r in existing)
        {
            if (r.OrganizationId != actor.OrganizationId || r.DocumentVersionId != doc.DocumentVersionId || r.ScopeKey != scope ||
                r.DocumentContentHash != doc.ContentHash || r.DataUseDecision != input.DataUseDecision || r.TeacherParticipation != input.TeacherParticipation || r.RevokedAtUtc != null)
                throw Error("CONSENT_CONFLICT", 409, "已有不同的同意紀錄，請聯絡醫療團隊確認。");
        }
        if (existing.Count > 0) return Receipt(existing[0], true);
        var record = new ParentConsentRecord { ConsentId = Guid.NewGuid(), DocumentVersionId = doc.DocumentVersionId,
            ActorUserId = actor.UserId, OrganizationId = actor.OrganizationId, CaseId = input.CaseId, ScopeKey = scope,
            DocumentContentHash = doc.ContentHash, DataUseDecision = input.DataUseDecision, TeacherParticipation = input.TeacherParticipation,
            RequestId = input.RequestId, RecordedAtUtc = Now };
        db.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            var raced = await db.Set<ParentConsentRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ActorUserId == actor.UserId &&
                x.OrganizationId == actor.OrganizationId && x.DocumentVersionId == doc.DocumentVersionId && x.ScopeKey == scope, ct);
            if (raced == null) throw;
            if (raced.DocumentContentHash != doc.ContentHash || raced.DataUseDecision != input.DataUseDecision ||
                raced.TeacherParticipation != input.TeacherParticipation || raced.RevokedAtUtc != null)
                throw Error("CONSENT_CONFLICT", 409, "已有不同的同意紀錄，請聯絡醫療團隊確認。");
            return Receipt(raced, true);
        }
        return Receipt(record, false);
    }
    static ParentConsentReceipt Receipt(ParentConsentRecord r, bool replayed) => new(r.ConsentId, r.DocumentVersionId,
        r.CaseId, r.DataUseDecision, r.TeacherParticipation, r.RecordedAtUtc, replayed);
    public async Task<ParentConsentRecord> RequireAsync(CaseActor actor, Guid? caseId, CancellationToken ct = default)
    {
        var doc = await DocumentAsync(ct);
        if (caseId is Guid id) await CheckCase(actor, id, ct);
        if (doc.ScopeCode == "CASE" && caseId == null) throw Error("CONSENT_SCOPE_REQUIRED", 403, "請先確認適用的同意範圍。");
        var scope = doc.ScopeCode == "ACCOUNT" ? "ACCOUNT" : Key(caseId);
        var r = await db.Set<ParentConsentRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ActorUserId == actor.UserId &&
            x.OrganizationId == actor.OrganizationId && x.DocumentVersionId == doc.DocumentVersionId && x.ScopeKey == scope &&
            x.DocumentContentHash == doc.ContentHash && x.RevokedAtUtc == null && x.DataUseDecision == "AGREED", ct);
        return r ?? throw Error("CONSENT_REQUIRED", 403, "請先閱讀並確認資料使用同意。");
    }
    public async Task RequireAllAsync(CaseActor actor, CancellationToken ct = default)
    {
        var doc = await DocumentAsync(ct);
        if (doc.ScopeCode == "ACCOUNT") { await RequireAsync(actor, null, ct); return; }
        var ids = await access.ReadableCases(actor).Select(x => x.CaseId).ToArrayAsync(ct);
        if (ids.Length == 0) throw Error("CONSENT_SCOPE_REQUIRED", 403, "請聯絡醫療團隊確認同意範圍。");
        foreach (var id in ids) await RequireAsync(actor, id, ct);
    }
}
