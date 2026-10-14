using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Authentication;

public sealed record ParentOtpLoginResponse(Guid UserId, string Role, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class ParentOtpAuthenticationService(ApplicationDbContext db, IParentSmsVerification sms,
    JwtTokenService tokens, TimeProvider clock)
{
    public Task<ParentOtpChallenge> RequestAsync(string phone, CancellationToken ct = default)
        => sms.RequestAsync(CaseGuardianService.NormalizePhone(phone), ct);

    public async Task<ParentOtpLoginResponse> VerifyAsync(string challengeId, string code, CancellationToken ct = default)
    {
        var verified = await sms.VerifyAsync(challengeId, code, ct);
        if (verified is null) throw new CaseAccessException("OTP_INVALID", 401, "驗證碼不正確或已失效，請重新取得。");
        var phone = CaseGuardianService.NormalizePhone(verified.Phone);

        // Existing approved parent accounts only. First-account creation/organization selection
        // is intentionally not inferred from an arbitrary client organizationId or invite text.
        var accounts = await (from user in db.Users.AsNoTracking()
            join role in db.Roles on user.RoleId equals role.Id
            join org in db.Organizations on user.OrganizationId equals org.Id
            where user.Phone == phone && role.Name == "PARENT"
            select new { user.Id, user.Username, user.Status, OrganizationStatus = org.Status }).Take(2).ToListAsync(ct);
        if (accounts.Count != 1 || accounts[0].Status != "ACTIVE" || accounts[0].OrganizationStatus != "ACTIVE")
            throw new CaseAccessException("PARENT_ACCOUNT_NOT_READY", 403, "無法完成登入，請聯絡承辦窗口確認帳號。");

        var account = accounts[0];
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.Users.Where(u => u.Id == account.Id && u.Phone == phone && u.Status == "ACTIVE"
            && db.Organizations.Any(o => o.Id == u.OrganizationId && o.Status == "ACTIVE")
            && db.Roles.Any(r => r.Id == u.RoleId && r.Name == "PARENT"))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PhoneVerifiedAtUtc, now)
                .SetProperty(u => u.LastLoginAtUtc, now).SetProperty(u => u.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new CaseAccessException("UNAUTHENTICATED", 401, "請重新登入。");
        var token = tokens.CreateToken(account.Id, account.Username, "PARENT");
        return new(account.Id, "PARENT", token.AccessToken, token.ExpiresAt);
    }
}
