using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Services.Cases;
using MimeKit;

namespace EarlyInterventionCare.Api.Services.Email;

public sealed record ParentEmailChallenge(string ChallengeId, DateTimeOffset ExpiresAtUtc, int ResendAfterSeconds);
public sealed record VerifiedParentEmail(string Email);

public interface IParentEmailVerification
{
    Task<ParentEmailChallenge> RequestAsync(string email, CancellationToken ct = default);
    Task<VerifiedParentEmail?> VerifyAsync(string challengeId, string code, CancellationToken ct = default);
}

// Single-process challenge store. Restart invalidates challenges; no real database
// schema is created. Keep the public login endpoint disabled until identity,
// shared storage/deployment, per-IP limits and Guardian integration are complete.
public sealed class ParentEmailVerification(IParentEmailSender sender, TimeProvider clock) : IParentEmailVerification
{
    private const int Capacity = 4096;
    private readonly object gate = new();
    private readonly byte[] hashKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    // Case changes must not bypass the delivery throttle. This does not define
    // account identity normalization (which still needs a common schema contract).
    private readonly Dictionary<string, DateTimeOffset> requested = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ParentEmailChallenge> RequestAsync(string email, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var address = Normalize(email);
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Entry entry;
        lock (gate)
        {
            var now = clock.GetUtcNow();
            Prune(now);
            if (requested.TryGetValue(address, out var last) && now < last.AddSeconds(60))
                throw new CaseAccessException("OTP_RESEND_TOO_SOON", 429, "請稍候再重新取得驗證碼。");
            if (entries.Count >= Capacity || (!requested.ContainsKey(address) && requested.Count >= Capacity))
                throw Unavailable();
            // A replacement invalidates the older challenge even if it is still sending.
            foreach (var old in entries.Where(p => string.Equals(p.Value.Email, address, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray())
                entries.Remove(old);
            entry = new(address, Hash(id, code), now.AddMinutes(5));
            entries.Add(id, entry);
            requested[address] = now;
        }

        try
        {
            await sender.SendCodeAsync(address, code, 5, ct);
            lock (gate)
            {
                if (clock.GetUtcNow() >= entry.ExpiresAt || !entries.TryGetValue(id, out var current) || !ReferenceEquals(current, entry))
                    throw Unavailable();
                entry.Ready = true;
            }
            return new(id, entry.ExpiresAt, 60);
        }
        catch (Exception)
        {
            lock (gate) { entries.Remove(id); }
            // No retry: an SMTP exception can occur after delivery. Keep cooldown.
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw Unavailable();
        }
    }

    public Task<VerifiedParentEmail?> VerifyAsync(string challengeId, string code, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (challengeId is null || challengeId.Length != 64 || challengeId.Any(c => !char.IsAsciiHexDigit(c)))
            return Task.FromResult<VerifiedParentEmail?>(null);
        lock (gate)
        {
            Prune(clock.GetUtcNow());
            if (!entries.TryGetValue(challengeId, out var entry) || !entry.Ready)
                return Task.FromResult<VerifiedParentEmail?>(null);
            entry.Attempts++;
            var valid = code is not null && code.Length == 6 && code.All(c => c is >= '0' and <= '9') &&
                CryptographicOperations.FixedTimeEquals(entry.Hash, Hash(challengeId, code));
            if (valid || entry.Attempts >= 5) entries.Remove(challengeId);
            // Removal and comparison share the lock: at most one verification wins.
            return Task.FromResult(valid ? new VerifiedParentEmail(entry.Email) : null);
        }
    }

    private byte[] Hash(string challengeId, string code) =>
        HMACSHA256.HashData(hashKey, Encoding.UTF8.GetBytes(challengeId + ":" + code));

    private void Prune(DateTimeOffset now)
    {
        foreach (var id in entries.Where(p => now >= p.Value.ExpiresAt).Select(p => p.Key).ToArray()) entries.Remove(id);
        foreach (var email in requested.Where(p => now >= p.Value.AddMinutes(5)).Select(p => p.Key).ToArray()) requested.Remove(email);
    }

    private static string Normalize(string? email)
    {
        var text = email?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 254 || text.Any(char.IsControl) ||
            !MailboxAddress.TryParse(text, out var parsed) || parsed is null || !string.IsNullOrEmpty(parsed.Name) ||
            parsed.Address != text || text.LastIndexOf('@') <= 0 || text.EndsWith('@'))
            throw new CaseAccessException("INVALID_INPUT", 400, "請輸入有效的電子郵件地址。");
        var at = text.LastIndexOf('@');
        // Keep the local part; do not remove dots/+ tags or infer account ownership.
        return text[..at] + "@" + text[(at + 1)..].ToLowerInvariant();
    }

    private static CaseAccessException Unavailable() => new("EMAIL_SERVICE_UNAVAILABLE", 503, "登入服務暫時無法使用，請稍後重試。");
    private sealed class Entry(string email, byte[] hash, DateTimeOffset expiresAt)
    {
        public string Email { get; } = email;
        public byte[] Hash { get; } = hash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool Ready { get; set; }
        public int Attempts { get; set; }
    }
}
