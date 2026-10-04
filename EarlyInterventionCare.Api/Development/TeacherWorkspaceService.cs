using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Services;

namespace EarlyInterventionCare.Api.Development;

public sealed record WorkspaceQuestionnaire(string Id, string VersionId, string Title,
    string Desc, string[] Options, string Instructions, string Source, string Color,
    Dictionary<int, string> Groups, string[] Questions);
public sealed record WorkspacePatient(string CaseId, string DisplayName, string Sex, int Age);
public sealed record WorkspaceTask(Guid GrantId, string TaskId, string GrantStatus,
    WorkspacePatient Patient, WorkspaceQuestionnaire[] Questionnaires, string Notice);
public sealed record WorkspaceGrant(Guid GrantId, string AuthorizationCode,
    string TaskId, string QuestionnaireVersionId, string Notice);

// Independent synthetic SNAP task. Never shares the old two-question test grant.
// No consent verification, database persistence, drafts or submissions in this stage.
public sealed class TeacherWorkspaceService
{
    private sealed record Entry(Guid Id, string Status = "ACTIVE");
    private readonly Dictionary<string, Entry> codes = new();
    private readonly Dictionary<string, Guid> sessions = new();
    private readonly object gate = new();
    private readonly WorkspaceQuestionnaire questionnaire;
    private readonly IAuditLogService audit;
    public const string Notice = "開發測試：虛構個案，未驗證家長同意；授權與會話僅存於記憶體，重啟後失效。答案尚未保存或提交。";

    public TeacherWorkspaceService(IWebHostEnvironment environment, IAuditLogService audit)
    {
        this.audit = audit;
        var path = Path.Combine(environment.ContentRootPath, "Development", "SnapQuestionnaire.json");
        questionnaire = JsonSerializer.Deserialize<WorkspaceQuestionnaire>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (questionnaire is null || questionnaire.Questions.Length != 26 || questionnaire.Options.Length != 4)
            throw new InvalidOperationException("Invalid development questionnaire fixture.");
    }

    public WorkspaceGrant Create(string correlationId)
    {
        lock (gate)
        {
            if (codes.Count >= 1000) throw new InvalidOperationException("Development grant capacity reached.");
            string raw;
            do { raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)); } while (codes.ContainsKey(Hash(raw)));
            var entry = new Entry(Guid.NewGuid());
            codes.Add(Hash(raw), entry);
            Record(entry.Id, "TeacherWorkspace.Create", "Success", correlationId);
            return new(entry.Id, string.Join("-", Enumerable.Range(0, 4).Select(i => raw.Substring(i * 4, 4))),
                "dev-snap-task-001", questionnaire.VersionId, Notice);
        }
    }

    public string? Verify(string? code, string correlationId)
    {
        var normalized = code?.Trim().Replace("-", "").ToUpperInvariant();
        if (normalized is null || normalized.Length != 16 || !normalized.All(Uri.IsHexDigit)) return null;
        lock (gate)
        {
            if (!codes.TryGetValue(Hash(normalized), out var entry) || entry.Status != "ACTIVE") return null;
            // Replace old sessions for this grant; session tokens are never returned in JSON.
            foreach (var key in sessions.Where(x => x.Value == entry.Id).Select(x => x.Key).ToArray()) sessions.Remove(key);
            var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions.Add(Hash(session), entry.Id);
            Record(entry.Id, "TeacherWorkspace.Verify", "Success", correlationId);
            return session;
        }
    }

    public WorkspaceTask? GetTask(string? session)
    {
        if (session is null || session.Length != 64 || !session.All(Uri.IsHexDigit)) return null;
        lock (gate)
        {
            if (!sessions.TryGetValue(Hash(session), out var id)) return null;
            var entry = codes.Values.FirstOrDefault(x => x.Id == id);
            if (entry is null || entry.Status != "ACTIVE") return null;
            return new(id, "dev-snap-task-001", entry.Status,
                new("DEV-SNAP-001", "林○安", "男", 4), [questionnaire], Notice);
        }
    }

    public bool Revoke(Guid id, string correlationId)
    {
        lock (gate)
        {
            var pair = codes.FirstOrDefault(x => x.Value.Id == id);
            if (pair.Key is null) return false;
            if (pair.Value.Status != "REVOKED")
            {
                codes[pair.Key] = pair.Value with { Status = "REVOKED" };
                foreach (var key in sessions.Where(x => x.Value == id).Select(x => x.Key).ToArray()) sessions.Remove(key);
                Record(id, "TeacherWorkspace.Revoke", "Success", correlationId);
            }
            return true;
        }
    }

    public void Logout(string? session)
    {
        if (session is null) return;
        lock (gate) sessions.Remove(Hash(session));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private void Record(Guid id, string action, string result, string correlationId)
    {
        try { audit.TryWrite(new("DevelopmentTestOperator", "development-test", action,
            "TeacherWorkspaceGrant", id.ToString(), result, correlationId)); }
        catch { /* Audit failure does not change a completed state transition. */ }
    }
}
