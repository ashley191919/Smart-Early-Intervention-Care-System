using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

// Format/shape checks only. No Bridge, HTTP host, EF, authorization, or fake engine.
var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Expected value/shape mismatch");
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {e.Message}");
    }
}
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var version = Guid.Parse("11111111-1111-4111-8111-111111111111");
var task = Guid.Parse("22222222-2222-4222-8222-222222222222");
var utc = new DateTime(2026, 10, 30, 1, 2, 3, DateTimeKind.Utc);
var answers = new[] { new QuestionnaireAnswer("SYNTHETIC_Q1", "0") };
var request = new SaveQuestionnaireDraftRequest(version, 9007199254740993UL, null, "2026-10-30", answers, null);
using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, options));
var root = json.RootElement;
Check("core GUID serializes as exact string", () =>
    root.GetProperty("questionnaireVersionId").ValueKind == JsonValueKind.String &&
    root.GetProperty("questionnaireVersionId").GetString() == version.ToString("D"));
Check("core revision remains UInt64 JSON number (not HTTP contract)", () =>
    root.GetProperty("expectedRevision").ValueKind == JsonValueKind.Number &&
    root.GetProperty("expectedRevision").GetUInt64() == 9007199254740993UL);
Check("nullable metadata remains JSON null", () =>
    root.GetProperty("respondentName").ValueKind == JsonValueKind.Null &&
    root.GetProperty("observation").ValueKind == JsonValueKind.Null);
Check("answer values are strings", () =>
    root.GetProperty("answers")[0].GetProperty("questionId").GetString() == "SYNTHETIC_Q1" &&
    root.GetProperty("answers")[0].GetProperty("optionValue").ValueKind == JsonValueKind.String &&
    root.GetProperty("answers")[0].GetProperty("optionValue").GetString() == "0");
var clone = JsonSerializer.Deserialize<SaveQuestionnaireDraftRequest>(root.GetRawText(), options)!;
Check("core request roundtrip preserves fields", () => clone.QuestionnaireVersionId == version &&
    clone.ExpectedRevision == request.ExpectedRevision && clone.FilledOn == "2026-10-30" &&
    clone.Answers is { Length: 1 } && clone.Answers[0] == answers[0]);

// Hand-authored synthetic scalar fixture reflecting 11's DTO field types at e2affb5.
// Not a compiled 11 Controller/DTO test and not a proposed Bridge implementation.
using var http = JsonDocument.Parse("""
{"questionnaireVersionId":"11111111-1111-4111-8111-111111111111","expectedRevision":"9007199254740993","filledOn":"2026-10-30"}
""");
Check("11-shaped scalar fixture GUID matches core", () =>
    http.RootElement.GetProperty("questionnaireVersionId").GetGuid() == version);
Check("11-shaped revision string parses losslessly to core value", () =>
    http.RootElement.GetProperty("expectedRevision").ValueKind == JsonValueKind.String &&
    RevisionStringConverter.TryParse(http.RootElement.GetProperty("expectedRevision").GetString(), out var n) &&
    n == request.ExpectedRevision);
var revisionJson = JsonSerializer.SerializeToElement(RevisionStringConverter.Format(ulong.MaxValue), options);
Check("formatted output is a JSON string without precision loss", () =>
    revisionJson.ValueKind == JsonValueKind.String && revisionJson.GetString() == "18446744073709551615");
Check("date-only fixture parses exactly", () =>
    DateOnly.TryParseExact(http.RootElement.GetProperty("filledOn").GetString(), "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date == new DateOnly(2026, 10, 30));
Check("date-only lexical examples reject time/invalid calendar", () =>
    !DateOnly.TryParseExact("2026-10-30T00:00:00Z", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
    !DateOnly.TryParseExact("2026-02-30", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));

var revisionProperty = typeof(SaveQuestionnaireDraftRequest).GetProperty("ExpectedRevision")!;
Check("core expected revision is nullable UInt64 with Required attribute", () =>
    revisionProperty.PropertyType == typeof(ulong?) && revisionProperty.GetCustomAttribute<RequiredAttribute>() != null);
var validation = new List<ValidationResult>();
var missingRevision = request with { ExpectedRevision = null };
Check("explicit DataAnnotations validation rejects missing revision", () =>
    !Validator.TryValidateObject(missingRevision, new ValidationContext(missingRevision), validation, true) &&
    validation.Any(result => result.MemberNames.Contains("ExpectedRevision")));
Check("core submit has no ExpectedRevision or relationship field", () =>
    typeof(QuestionnaireSubmission).GetProperty("ExpectedRevision") == null &&
    typeof(QuestionnaireSubmission).GetProperty("RespondentRelationship") == null);
var draft = new QuestionnaireDraftResponse(task, version, ulong.MaxValue, null, null, [], null, utc, "IN_PROGRESS");
var draftJson = JsonSerializer.SerializeToElement(draft, options);
Check("draft UTC timestamp serializes with Z", () => draftJson.GetProperty("updatedAtUtc").GetString() == "2026-10-30T01:02:03Z");
Check("draft revision UInt64 declaration and JSON value preserved", () =>
    typeof(QuestionnaireDraftResponse).GetProperty("Revision")!.PropertyType == typeof(ulong) &&
    draftJson.GetProperty("revision").GetUInt64() == ulong.MaxValue);
Check("draft JSON contains only declared DTO fields", () =>
    draftJson.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] {
        "taskId", "questionnaireVersionId", "revision", "respondentName", "filledOn", "answers", "observation", "updatedAtUtc", "taskStatus"
    }.Order()));
Check("core draft answer list is nonnullable metadata", () =>
    new NullabilityInfoContext().Create(typeof(QuestionnaireDraftResponse).GetProperty("Answers")!).ReadState == NullabilityState.NotNull);

Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL (format only; no Bridge/HTTP/DB validation)");
return failed == 0 ? 0 : 1;
