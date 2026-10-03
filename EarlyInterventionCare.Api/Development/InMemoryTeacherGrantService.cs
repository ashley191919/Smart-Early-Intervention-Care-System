using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Services;

namespace EarlyInterventionCare.Api.Development;

// Singleton, process-local storage. Restarting the process invalidates every grant.
public sealed class InMemoryTeacherGrantService(TimeProvider clock) : ITeacherGrantService
{
    private readonly ConcurrentDictionary<string, TestTeacherGrant> grants = new();

    public CreateTeacherGrantResponse Create(int expiresInSeconds)
    {
        if (expiresInSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(expiresInSeconds));

        var now = clock.GetUtcNow();
        foreach (var entry in grants)
            if (entry.Value.ExpiresAtUtc <= now)
                grants.TryRemove(entry.Key, out _);

        while (true)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var grant = new TestTeacherGrant(Guid.NewGuid(), "dev-case-001",
                "dev-questionnaire-v1", "dev-task-001", now.AddSeconds(expiresInSeconds));
            if (grants.TryAdd(Hash(token), grant))
                return new(grant.GrantId, $"/teacher/test-form?token={token}", grant.ExpiresAtUtc);
        }
    }

    public TestTeacherGrant? Validate(string? token)
    {
        if (token is null || token.Length != 43 ||
            token.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '-' or '_')))
            return null;

        var hash = Hash(token);
        if (!grants.TryGetValue(hash, out var grant))
            return null;
        if (grant.ExpiresAtUtc <= clock.GetUtcNow())
        {
            grants.TryRemove(hash, out _);
            return null;
        }
        return grant;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
