using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Authentication;

// Approved existing accounts only. No auto-signup or organization inference.
// Not exposed as a public login endpoint until migration and integration are ready.
public sealed class ParentEmailAuthenticationService(ApplicationDbContext db, IParentEmailVerification verification,
    JwtTokenService tokens, TimeProvider clock)
{
    // Same response regardless of account existence: do not enumerate accounts here.
    public Task<ParentEmailChallenge> RequestAsync(string email, CancellationToken ct = default)
        => verification.RequestAsync(EmailAddressIdentity.Normalize(email), ct);

    public async Task<ParentOtpLoginResponse> VerifyAsync(string challengeId, string code, CancellationToken ct = default)
    {
        var proof = await verification.VerifyAsync(challengeId, code, ct);
        if (proof == null) throw new CaseAccessException("OTP_INVALID", 401, "驗證碼不正確或已失效，請重新取得。");
        var email = EmailAddressIdentity.Normalize(proof.Email);
        var key = EmailAddressIdentity.LookupKey(email);
        var accounts = await (from user in db.Users.AsNoTracking()
            join role in db.Roles on user.RoleId equals role.Id
            join org in db.Organizations on user.OrganizationId equals org.Id
            where user.NormalizedEmail == key && role.Name == "PARENT"
            select new { user.Id, user.Username, user.Email, user.Status, OrganizationStatus = org.Status }).Take(2).ToListAsync(ct);
        // Include disabled duplicates rather than choosing an arbitrary active account.
        if (accounts.Count != 1 || accounts[0].Status != "ACTIVE" || accounts[0].OrganizationStatus != "ACTIVE" ||
            !Matches(accounts[0].Email, key))
            throw new CaseAccessException("PARENT_ACCOUNT_NOT_READY", 403, "無法完成登入，請聯絡承辦窗口確認帳號。");
        var account = accounts[0];
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.Users.Where(u => u.Id == account.Id && u.Email == account.Email && u.NormalizedEmail == key && u.Status == "ACTIVE"
            && db.Organizations.Any(o => o.Id == u.OrganizationId && o.Status == "ACTIVE")
            && db.Roles.Any(r => r.Id == u.RoleId && r.Name == "PARENT"))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAtUtc, now)
                .SetProperty(u => u.LastLoginAtUtc, now).SetProperty(u => u.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new CaseAccessException("UNAUTHENTICATED", 401, "請重新登入。");
        var token = tokens.CreateToken(account.Id, account.Username, "PARENT");
        return new(account.Id, "PARENT", token.AccessToken, token.ExpiresAt);
    }

    private static bool Matches(string? email, string key)
    {
        try { return EmailAddressIdentity.LookupKey(email) == key; }
        catch (CaseAccessException) { return false; }
    }
}
