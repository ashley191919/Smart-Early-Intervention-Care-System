using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using EarlyInterventionCare.Integration.ParentBridge;

int passed = 0, failed = 0;
void Check(string name, Func<bool> action)
{
    try { if (!action()) throw new Exception("Assertion failed"); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
void Invalid(string name, Action action, string code = "INVALID_INPUT", int status = 400) => Check(name, () => {
    try { action(); return false; }
    catch (CaseAccessException e) { return e.Code == code && e.Status == status && !e.Message.Contains("SYNTHETIC_PRIVATE_MARKER"); }
});
void Null(string name, Action action) => Check(name, () => {
    try { action(); return false; } catch (ArgumentNullException e) { return !e.Message.Contains("SYNTHETIC_PRIVATE_MARKER"); }
});
var version = Guid.Parse("11111111-1111-4111-8111-111111111111");
var task = Guid.Parse("22222222-2222-4222-8222-222222222222");
var user = Guid.Parse("33333333-3333-4333-8333-333333333333");
var guardian = Guid.Parse("44444444-4444-4444-8444-444444444444");
QuestionnaireAnswer[] answers = [new("Q2", "1"), new("Q1", "0")];
var draft = new ParentDraftRequest(version, "0", null, null, null, answers, null);
var submit = new ParentSubmitRequest(version, "1", " Synthetic ", "2026-10-30", "MOTHER", answers, " note ");
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", user.ToString())], "synthetic"));
var context = new ParentRequestContext(principal, user, version, task, user, version, guardian);
var nullability = new NullabilityInfoContext();

Check("pinned Context has the exact seven constructor fields", () =>
    typeof(ParentRequestContext).GetConstructors().Single().GetParameters().Select(p => p.Name).SequenceEqual(
        new[] { "Principal", "ActorUserId", "OrganizationId", "CaseId", "ConsentId", "DocumentVersionId", "GuardianId" }));
Check("draft request and response relationship are nullable metadata", () =>
    nullability.Create(typeof(ParentDraftRequest).GetProperty("RespondentRelationship")!).ReadState == NullabilityState.Nullable &&
    nullability.Create(typeof(ParentDraftResponse).GetProperty("RespondentRelationship")!).ReadState == NullabilityState.Nullable);
Check("submit relationship remains nonnullable metadata", () =>
    nullability.Create(typeof(ParentSubmitRequest).GetProperty("RespondentRelationship")!).ReadState == NullabilityState.NotNull);
Check("four pinned Gateway methods unchanged", () => typeof(IParentQuestionnaireGateway).GetMethods().Select(m => m.Name).Order()
    .SequenceEqual(new[] { "GetDraftAsync", "GetReceiptAsync", "SaveDraftAsync", "SubmitAsync" }));
Check("verbatim pinned payload mapping matches upstream Git blob", () => {
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pinned11PayloadMapping.cs"), Encoding.UTF8).Replace("\r\n", "\n");
    var marker = "// END PIN METADATA\n";
    var raw = Encoding.UTF8.GetBytes(source[(source.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
    var prefix = Encoding.UTF8.GetBytes($"blob {raw.Length}\0");
    return Convert.ToHexString(SHA1.HashData(prefix.Concat(raw).ToArray())).ToLowerInvariant() == "30bfe65d72f56a9eb88a2b18358fc50183fd849e";
});
Check("GuardianId and all Context fields preserved by reference", () => {
    var kept = ParentBridgeDtoMapper.PreserveContext(context);
    return ReferenceEquals(kept, context) && kept.GuardianId == guardian && kept.ActorUserId == user &&
        kept.OrganizationId == version && kept.CaseId == task && kept.ConsentId == user && kept.DocumentVersionId == version;
});
Check("mismatched Context IDs cannot synthesize principal claims", () => {
    var mismatched = context with { ActorUserId = guardian, GuardianId = user };
    return ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(mismatched), principal) &&
        principal.FindAll("userId").Single().Value == user.ToString() && principal.Claims.Count() == 1;
});
Check("anonymous principal is never upgraded", () => {
    var anonymous = new ClaimsPrincipal();
    return ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(context with { Principal = anonymous }), anonymous) &&
        !anonymous.Identities.Any(i => i.IsAuthenticated);
});
Check("no fabricated Stamp or core access context", () =>
    typeof(ParentRequestContext).GetProperty("EligibilityStamp") is null &&
    typeof(ParentBridgeDtoMapper).GetMethods().All(m => m.ReturnType != typeof(ParentGatewayCallContext) && m.ReturnType != typeof(ParentAccessExpectation)));

Check("null old draft relationship is retained without inference", () => {
    var c = ParentBridgeDtoMapper.MapDraft(draft);
    return c.RespondentRelationship is null && c.Payload.ExpectedRevision == 0 && c.Payload.RespondentName is null && c.Payload.FilledOn is null;
});
Check("submit uses pinned preparation without losing any field", () => {
    var c = ParentBridgeDtoMapper.MapSubmit(submit);
    return c.ExpectedRevision == 1 && c.RespondentRelationship == "MOTHER" && c.Payload.QuestionnaireVersionId == version &&
        c.Payload.RespondentName == submit.RespondentName && c.Payload.FilledOn == submit.FilledOn && c.Payload.Observation == submit.Observation &&
        c.Payload.Answers!.SequenceEqual(answers) && !ReferenceEquals(c.Payload.Answers, answers);
});
Check("pinned preparation clones draft answer array once", () => {
    var local = answers.ToArray(); var c = ParentBridgeDtoMapper.MapDraft(draft with { Answers = local }); local[0] = new("changed", "3");
    return c.Payload.Answers![0]!.QuestionId == "Q2" && !ReferenceEquals(c.Payload.Answers, local);
});
Check("no extra normalization, sorting or null-answer repair", () => {
    var c = ParentBridgeDtoMapper.MapDraft(draft with { RespondentName = "  Synthetic  ", Observation = "  note  ", Answers = [answers[0], null!, answers[1]] });
    return c.Payload.RespondentName == "  Synthetic  " && c.Payload.Observation == "  note  " && c.Payload.Answers![1] is null && c.Payload.Answers[0]!.QuestionId == "Q2";
});
foreach (var text in new[] { "0", "1", "9007199254740991", "9007199254740992", "9007199254740993", "18446744073709551615" })
    Check($"both commands preserve exact UInt64 revision {text}", () => {
        var expected = ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return ParentBridgeDtoMapper.MapDraft(draft with { ExpectedRevision = text }).Payload.ExpectedRevision == expected &&
            ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = text }).ExpectedRevision == expected;
    });
var invalidRevisions = new string?[] { null, "", " ", " 1", "1 ", "00", "01", "+1", "-1", "１", "١", "1.0", "1e2", "18446744073709551616", "SYNTHETIC_PRIVATE_MARKER" };
for (var i = 0; i < invalidRevisions.Length; i++)
{
    var text = invalidRevisions[i];
    Invalid($"draft rejects revision case {i} with safe INVALID_INPUT/400", () => ParentBridgeDtoMapper.MapDraft(draft with { ExpectedRevision = text! }));
    Invalid($"submit rejects revision case {i} with safe INVALID_INPUT/400", () => ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = text! }));
}
foreach (var relation in new[] { "FATHER", "MOTHER", "GRANDPARENT", "RELATIVE", "GUARDIAN", "CAREGIVER", "OTHER" })
    Check($"both commands preserve relationship {relation}", () => ParentBridgeDtoMapper.MapDraft(draft with { RespondentRelationship = relation }).RespondentRelationship == relation &&
        ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = relation }).RespondentRelationship == relation);
Invalid("submit requires explicit relationship", () => ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = null! }));
foreach (var relation in new[] { "", " ", "mother", " MOTHER", "MOTHER ", "SYNTHETIC_PRIVATE_MARKER" })
{
    Invalid("invalid draft relationship rejected without echo", () => ParentBridgeDtoMapper.MapDraft(draft with { RespondentRelationship = relation }));
    Invalid("invalid submit relationship rejected without echo", () => ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = relation }));
}
Invalid("latest draft rejects missing answers rather than inventing data", () => ParentBridgeDtoMapper.MapDraft(draft with { Answers = null! }));
Invalid("latest submit rejects missing answers", () => ParentBridgeDtoMapper.MapSubmit(submit with { Answers = null! }));
Check("request JSON keeps maximal Revision as decimal string", () => {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var raw = JsonSerializer.Serialize(submit with { ExpectedRevision = "18446744073709551615" }, options);
    using var json = JsonDocument.Parse(raw);
    return json.RootElement.GetProperty("expectedRevision").ValueKind == JsonValueKind.String &&
        ParentBridgeDtoMapper.MapSubmit(JsonSerializer.Deserialize<ParentSubmitRequest>(raw, options)!).ExpectedRevision == ulong.MaxValue;
});
Check("numeric JSON Revision cannot enter string DTO conversion", () => {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var raw = JsonSerializer.Serialize(submit, options).Replace("\"expectedRevision\":\"1\"", "\"expectedRevision\":1", StringComparison.Ordinal);
    using var json = JsonDocument.Parse(raw);
    if (json.RootElement.GetProperty("expectedRevision").ValueKind != JsonValueKind.Number) return false;
    try { JsonSerializer.Deserialize<ParentSubmitRequest>(raw, options); return false; } catch (JsonException) { return true; }
});

var coreDraft = new QuestionnaireDraftResponse(task, version, ulong.MaxValue, "Synthetic", "2026-10-30", answers, null,
    new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), "IN_PROGRESS");
Check("DraftResult restores nullable relationship and decimal-string JSON revision", () => {
    var result = ParentBridgeDtoMapper.MapDraftResult(new(coreDraft, null), task, version);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return result.RespondentRelationship is null && result.Revision == "18446744073709551615" &&
        json.RootElement.GetProperty("revision").ValueKind == JsonValueKind.String && json.RootElement.GetProperty("respondentRelationship").ValueKind == JsonValueKind.Null &&
        !ReferenceEquals(result.Answers, answers);
});
Check("stored relationship is preserved on response", () => ParentBridgeDtoMapper.MapDraftResult(new(coreDraft, "CAREGIVER"), task, version).RespondentRelationship == "CAREGIVER");
Invalid("foreign draft Task rejected", () => ParentBridgeDtoMapper.MapDraftResult(new(coreDraft with { TaskId = user }, null), task, version), "PARENT_CORE_RESPONSE_INVALID", 503);
Invalid("foreign draft version rejected", () => ParentBridgeDtoMapper.MapDraftResult(new(coreDraft with { QuestionnaireVersionId = user }, null), task, version), "PARENT_CORE_RESPONSE_INVALID", 503);
Invalid("submitted draft cannot be restored as editable", () => ParentBridgeDtoMapper.MapDraftResult(new(coreDraft with { TaskStatus = "SUBMITTED" }, null), task, version), "PARENT_CORE_RESPONSE_INVALID", 503);
Invalid("invalid stored relationship fails without echo", () => ParentBridgeDtoMapper.MapDraftResult(new(coreDraft, "SYNTHETIC_PRIVATE_MARKER"), task, version), "PARENT_CORE_RESPONSE_INVALID", 503);
var coreReceipt = new ParentQuestionnaireReceipt(user, task, version, new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), "SUBMITTED", true);
Check("receipt retains all IDs, UTC timestamp/status and replay flag", () => {
    var r = ParentBridgeDtoMapper.MapReceipt(coreReceipt, task, version);
    return r.ResponseId == user && r.TaskId == task && r.QuestionnaireVersionId == version && r.Replayed &&
        r.SubmittedAtUtc == coreReceipt.SubmittedAtUtc && r.SubmittedAtUtc.Kind == DateTimeKind.Utc && r.TaskStatus == "SUBMITTED";
});
Check("receipt JSON contains only six receipt fields, no answers or Context", () => {
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(ParentBridgeDtoMapper.MapReceipt(coreReceipt, task, version), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return json.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(
        new[] { "questionnaireVersionId", "replayed", "responseId", "submittedAtUtc", "taskId", "taskStatus" }) &&
        json.RootElement.GetProperty("submittedAtUtc").GetString()!.EndsWith('Z');
});
foreach (var changed in new[] { coreReceipt with { ResponseId = Guid.Empty }, coreReceipt with { TaskId = user },
    coreReceipt with { QuestionnaireVersionId = user }, coreReceipt with { TaskStatus = "SYNTHETIC_PRIVATE_MARKER" },
    coreReceipt with { SubmittedAtUtc = DateTime.SpecifyKind(coreReceipt.SubmittedAtUtc, DateTimeKind.Unspecified) } })
    Invalid("invalid receipt rejected without exposing contents", () => ParentBridgeDtoMapper.MapReceipt(changed, task, version), "PARENT_CORE_RESPONSE_INVALID", 503);
Null("null draft rejected", () => ParentBridgeDtoMapper.MapDraft(null!));
Null("null submit rejected", () => ParentBridgeDtoMapper.MapSubmit(null!));
Null("null Context rejected", () => ParentBridgeDtoMapper.PreserveContext(null!));
Null("null principal rejected without synthesis", () => ParentBridgeDtoMapper.PreserveContext(context with { Principal = null! }));
Null("null DraftResult rejected", () => ParentBridgeDtoMapper.MapDraftResult(null!, task, version));
Null("null receipt rejected", () => ParentBridgeDtoMapper.MapReceipt(null!, task, version));
Check("Mapper is absent from formal API assembly", () => typeof(ParentSubmitCommand).Assembly.GetType(typeof(ParentBridgeDtoMapper).FullName!) is null);
Check("no Gateway implementation or access adapter in tool assembly", () => !typeof(Program).Assembly.GetTypes().Any(t => !t.IsInterface &&
    (typeof(IParentQuestionnaireGateway).IsAssignableFrom(t) || typeof(IParentTransactionAccessAdapter).IsAssignableFrom(t))));
Check("teacher Submission DTO remains unchanged", () => typeof(QuestionnaireSubmission).GetProperty("RespondentRelationship") is null && typeof(QuestionnaireSubmission).GetProperty("ExpectedRevision") is null);
var culture = CultureInfo.CurrentCulture;
try {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
    Check("InvariantCulture is retained across input and output", () => ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = "18446744073709551615" }).ExpectedRevision == ulong.MaxValue &&
        ParentBridgeDtoMapper.MapDraftResult(new(coreDraft, null), task, version).Revision == "18446744073709551615");
} finally { CultureInfo.CurrentCulture = culture; }
Console.WriteLine("BLOCKED EligibilityStamp/generation is not implemented; Context preservation is not authorization");
Console.WriteLine("BLOCKED formal Bridge/Adapter/transaction/HTTP/MySQL integration is not performed");
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL, 2 BLOCKED; pinned DTO/preparation only; no API/socket/DB");
return failed > 0 ? 1 : 2;
