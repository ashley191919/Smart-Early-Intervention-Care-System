using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using EarlyInterventionCare.Integration.ParentBridge;

int passed = 0, failed = 0;
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
async Task Check(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
async Task Denied(string name, Func<Task> work) => await Check(name, async () => {
    try { await work(); throw new Exception("Expected fail-closed result"); }
    catch (QuestionnaireWorkflowException e)
    {
        Assert(e.Code == "PARENT_ACCESS_NOT_INTEGRATED" && e.Status == 503);
        Assert(e.Message == "Parent bridge qualification integration is unavailable.");
        Assert(!e.Message.Contains("SYNTHETIC_PRIVATE_MARKER"));
    }
});
var task = Guid.Parse("11111111-1111-4111-8111-111111111111");
var version = Guid.Parse("22222222-2222-4222-8222-222222222222");
var user = Guid.Parse("33333333-3333-4333-8333-333333333333");
var guardian = Guid.Parse("44444444-4444-4444-8444-444444444444");
var key = Guid.Parse("55555555-5555-4555-8555-555555555555");
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", user.ToString())], "synthetic"));
var context = new ParentRequestContext(principal, user, version, task, key, version, guardian);
var draft = new ParentDraftRequest(version, "18446744073709551615", null, null, null, [], null);
var submit = new ParentSubmitRequest(version, "7", "Synthetic respondent", "2026-10-30", "MOTHER", [new("Q1", "0")], null);
IParentQuestionnaireGateway bridge = new ParentGatewayBridgeSkeleton();

foreach (var method in typeof(IParentQuestionnaireGateway).GetMethods())
    await Check($"four-method signature: {method.Name}", () => {
        var actual = typeof(ParentGatewayBridgeSkeleton).GetMethod(method.Name)!;
        Assert(actual.ReturnType == method.ReturnType);
        Assert(actual.GetParameters().Select(p => (p.Name, p.ParameterType, p.IsOptional))
            .SequenceEqual(method.GetParameters().Select(p => (p.Name, p.ParameterType, p.IsOptional))));
        return Task.CompletedTask;
    });
await Check("exactly four instance methods, no forwarding overloads", () => {
    Assert(typeof(ParentGatewayBridgeSkeleton).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length == 4);
    return Task.CompletedTask;
});
await Check("skeleton is internal and absent from formal API assembly", () => {
    Assert(!typeof(ParentGatewayBridgeSkeleton).IsPublic);
    Assert(typeof(ParentSubmitCommand).Assembly.GetType(typeof(ParentGatewayBridgeSkeleton).FullName!) is null);
    Assert(typeof(ParentSubmitCommand).Assembly.GetType(typeof(IParentQuestionnaireGateway).FullName!) is null);
    return Task.CompletedTask;
});
await Check("no state, store, adapter, switch or injected forwarding factory", () => {
    Assert(typeof(ParentGatewayBridgeSkeleton).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length == 0);
    var constructors = typeof(ParentGatewayBridgeSkeleton).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(constructors.Length == 1 && constructors[0].GetParameters().Length == 0);
    Assert(typeof(ParentGatewayBridgeSkeleton).GetInterfaces().SequenceEqual(new[] { typeof(IParentQuestionnaireGateway) }));
    return Task.CompletedTask;
});

// These are rejection tests, NOT author/scope/receipt/storage acceptance.
foreach (var (label, candidate) in new (string, ParentRequestContext)[] {
    ("normal trusted-context-shaped data but no Stamp", context),
    ("different actor/Guardian", context with { ActorUserId = guardian, GuardianId = user }),
    ("anonymous principal", context with { Principal = new ClaimsPrincipal() }),
    ("null context", null!),
})
{
    await Denied($"GetDraft denies {label}", () => bridge.GetDraftAsync(candidate, task));
    await Denied($"SaveDraft denies {label}", () => bridge.SaveDraftAsync(candidate, task, draft));
    await Denied($"Submit denies {label}", () => bridge.SubmitAsync(candidate, task, key, submit));
    await Denied($"GetReceipt denies {label}", () => bridge.GetReceiptAsync(candidate, task));
}
await Denied("changed Key does not enable a submission", () => bridge.SubmitAsync(context, task, Guid.NewGuid(), submit));
await Denied("changed content does not produce a conflict/version oracle", () => bridge.SubmitAsync(context, task, key, submit with { Observation = "SYNTHETIC_PRIVATE_MARKER" }));
await Denied("null input and empty IDs do not reach a legacy fallback", () => bridge.SubmitAsync(context, Guid.Empty, Guid.Empty, null!));
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    await Denied("cancelled draft read stays denied", () => bridge.GetDraftAsync(context, task, cancelled.Token));
    await Denied("cancelled save stays denied", () => bridge.SaveDraftAsync(context, task, draft, cancelled.Token));
    await Denied("cancelled submit stays denied", () => bridge.SubmitAsync(context, task, key, submit, cancelled.Token));
    await Denied("cancelled receipt read stays denied", () => bridge.GetReceiptAsync(context, task, cancelled.Token));
}

await Check("prepared Draft preserves maximal Revision and null relationship", () => {
    var command = ParentBridgeDtoMapper.MapDraft(draft);
    Assert(command.Payload.ExpectedRevision == ulong.MaxValue && command.RespondentRelationship is null);
    return Task.CompletedTask;
});
await Check("Submit metadata stays in ParentSubmitCommand, not legacy Payload alone", () => {
    var command = ParentBridgeDtoMapper.MapSubmit(submit);
    Assert(command.ExpectedRevision == 7 && command.RespondentRelationship == "MOTHER");
    Assert(command.Payload.QuestionnaireVersionId == version && command.Payload.Answers![0]!.OptionValue == "0");
    return Task.CompletedTask;
});
await Check("original principal/Guardian preserved without synthesizing access", () => {
    var kept = ParentBridgeDtoMapper.PreserveContext(context);
    Assert(ReferenceEquals(kept, context) && ReferenceEquals(kept.Principal, principal) && kept.GuardianId == guardian);
    Assert(typeof(ParentRequestContext).GetProperty("EligibilityStamp") is null);
    Assert(typeof(ParentGatewayBridgeSkeleton).GetMethods().All(m => m.ReturnType != typeof(ParentAccessExpectation)));
    return Task.CompletedTask;
});
await Check("relationship null/invalid Revision cannot be laundered by pure mapping", () => {
    foreach (var input in new[] { submit with { RespondentRelationship = null! }, submit with { ExpectedRevision = "07" } })
    {
        try { ParentBridgeDtoMapper.MapSubmit(input); throw new Exception("Expected format rejection"); }
        catch (CaseAccessException e) { Assert(e.Code == "INVALID_INPUT" && e.Status == 400); }
    }
    return Task.CompletedTask;
});
await Check("Draft response mapping uses supplied stored relationship, not current Guardian", () => {
    var core = new QuestionnaireDraftResponse(task, version, 7, null, null, [], null, null, "IN_PROGRESS");
    var result = ParentBridgeDtoMapper.MapDraftResult(new(core, null), task, version);
    Assert(result.Revision == "7" && result.RespondentRelationship is null);
    return Task.CompletedTask;
});
await Check("receipt maps independently, without hash/version/author disclosure", () => {
    var core = new ParentQuestionnaireReceipt(user, task, version, new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), "SUBMITTED", false);
    var result = ParentBridgeDtoMapper.MapReceipt(core, task, version);
    Assert(result.ResponseId == user && !result.Replayed);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
    Assert(json.RootElement.EnumerateObject().Count() == 6 && !json.RootElement.TryGetProperty("HashVersion", out _));
    return Task.CompletedTask;
});
await Check("no alternative Store/transaction/authorization implementation", () => {
    Assert(!typeof(Program).Assembly.GetTypes().Any(t => !t.IsInterface &&
        (typeof(IParentQuestionnaireStore).IsAssignableFrom(t) || typeof(IParentQuestionnaireTransaction).IsAssignableFrom(t) ||
         typeof(IParentTransactionAccessAdapter).IsAssignableFrom(t) || typeof(IParentTaskAccessAdapter).IsAssignableFrom(t))));
    return Task.CompletedTask;
});
await Check("teacher/common submission DTO is unchanged", () => {
    Assert(typeof(QuestionnaireSubmission).GetProperty("RespondentRelationship") is null &&
        typeof(QuestionnaireSubmission).GetProperty("ExpectedRevision") is null);
    return Task.CompletedTask;
});
Console.WriteLine("BLOCKED real 11 types in API, EligibilityStamp/generation and transaction Adapter are not delivered");
Console.WriteLine("BLOCKED guarded Draft/Receipt and actual Bridge forwarding are not implemented; no legacy fallback");
Console.WriteLine("BLOCKED Schema/HTTP/MySQL atomicity, author/version/idempotency integration acceptance is not performed");
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL, 3 BLOCKED; shape/mapping/denial only; no API/socket/database");
return failed > 0 ? 1 : 2;
