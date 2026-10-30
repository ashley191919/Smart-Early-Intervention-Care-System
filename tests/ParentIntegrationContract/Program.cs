using System.Reflection;
using System.Security.Claims;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Assertion failed");
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
bool Signature(Type owner, string name, Type result, params Type[] parameters)
{
    var method = owner.GetMethod(name);
    return method?.ReturnType == result && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters);
}
async Task Denied(string name, Func<Task> work)
{
    try { await work(); Check(name, () => false); }
    catch (QuestionnaireWorkflowException e)
    { Check(name, () => e.Code == "PARENT_ACCESS_NOT_INTEGRATED" && e.Status == 503); }
    catch (Exception e) { Check($"{name} unexpected {e.GetType().Name}", () => false); }
}

Check("contract identifier", () => ParentGatewayIntegrationContract.Version == "parent-bridge-contract-1");
Check("authorize signature uses actual existing transaction and trusted principal", () => Signature(
    typeof(IParentTransactionAccessAdapter), "LockAndAuthorizeAsync", typeof(Task<ParentTransactionAccess>),
    typeof(IParentQuestionnaireTransaction), typeof(ClaimsPrincipal), typeof(Guid),
    typeof(ParentAccessExpectation), typeof(bool), typeof(CancellationToken)));
Check("revalidate signature carries trusted principal and original evidence", () => Signature(
    typeof(IParentTransactionAccessAdapter), "RevalidateAsync", typeof(Task),
    typeof(IParentQuestionnaireTransaction), typeof(ClaimsPrincipal), typeof(ParentTransactionAccess), typeof(CancellationToken)));
Check("encoder accepts only canonical content", () => Signature(
    typeof(IParentCanonicalSubmissionEncoder), "Encode", typeof(byte[]), typeof(ParentCanonicalSubmission)));
Check("canonical includes relationship but excludes revision/key/author", () =>
    typeof(ParentCanonicalSubmission).GetProperty("RespondentRelationship")?.PropertyType == typeof(string) &&
    new[] { "ExpectedRevision", "IdempotencyKey", "ActorUserId", "Principal" }
        .All(p => typeof(ParentCanonicalSubmission).GetProperty(p) == null));
Check("submit revision is UInt64", () => typeof(ParentSubmitCommand).GetProperty("ExpectedRevision")?.PropertyType == typeof(ulong));
Check("parent commands reuse existing core payloads", () =>
    typeof(ParentSaveDraftCommand).GetProperty("Payload")?.PropertyType == typeof(SaveQuestionnaireDraftRequest) &&
    typeof(ParentSubmitCommand).GetProperty("Payload")?.PropertyType == typeof(QuestionnaireSubmission));
var nullability = new NullabilityInfoContext();
Check("draft relationship nullable; submit relationship required metadata", () =>
    nullability.Create(typeof(ParentSaveDraftCommand).GetProperty("RespondentRelationship")!).ReadState == NullabilityState.Nullable &&
    nullability.Create(typeof(ParentSubmitCommand).GetProperty("RespondentRelationship")!).ReadState == NullabilityState.NotNull);

var id = Guid.Parse("11111111-1111-4111-8111-111111111111");
var expectation = new ParentAccessExpectation(id, id, id, id, id, id, "TEST-ONLY:NOT-AUTHORIZATION");
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", id.ToString())], "synthetic-test"));
var call = new ParentGatewayCallContext(principal, expectation);
Check("call preserves original principal reference", () => ReferenceEquals(call.Principal, principal) &&
    ReferenceEquals(call.Principal.Identities.Single(), principal.Identities.Single()));
Check("expectation has binding/document snapshot fields", () =>
    typeof(ParentAccessExpectation).GetProperty("GuardianId")?.PropertyType == typeof(Guid) &&
    typeof(ParentAccessExpectation).GetProperty("DocumentVersionId")?.PropertyType == typeof(Guid) &&
    typeof(ParentAccessExpectation).GetProperty("EligibilityStamp")?.PropertyType == typeof(string));
var legacy = new ParentDraftResult(new QuestionnaireDraftResponse(id, id, 0, null, null, [], null, null, "PENDING"), null);
Check("legacy result keeps null relationship", () => legacy.RespondentRelationship == null && legacy.Draft.Revision == 0);
var draft = new ParentSaveDraftCommand(new SaveQuestionnaireDraftRequest(id, 0, null, null, [], null), null);
Check("absent draft command can carry revision zero and no relationship", () => draft.Payload.ExpectedRevision == 0 && draft.RespondentRelationship == null);

IParentRevisionCodec codec = new StrictParentRevisionCodec();
foreach (var (text, value) in new (string, ulong)[] {
    ("0", 0), ("1", 1), ("9007199254740991", 9007199254740991UL),
    ("9007199254740992", 9007199254740992UL), ("9007199254740993", 9007199254740993UL),
    ("18446744073709551615", ulong.MaxValue) })
    Check($"revision codec boundary {text}", () => codec.TryParse(text, out var parsed) && parsed == value && codec.Format(value) == text);
foreach (var text in new string?[] { null, "", " ", "01", "00", "+1", "-1", "１", "1.0", "18446744073709551616" })
    Check($"revision codec rejects [{text ?? "NULL"}]", () => !codec.TryParse(text, out var parsed) && parsed == 0);

// This trap never constructs DbContext/connection/host. Every member throws if touched.
var trap = new TrapTransaction();
IParentTransactionAccessAdapter deny = new DenyParentTransactionAccessAdapter();
await Denied("write authorization always denied", () => deny.LockAndAuthorizeAsync(trap, principal, id, expectation, true));
await Denied("read authorization also denied", () => deny.LockAndAuthorizeAsync(trap, principal, id, expectation, false));
await Denied("fabricated evidence never authorizes revalidation", () => deny.RevalidateAsync(trap, principal, new(id, expectation)));
await Denied("empty expectation/principal never authorizes", () => deny.LockAndAuthorizeAsync(
    trap, new ClaimsPrincipal(), Guid.Empty, new(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, ""), true));
Check("default adapter never touched transaction", () => trap.Touches == 0);
Check("existing gateway has no overload using new commands", () => typeof(ParentQuestionnaireGateway).GetMethods()
    .All(m => m.GetParameters().All(p => p.ParameterType != typeof(ParentSubmitCommand) && p.ParameterType != typeof(ParentSaveDraftCommand))));

Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL; no host/socket/database; no integration enabled");
return failed == 0 ? 0 : 1;

sealed class TrapTransaction : IParentQuestionnaireTransaction
{
    public int Touches { get; private set; }
    private Exception Touched() { Touches++; return new InvalidOperationException("Transaction must not be touched"); }
    public ApplicationDbContext? DbContext => throw Touched();
    public Task<QuestionnaireTask?> LockTaskAsync(Guid id, CancellationToken ct) => throw Touched();
    public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => throw Touched();
    public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => throw Touched();
    public Task<QuestionnaireResponse?> FindResponseAsync(Guid id, CancellationToken ct) => throw Touched();
    public Task<QuestionnaireResponse?> FindByKeyAsync(Guid user, Guid key, CancellationToken ct) => throw Touched();
    public void AddDraft(QuestionnaireDraft draft) => throw Touched();
    public void AddResponse(QuestionnaireResponse response) => throw Touched();
    public Task CommitAsync(CancellationToken ct) => throw Touched();
    public ValueTask DisposeAsync() => throw Touched();
}
