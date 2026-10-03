using System.Security.Cryptography;
using System.Text;
using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Services;

namespace EarlyInterventionCare.Api.Development;

// One immutable entry owns both grant and response; all access uses the same lock.
public sealed class InMemoryTeacherGrantService(TimeProvider clock, IAuditLogService audit) : ITeacherGrantService
{
    private sealed record Entry(TestTeacherGrant Grant, TeacherResponse? Response = null);
    private readonly Dictionary<string, Entry> grants = new();
    private readonly object gate = new();

    public CreateTeacherGrantResponse Create(int expiresInSeconds, string? requestCorrelationId = null)
    {
        if (expiresInSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(expiresInSeconds));
        lock (gate)
        {
            var now = clock.GetUtcNow();
            while (true)
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var grant = new TestTeacherGrant(Guid.NewGuid(), "dev-case-001",
                    "dev-questionnaire-v1", "dev-task-001", now.AddSeconds(expiresInSeconds));
                if (grants.TryAdd(Hash(token), new Entry(grant)))
                {
                    Record(grant, "DevelopmentTestOperator", "development-test", "TeacherGrant.Create", "Success", requestCorrelationId);
                    return new(grant.GrantId, $"/teacher/test-form?token={token}", grant.ExpiresAtUtc);
                }
            }
        }
    }

    public TestTeacherGrant? Validate(string? token)
    {
        lock (gate) return ActiveEntry(token)?.Grant;
    }

    public SubmissionResult Submit(string? token, string? question1, string? question2, string? requestCorrelationId = null)
    {
        lock (gate)
        {
            try
            {
                var entry = KnownEntry(token);
                if (entry is null) return SubmissionResult.InvalidGrant;
                var rejected = entry.Grant.Status != "ACTIVE" ? entry.Grant.Status
                    : entry.Grant.ExpiresAtUtc <= clock.GetUtcNow() ? "EXPIRED" : null;
                if (rejected != null)
                {
                    Record(entry.Grant, "TeacherGrantBearer", entry.Grant.GrantId.ToString(),
                        "TeacherResponse.Submit", "Rejected:" + rejected, requestCorrelationId);
                    return SubmissionResult.InvalidGrant;
                }
                if (!Legal(question1) || !Legal(question2)) return SubmissionResult.InvalidAnswers;
                var grant = entry.Grant;
                var response = new TeacherResponse(Guid.NewGuid(), grant.GrantId, grant.CaseId,
                    grant.TaskId, grant.TaskVersionId, grant.QuestionnaireVersionId,
                    question1!, question2!, clock.GetUtcNow());
                if (response.SubmittedAtUtc >= grant.ExpiresAtUtc)
                {
                    Record(grant, "TeacherGrantBearer", grant.GrantId.ToString(),
                        "TeacherResponse.Submit", "Rejected:EXPIRED", requestCorrelationId);
                    return SubmissionResult.InvalidGrant;
                }
                // Finish all potentially failing preparation before replacing the existing entry.
                var saved = new Entry(grant with { Status = "USED" }, response);
                grants[Hash(token!)] = saved;
                Record(grant, "TeacherGrantBearer", grant.GrantId.ToString(), "TeacherResponse.Submit", "Success", requestCorrelationId);
                return SubmissionResult.Success;
            }
            catch (Exception)
            {
                // Never log bearer tokens or answers. Failure leaves the original entry intact.
                return SubmissionResult.SaveFailed;
            }
        }
    }

    public RevokeTeacherGrantResponse? Revoke(Guid grantId, string? requestCorrelationId = null)
    {
        lock (gate)
        {
            foreach (var pair in grants)
            {
                var grant = pair.Value.Grant;
                if (grant.GrantId != grantId) continue;
                // Terminal states take precedence over expiration; preserve first revocation time.
                if (grant.Status is "REVOKED" or "USED")
                    return new(grantId, grant.Status, grant.RevokedAtUtc);
                var now = clock.GetUtcNow();
                if (grant.ExpiresAtUtc <= now)
                    return new(grantId, "EXPIRED", null);
                var revoked = grant with { Status = "REVOKED", RevokedAtUtc = now };
                grants[pair.Key] = pair.Value with { Grant = revoked };
                Record(revoked, "DevelopmentTestOperator", "development-test", "TeacherGrant.Revoke", "Success", requestCorrelationId);
                return new(grantId, revoked.Status, revoked.RevokedAtUtc);
            }
            return null;
        }
    }

    // Read-only in-process verification snapshot; no HTTP inspection endpoint.
    internal TeacherResponse[] Responses
    {
        get { lock (gate) return grants.Values.Where(e => e.Response != null).Select(e => e.Response!).ToArray(); }
    }

    private Entry? ActiveEntry(string? token)
    {
        var entry = KnownEntry(token);
        return entry != null && entry.Grant.Status == "ACTIVE"
            && entry.Grant.ExpiresAtUtc > clock.GetUtcNow() ? entry : null;
    }

    private Entry? KnownEntry(string? token)
    {
        if (token is null || token.Length != 43 ||
            token.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '-' or '_'))) return null;
        return grants.TryGetValue(Hash(token), out var entry) ? entry : null;
    }

    private void Record(TestTeacherGrant grant, string actorType, string actorId,
        string action, string result, string? correlationId)
    {
        try
        {
            audit.TryWrite(new(actorType, actorId, action, "TeacherGrant", grant.GrantId.ToString(),
                result, correlationId ?? Guid.NewGuid().ToString()));
        }
        catch (Exception) { /* Audit failure must never change a completed business operation. */ }
    }

    private static bool Legal(string? answer) => answer is "yes" or "sometimes" or "no";
    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
