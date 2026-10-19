using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Cases;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Services.Email;
namespace EarlyInterventionCare.Api.Services.Cases;
public sealed class CaseGuardianService(ApplicationDbContext db, CaseAccessService access, TimeProvider clock)
{
    DateTime Now => clock.GetUtcNow().UtcDateTime;
    static DateTime Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);
    public static CaseDto Dto(CaseRecord c) => new(c.CaseId, c.CaseCode, c.ChildName, c.BirthDate, c.Sex, c.NationalId, c.Address, c.CaseStatus, c.Revision, Utc(c.CreatedAtUtc), Utc(c.UpdatedAtUtc));
    static CaseAccessException Missing() => new("CASE_NOT_FOUND", 404, "找不到可存取的個案。");
    public static string NormalizePhone(string? value)
    {
        var p = (value ?? "").Trim().Replace(" ", "").Replace("-", "");
        if (p.StartsWith("+886")) p = "0" + p[4..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(p, "^09[0-9]{8}$")) throw new CaseAccessException("INVALID_PHONE", 400, "請輸入有效的台灣手機號碼。");
        return p;
    }
    static string Name(string? s) { var n = (s ?? "").Trim(); if (n.Length is < 1 or > 100) throw new CaseAccessException("INVALID_NAME", 400, "請輸入姓名。"); return n; }
    void Validate(CaseInput input)
    {
        Name(input.ChildName);
        if (!CaseFieldValidation.IsValidNationalId(input.NationalId) || string.IsNullOrWhiteSpace(input.Address) || input.Address.Trim().Length > 300) throw new CaseAccessException("INVALID_INPUT", 400, "請確認身分證字號檢查碼與必填地址。");
        var today = DateOnly.FromDateTime(Now.AddHours(8));
        if (input.BirthDate == default || input.BirthDate > today || input.BirthDate < today.AddYears(-120) || input.Sex is not ("MALE" or "FEMALE" or "UNKNOWN")) throw new CaseAccessException("INVALID_INPUT", 400, "請確認出生日期與性別。");
    }
    public async Task<CaseRecord> ReadAsync(CaseActor actor, Guid id, CancellationToken ct = default) => await access.ReadableCases(actor).SingleOrDefaultAsync(c => c.CaseId == id, ct) ?? throw Missing();
    public async Task<IReadOnlyList<CaseDto>> ListAsync(CaseActor actor, int page = 1, int size = 20, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000 || size < 1 || size > 100) throw new CaseAccessException("INVALID_PAGE", 400, "請確認頁碼。");
        var rows = await access.ReadableCases(actor).OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.CaseId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return rows.Select(Dto).ToArray();
    }
    public async Task<IReadOnlyList<ParentCaseDto>> ParentCasesAsync(CaseActor actor, int page = 1, int size = 20, CancellationToken ct = default)
    {
        CaseAccessService.Parent(actor);
        var cases = await ListAsync(actor, page, size, ct);
        var ids = cases.Select(c => c.CaseId).ToArray();
        var links = await (from link in db.CaseGuardians.AsNoTracking()
                           join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.GuardianId
                           where ids.Contains(link.CaseId) && link.LinkStatus == "ACTIVE" && link.BoundAtUtc != null && guardian.ParentUserId == actor.UserId && guardian.OrganizationId == actor.OrganizationId
                           select new { link.CaseId, link.GuardianId, link.Relationship, link.IsPrimaryContact }).ToListAsync(ct);
        return cases.Select(c => new ParentCaseDto(c.CaseId, c.CaseCode, c.ChildName, c.BirthDate, c.Sex, c.NationalId, c.Address, c.CaseStatus, c.Revision, c.CreatedAtUtc, c.UpdatedAtUtc,
            links.Where(l => l.CaseId == c.CaseId).Select(l => new ParentCaseRelationshipDto(l.GuardianId, l.Relationship, l.IsPrimaryContact)).ToArray())).ToArray();
    }
    public async Task<CaseDto> CreateAsync(CaseActor actor, CaseInput input, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); Validate(input);
        var id = Guid.NewGuid(); var c = new CaseRecord { CaseId = id, OrganizationId = actor.OrganizationId, CaseCode = "CASE-" + id.ToString("N").ToUpperInvariant(), ChildName = Name(input.ChildName), BirthDate = input.BirthDate, Sex = input.Sex, NationalId = input.NationalId, Address = input.Address.Trim(), CaseStatus = "NEW", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        db.Cases.Add(c); await db.SaveChangesAsync(ct); return Dto(c);
    }
    public async Task<CaseDto> UpdateAsync(CaseActor actor, Guid id, CaseUpdateInput input, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); Validate(input); await ReadAsync(actor, id, ct);
        var changed = await db.Cases.Where(c => c.CaseId == id && c.OrganizationId == actor.OrganizationId && c.Revision == input.Revision).ExecuteUpdateAsync(s => s.SetProperty(c => c.ChildName, Name(input.ChildName)).SetProperty(c => c.BirthDate, input.BirthDate).SetProperty(c => c.Sex, input.Sex).SetProperty(c => c.NationalId, input.NationalId).SetProperty(c => c.Address, input.Address.Trim()).SetProperty(c => c.UpdatedAtUtc, Now).SetProperty(c => c.Revision, c => c.Revision + 1), ct);
        if (changed != 1) throw new CaseAccessException("REVISION_CONFLICT", 409, "資料已更新，請重新載入再修改。");
        return Dto(await ReadAsync(actor, id, ct));
    }
    static string Email(string? value)
    {
        return EmailAddressIdentity.LookupKey(value);
    }
    static void Relationship(string value)
    {
        if (value is not ("FATHER" or "MOTHER" or "GRANDPARENT" or "RELATIVE" or "GUARDIAN" or "CAREGIVER" or "OTHER")) throw new CaseAccessException("INVALID_RELATIONSHIP", 400, "請選擇照顧者關係。");
    }
    public async Task<GuardianDto> AddGuardianAsync(CaseActor actor, Guid caseId, GuardianInput input, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct); Relationship(input.Relationship);
        var phone = NormalizePhone(input.Phone); var email = Email(input.Email); var name = Name(input.DisplayName);
        if (input.IsPrimaryContact && await db.CaseGuardians.AnyAsync(l => l.CaseId == caseId && l.LinkStatus == "ACTIVE" && l.IsPrimaryContact, ct)) throw new CaseAccessException("PRIMARY_CONTACT_EXISTS", 409, "此孩子已有主要聯絡人。");
        if (await db.Guardians.AnyAsync(g => g.OrganizationId == actor.OrganizationId && g.Phone == phone, ct)) throw new CaseAccessException("CONTACT_ALREADY_EXISTS", 409, "此手機已有聯絡資料，請選擇既有照顧者共用。");
        var g = new GuardianContact { GuardianId = Guid.NewGuid(), OrganizationId = actor.OrganizationId, DisplayName = name, Phone = phone, Email = email, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var l = new CaseGuardian { CaseId = caseId, GuardianId = g.GuardianId, Relationship = input.Relationship, IsPrimaryContact = input.IsPrimaryContact, CreatedAtUtc = Now };
        db.Guardians.Add(g); db.CaseGuardians.Add(l); await db.SaveChangesAsync(ct);
        return new(g.GuardianId, g.DisplayName, g.Phone, g.Email, l.Relationship, l.IsPrimaryContact, l.LinkStatus, g.Revision);
    }
    public async Task<IReadOnlyList<ParentProfileDto>> ContactListAsync(CaseActor actor, int page, int size, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor);
        if (page < 1 || page > 100000 || size < 1 || size > 100) throw new CaseAccessException("INVALID_PAGE", 400, "請確認頁碼。");
        return await db.Guardians.AsNoTracking().Where(g => g.OrganizationId == actor.OrganizationId).OrderBy(g => g.GuardianId).Skip((page - 1) * size).Take(size).Select(g => new ParentProfileDto(g.GuardianId, g.DisplayName, g.Phone, g.Email, g.Revision)).ToListAsync(ct);
    }
    public async Task<GuardianDto> LinkGuardianAsync(CaseActor actor, Guid caseId, Guid guardianId, GuardianLinkInput input, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct); Relationship(input.Relationship);
        var g = await db.Guardians.AsNoTracking().SingleOrDefaultAsync(g => g.GuardianId == guardianId && g.OrganizationId == actor.OrganizationId, ct) ?? throw new CaseAccessException("GUARDIAN_NOT_FOUND", 404, "找不到照顧者。");
        if (input.IsPrimaryContact && await db.CaseGuardians.AnyAsync(l => l.CaseId == caseId && l.LinkStatus == "ACTIVE" && l.IsPrimaryContact, ct)) throw new CaseAccessException("PRIMARY_CONTACT_EXISTS", 409, "此孩子已有主要聯絡人。");
        var old = await db.CaseGuardians.AsNoTracking().SingleOrDefaultAsync(l => l.CaseId == caseId && l.GuardianId == guardianId, ct);
        if (old?.LinkStatus == "ACTIVE") throw new CaseAccessException("LINK_ALREADY_EXISTS", 409, "此照顧者已與個案連結。");
        if (old == null) db.CaseGuardians.Add(new CaseGuardian { CaseId = caseId, GuardianId = guardianId, Relationship = input.Relationship, IsPrimaryContact = input.IsPrimaryContact, CreatedAtUtc = Now });
        else
        {
            if (await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId && l.LinkStatus == "REVOKED").ExecuteUpdateAsync(s => s.SetProperty(l => l.Relationship, input.Relationship).SetProperty(l => l.IsPrimaryContact, input.IsPrimaryContact).SetProperty(l => l.LinkStatus, "ACTIVE").SetProperty(l => l.RevokedAtUtc, (DateTime?)null).SetProperty(l => l.BoundAtUtc, (DateTime?)null), ct) != 1) throw new CaseAccessException("REVISION_CONFLICT", 409, "關係已更新，請重新載入。");
        }
        await db.SaveChangesAsync(ct);
        return new(g.GuardianId, g.DisplayName, g.Phone, g.Email, input.Relationship, input.IsPrimaryContact, "ACTIVE", g.Revision);
    }
    public async Task UpdateGuardianAsync(CaseActor actor, Guid caseId, Guid guardianId, GuardianUpdateInput input, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct);
        var g = await db.Guardians.AsNoTracking().SingleOrDefaultAsync(g => g.GuardianId == guardianId && g.OrganizationId == actor.OrganizationId && db.CaseGuardians.Any(l => l.CaseId == caseId && l.GuardianId == g.GuardianId && l.LinkStatus == "ACTIVE"), ct);
        if (g == null) throw new CaseAccessException("GUARDIAN_NOT_FOUND", 404, "找不到照顧者。");
        if (input.IsPrimaryContact && await db.CaseGuardians.AnyAsync(l => l.CaseId == caseId && l.GuardianId != guardianId && l.LinkStatus == "ACTIVE" && l.IsPrimaryContact, ct)) throw new CaseAccessException("PRIMARY_CONTACT_EXISTS", 409, "此孩子已有主要聯絡人。");
        var phone = NormalizePhone(input.Phone); var name = Name(input.DisplayName); var email = Email(input.Email);
        if (await db.Guardians.AnyAsync(x => x.OrganizationId == actor.OrganizationId && x.Phone == phone && x.GuardianId != guardianId, ct)) throw new CaseAccessException("CONTACT_ALREADY_EXISTS", 409, "此手機已有聯絡資料，請選擇既有照顧者共用。");
        if (email != Email(g.Email) && g.ParentUserId != null) throw new CaseAccessException("EMAIL_CHANGE_REQUIRES_REVERIFICATION", 409, "已綁定照顧者的電子郵件變更需要重新核准與驗證。");
        if (input.Relationship is not ("FATHER" or "MOTHER" or "GRANDPARENT" or "RELATIVE" or "GUARDIAN" or "CAREGIVER" or "OTHER")) throw new CaseAccessException("INVALID_RELATIONSHIP", 400, "請選擇照顧者關係。");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Guardians.Where(x => x.GuardianId == guardianId && x.Revision == input.Revision).ExecuteUpdateAsync(s => s.SetProperty(x => x.DisplayName, name).SetProperty(x => x.Phone, phone).SetProperty(x => x.Email, email).SetProperty(x => x.UpdatedAtUtc, Now).SetProperty(x => x.Revision, x => x.Revision + 1), ct) != 1) throw new CaseAccessException("REVISION_CONFLICT", 409, "資料已更新，請重新載入。");
        if (await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId && l.LinkStatus == "ACTIVE").ExecuteUpdateAsync(s => s.SetProperty(l => l.Relationship, input.Relationship).SetProperty(l => l.IsPrimaryContact, input.IsPrimaryContact), ct) != 1) throw new CaseAccessException("LINK_NOT_FOUND", 404, "找不到有效綁定。");
        if (email != Email(g.Email)) await db.ParentBindingInvitations.Where(i => i.GuardianId == guardianId && i.ConsumedAtUtc == null && i.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAtUtc, Now), ct);
        await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<GuardianDto>> GuardiansAsync(CaseActor actor, Guid caseId, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct);
        return await (from l in db.CaseGuardians.AsNoTracking() join g in db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId where l.CaseId == caseId && g.OrganizationId == actor.OrganizationId orderby g.CreatedAtUtc select new GuardianDto(g.GuardianId, g.DisplayName, g.Phone, g.Email, l.Relationship, l.IsPrimaryContact, l.LinkStatus, g.Revision)).ToListAsync(ct);
    }
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
    public async Task<(Guid InvitationId, string Code, DateTime? ExpiresAtUtc)> InviteAsync(CaseActor actor, Guid caseId, Guid guardianId, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct);
        var g = await (from l in db.CaseGuardians join c in db.Guardians on l.GuardianId equals c.GuardianId where l.CaseId == caseId && l.GuardianId == guardianId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc == null && c.OrganizationId == actor.OrganizationId select c).AsNoTracking().SingleOrDefaultAsync(ct);
        if (g == null) throw new CaseAccessException("GUARDIAN_NOT_FOUND", 404, "找不到照顧者。");
        if (await db.CaseGuardians.AnyAsync(l => l.CaseId == caseId && l.GuardianId == guardianId && l.BoundAtUtc != null, ct)) throw new CaseAccessException("ALREADY_BOUND", 409, "此個案已完成綁定。");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var i = new ParentBindingInvitation { InvitationId = Guid.NewGuid(), CaseId = caseId, GuardianId = guardianId, TargetPhone = g.Phone, TargetEmailNormalized = Email(g.Email), CodeHash = Hash(code), IssuedByUserId = actor.UserId, CreatedAtUtc = Now, ExpiresAtUtc = null };
        db.ParentBindingInvitations.Add(i); await db.SaveChangesAsync(ct); return (i.InvitationId, code, null);
    }
    public async Task RevokeInviteAsync(CaseActor actor, Guid id, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor);
        var i = await db.ParentBindingInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.InvitationId == id, ct);
        if (i == null) throw Missing(); await ReadAsync(actor, i.CaseId, ct);
        if (await db.ParentBindingInvitations.Where(x => x.InvitationId == id && x.ConsumedAtUtc == null && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, Now), ct) != 1) throw new CaseAccessException("INVITATION_UNAVAILABLE", 409, "邀請已使用或已撤銷。");
    }
    public async Task RevokeLinkAsync(CaseActor actor, Guid caseId, Guid guardianId, CancellationToken ct = default)
    {
        CaseAccessService.Medical(actor); await ReadAsync(actor, caseId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId && l.LinkStatus == "ACTIVE").ExecuteUpdateAsync(s => s.SetProperty(l => l.LinkStatus, "REVOKED").SetProperty(l => l.RevokedAtUtc, Now), ct) != 1) throw new CaseAccessException("LINK_NOT_FOUND", 404, "找不到有效綁定。");
        await db.ParentBindingInvitations.Where(i => i.CaseId == caseId && i.GuardianId == guardianId && i.ConsumedAtUtc == null && i.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAtUtc, Now), ct);
        await tx.CommitAsync(ct);
    }
    public async Task<CaseDto> BindAsync(CaseActor actor, string code, CancellationToken ct = default)
    {
        CaseAccessService.Parent(actor); var email = Email(actor.Email);
        var hash = Hash(code); var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var i = await db.ParentBindingInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.CodeHash == hash && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) && x.TargetEmailNormalized == email, ct);
        if (i == null) throw new CaseAccessException("INVITATION_UNAVAILABLE", 400, "邀請碼無法使用，請與醫護確認。");
        var valid = await (from l in db.CaseGuardians join g in db.Guardians on l.GuardianId equals g.GuardianId join c in db.Cases on l.CaseId equals c.CaseId where l.CaseId == i.CaseId && l.GuardianId == i.GuardianId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc == null && c.OrganizationId == actor.OrganizationId && g.OrganizationId == actor.OrganizationId && g.Email == email && (g.ParentUserId == null || g.ParentUserId == actor.UserId) select l).AnyAsync(ct);
        if (!valid) throw new CaseAccessException("INVITATION_UNAVAILABLE", 400, "邀請碼無法使用，請與醫護確認。");
        var consumed = await db.ParentBindingInvitations.Where(x => x.InvitationId == i.InvitationId && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            && db.Users.Any(u => u.Id == actor.UserId && u.NormalizedEmail == email && u.EmailVerifiedAtUtc != null && u.Status == "ACTIVE" && u.OrganizationId == actor.OrganizationId
                && db.Roles.Any(r => r.Id == u.RoleId && r.Name == "PARENT") && db.Organizations.Any(o => o.Id == u.OrganizationId && o.Status == "ACTIVE")))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, now).SetProperty(x => x.ConsumedByUserId, actor.UserId), ct);
        var bound = await db.Guardians.Where(g => g.GuardianId == i.GuardianId && g.Email == email && (g.ParentUserId == null || g.ParentUserId == actor.UserId)).ExecuteUpdateAsync(s => s.SetProperty(g => g.ParentUserId, actor.UserId).SetProperty(g => g.UpdatedAtUtc, now).SetProperty(g => g.Revision, g => g.Revision + 1), ct);
        var linked = await db.CaseGuardians.Where(l => l.CaseId == i.CaseId && l.GuardianId == i.GuardianId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(l => l.BoundAtUtc, now), ct);
        if (consumed != 1 || bound != 1 || linked != 1) throw new CaseAccessException("INVITATION_UNAVAILABLE", 409, "邀請碼已使用，請重新確認。");
        await tx.CommitAsync(ct); return Dto(await ReadAsync(actor, i.CaseId, ct));
    }
    public async Task<IReadOnlyList<ParentProfileDto>> ProfilesAsync(CaseActor actor, CancellationToken ct = default)
    {
        CaseAccessService.Parent(actor);
        return await db.Guardians.AsNoTracking().Where(g => g.ParentUserId == actor.UserId && g.OrganizationId == actor.OrganizationId && db.CaseGuardians.Any(l => l.GuardianId == g.GuardianId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc != null)).Select(g => new ParentProfileDto(g.GuardianId, g.DisplayName, g.Phone, g.Email, g.Revision)).ToListAsync(ct);
    }
    public async Task UpdateProfileAsync(CaseActor actor, Guid guardianId, ProfileInput input, CancellationToken ct = default)
    {
        CaseAccessService.Parent(actor);
        var name = Name(input.DisplayName);
        var own = db.Guardians.Where(g => g.GuardianId == guardianId && g.ParentUserId == actor.UserId && g.OrganizationId == actor.OrganizationId && db.CaseGuardians.Any(l => l.GuardianId == g.GuardianId && l.LinkStatus == "ACTIVE" && l.BoundAtUtc != null));
        if (!await own.AnyAsync(ct)) throw new CaseAccessException("PROFILE_NOT_FOUND", 404, "找不到可修改的資料。");
        if (await own.Where(g => g.Revision == input.Revision).ExecuteUpdateAsync(s => s.SetProperty(g => g.DisplayName, name).SetProperty(g => g.UpdatedAtUtc, Now).SetProperty(g => g.Revision, g => g.Revision + 1), ct) != 1) throw new CaseAccessException("REVISION_CONFLICT", 409, "資料已更新，請重新載入。");
    }
    public async Task<IReadOnlyList<TeacherCompletionDto>> TeacherCompletionAsync(CaseActor actor, Guid caseId, CancellationToken ct = default)
    {
        CaseAccessService.Parent(actor); await ReadAsync(actor, caseId, ct);
        return await (from t in db.QuestionnaireTasks.AsNoTracking() join q in db.Questionnaires on t.QuestionnaireId equals q.QuestionnaireId where t.CaseId == caseId && t.RespondentRole == "TEACHER" && t.TaskStatus != "CANCELLED" orderby t.AssignmentRound, t.TaskId select new TeacherCompletionDto(t.TaskId, q.Title, t.AssignmentRound, t.TaskStatus == "SUBMITTED")).ToListAsync(ct);
    }
}
