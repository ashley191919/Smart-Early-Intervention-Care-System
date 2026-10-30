using System.Globalization;
using System.Security.Claims;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;
using EarlyInterventionCare.Integration.ParentBridge;

var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Assertion failed");
        passed++; Console.WriteLine($"PASS {name}");
    }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Reject<T>(string name, Action action) where T : Exception
{
    Check(name, () =>
    {
        try { action(); return false; }
        catch (T) { return true; }
    });
}
var version = Guid.Parse("11111111-1111-4111-8111-111111111111");
var answers = new[] { new QuestionnaireAnswer("SYNTHETIC_Q1", "0") };
var draft = new ParentDraftRequest(version, "0", "  synthetic  ", "2026-10-30", "FATHER", answers, null);
var submit = new ParentSubmitRequest(version, "1", "synthetic", "2026-10-30", "MOTHER", answers, " note ");
var mappedDraft = ParentBridgeDtoMapper.MapDraft(draft);
Check("draft preserves all payload fields", () => mappedDraft.Payload.QuestionnaireVersionId == version &&
    mappedDraft.Payload.ExpectedRevision == 0 && mappedDraft.Payload.RespondentName == draft.RespondentName &&
    mappedDraft.Payload.FilledOn == draft.FilledOn && mappedDraft.Payload.Observation == null &&
    mappedDraft.RespondentRelationship == "FATHER" && mappedDraft.Payload.Answers!.SequenceEqual(answers));
var mappedSubmit = ParentBridgeDtoMapper.MapSubmit(submit);
Check("submit preserves all payload fields and revision", () => mappedSubmit.ExpectedRevision == 1 &&
    mappedSubmit.Payload.QuestionnaireVersionId == version && mappedSubmit.Payload.RespondentName == submit.RespondentName &&
    mappedSubmit.Payload.FilledOn == submit.FilledOn && mappedSubmit.Payload.Observation == " note " &&
    mappedSubmit.RespondentRelationship == "MOTHER" && mappedSubmit.Payload.Answers!.SequenceEqual(answers));
Check("unfilled draft keeps null relationship/metadata", () =>
{
    // null! models a missing wire value against 11's still-nonnullable pinned declaration.
    var result = ParentBridgeDtoMapper.MapDraft(draft with { RespondentRelationship = null!, RespondentName = null, FilledOn = null });
    return result.RespondentRelationship == null && result.Payload.RespondentName == null && result.Payload.FilledOn == null;
});
Check("no shared draft answer array", () => !ReferenceEquals(mappedDraft.Payload.Answers, answers));
Check("no shared submit answer array", () => !ReferenceEquals(mappedSubmit.Payload.Answers, answers));
var mutation = new[] { new QuestionnaireAnswer("A", "0") };
var detached = ParentBridgeDtoMapper.MapDraft(draft with { Answers = mutation });
mutation[0] = new("B", "1");
Check("caller array mutation cannot change mapped command", () => detached.Payload.Answers![0]!.QuestionId == "A");
foreach (var (text, value) in new (string, ulong)[] {
    ("0", 0), ("1", 1), ("9007199254740991", 9007199254740991UL),
    ("9007199254740992", 9007199254740992UL), ("9007199254740993", 9007199254740993UL),
    ("18446744073709551615", ulong.MaxValue) })
    Check($"both commands preserve revision {text}", () =>
        ParentBridgeDtoMapper.MapDraft(draft with { ExpectedRevision = text }).Payload.ExpectedRevision == value &&
        ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = text }).ExpectedRevision == value);
foreach (var text in new string?[] { null, "", " ", " 1", "1 ", "00", "01", "+1", "-1", "１", "1.0", "18446744073709551616" })
{
    Reject<FormatException>($"draft rejects revision [{text ?? "NULL"}]", () => ParentBridgeDtoMapper.MapDraft(draft with { ExpectedRevision = text! }));
    Reject<FormatException>($"submit rejects revision [{text ?? "NULL"}]", () => ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = text! }));
}
foreach (var relationship in new[] { "FATHER", "MOTHER", "GRANDPARENT", "RELATIVE", "GUARDIAN", "CAREGIVER", "OTHER" })
    Check($"both commands preserve relationship {relationship}", () =>
        ParentBridgeDtoMapper.MapDraft(draft with { RespondentRelationship = relationship }).RespondentRelationship == relationship &&
        ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = relationship }).RespondentRelationship == relationship);
Reject<ArgumentException>("submit rejects missing relationship", () => ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = null! }));
foreach (var relationship in new[] { "", " ", "father", " FATHER", "FATHER ", "UNKNOWN" })
{
    Reject<ArgumentException>($"draft rejects invalid relationship [{relationship}]", () => ParentBridgeDtoMapper.MapDraft(draft with { RespondentRelationship = relationship }));
    Reject<ArgumentException>($"submit rejects invalid relationship [{relationship}]", () => ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = relationship }));
}
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", version.ToString())], "synthetic"));
var context = new ParentRequestContext(principal, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
var originalClaims = principal.Claims.Select(c => (c.Type, c.Value)).ToArray();
Check("original principal preserved despite context IDs", () => ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(context), principal) &&
    principal.Claims.Select(c => (c.Type, c.Value)).SequenceEqual(originalClaims));
Check("anonymous principal is not upgraded to authenticated", () =>
{
    var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
    return ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(context with { Principal = anonymous }), anonymous) &&
        !anonymous.Identities.Any(i => i.IsAuthenticated);
});
Reject<ArgumentNullException>("null draft rejected", () => ParentBridgeDtoMapper.MapDraft(null!));
Reject<ArgumentNullException>("null submit rejected", () => ParentBridgeDtoMapper.MapSubmit(null!));
Reject<ArgumentNullException>("null context rejected", () => ParentBridgeDtoMapper.GetOriginalPrincipal(null!));
Reject<ArgumentNullException>("null principal rejected without synthesis", () => ParentBridgeDtoMapper.GetOriginalPrincipal(context with { Principal = null! }));
Check("null answers not filled with synthetic answers", () => ParentBridgeDtoMapper.MapDraft(draft with { Answers = null! }).Payload.Answers == null);
Check("answer order/null entries preserved for core validator", () =>
{
    var result = ParentBridgeDtoMapper.MapDraft(draft with { Answers = [new("B", "1"), null!, new("A", "0")] });
    return result.Payload.Answers is { Length: 3 } a && a[0]!.QuestionId == "B" && a[1] == null && a[2]!.QuestionId == "A";
});
Check("mapper has no instance dependencies or transaction parameter", () => typeof(ParentBridgeDtoMapper).IsAbstract &&
    typeof(ParentBridgeDtoMapper).IsSealed && typeof(ParentBridgeDtoMapper).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Length == 0 &&
    typeof(ParentBridgeDtoMapper).GetMethods().All(m => m.GetParameters().All(p => p.ParameterType != typeof(IParentQuestionnaireTransaction))));
Check("teacher submission DTO unchanged", () => typeof(QuestionnaireSubmission).GetProperty("RespondentRelationship") == null &&
    typeof(QuestionnaireSubmission).GetProperty("ExpectedRevision") == null);
var savedCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
    Check("mapper is culture independent", () => ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = "18446744073709551615" }).ExpectedRevision == ulong.MaxValue);
}
finally { CultureInfo.CurrentCulture = savedCulture; }
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL; mapper only, no host/socket/database");
return failed == 0 ? 0 : 1;
