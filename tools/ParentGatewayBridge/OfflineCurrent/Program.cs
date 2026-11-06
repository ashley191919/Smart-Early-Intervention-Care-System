using System.Reflection;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;
using EarlyInterventionCare.Integration.ParentBridge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

int pass = 0, fail = 0;
void Assert(bool value) { if (!value) throw new InvalidOperationException("Contract assertion failed"); }
async Task Check(string name, Func<Task> action)
{
    try { await action(); pass++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { fail++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
Task Pure(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });
// The pinned Git objects contain LF. Accept ONLY a checkout CRLF -> LF change;
// never strip BOM, lone CR, whitespace, or normalize Unicode/content.
string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).Replace("\r\n", "\n");
string VerifiedSource(byte[] bytes, JsonElement row)
{
    var source = Text(bytes);
    var canonical = new UTF8Encoding(false, true).GetBytes(source);
    var header = Encoding.ASCII.GetBytes($"blob {canonical.Length}\0");
    if (Convert.ToHexString(SHA1.HashData(header.Concat(canonical).ToArray())).ToLowerInvariant() != row.GetProperty("blob").GetString())
        throw new InvalidDataException("Fixed source blob mismatch");
    return source;
}
void VerifyExtraction(byte[] sourceBytes, byte[] compiledBytes, JsonElement row)
{
    // Verify first, then apply original LF/UTF-16 manifest offsets.
    var source = VerifiedSource(sourceBytes, row);
    var expected = row.GetProperty("prefix").GetString() + source.Substring(row.GetProperty("start").GetInt32(), row.GetProperty("length").GetInt32());
    Assert(Text(compiledBytes) == expected);
}
byte[] CheckoutBytes(string file)
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
    var psi = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-c", "safe.directory=" + root.Replace('\\', '/').TrimEnd('/'), "cat-file", "--filters", ":tools/ParentGatewayBridge/OfflineCurrent/" + file }) psi.ArgumentList.Add(arg);
    using var process = Process.Start(psi)!;
    using var buffer = new MemoryStream();
    process.StandardOutput.BaseStream.CopyTo(buffer);
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException("Read-only Git checkout simulation failed: " + error);
    return buffer.ToArray();
}
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "source-manifest.json")));
await Pure("fixed reference SHA", () => Assert(manifest.RootElement.GetProperty("commit").GetString() == "d268b7d70be33e04d44bdfa044b13339448815f6"));
foreach (var row in manifest.RootElement.GetProperty("sources").EnumerateArray())
{
    await Pure("source Git blob " + row.GetProperty("source").GetString(), () => {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, row.GetProperty("snapshot").GetString()!));
        _ = VerifiedSource(bytes, row);
    });
    await Pure("compiled source is exact extraction " + row.GetProperty("compiled").GetString(), () => {
        var sourceBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, row.GetProperty("snapshot").GetString()!));
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../..", row.GetProperty("compiled").GetString()!));
        VerifyExtraction(sourceBytes, File.ReadAllBytes(path), row);
    });
    var source = Text(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, row.GetProperty("snapshot").GetString()!)));
    var compiled = Text(File.ReadAllBytes(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../..", row.GetProperty("compiled").GetString()!))));
    foreach (var sourceCrLf in new[] { false, true })
    foreach (var compiledCrLf in new[] { false, true })
        await Pure($"LF/CRLF source={sourceCrLf} compiled={compiledCrLf} exact blob/offsets", () =>
            VerifyExtraction(Encoding.UTF8.GetBytes(sourceCrLf ? source.Replace("\n", "\r\n") : source), Encoding.UTF8.GetBytes(compiledCrLf ? compiled.Replace("\n", "\r\n") : compiled), row));
    foreach (var changed in new[] { "X" + source[1..], source + " ", "\uFEFF" + source, source.Replace("\n", "\r"), source.TrimEnd('\n'), source + "異動" })
        await Pure("source non-newline mutation rejected", () => {
            try { _ = VerifiedSource(Encoding.UTF8.GetBytes(changed), row); throw new InvalidOperationException("Mutation accepted"); }
            catch (InvalidDataException) { }
        });
    await Pure("compiled content mutation rejected", () => {
        var rejected = false;
        try { VerifyExtraction(Encoding.UTF8.GetBytes(source), Encoding.UTF8.GetBytes(compiled + " "), row); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected);
    });
    await Pure("invalid UTF8 rejected", () => {
        try { _ = VerifiedSource([0xff], row); throw new InvalidOperationException("Invalid UTF8 accepted"); }
        catch (DecoderFallbackException) { }
    });
    await Pure("read-only index checkout filters preserve source blob and extraction", () =>
        VerifyExtraction(CheckoutBytes(row.GetProperty("snapshot").GetString()!), CheckoutBytes(row.GetProperty("compiled").GetString()!), row));
}
var task = Guid.Parse("11111111-1111-4111-8111-111111111111");
var version = Guid.Parse("22222222-2222-4222-8222-222222222222");
var user = Guid.Parse("33333333-3333-4333-8333-333333333333");
var guardian = Guid.Parse("44444444-4444-4444-8444-444444444444");
var key = Guid.Parse("55555555-5555-4555-8555-555555555555");
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", user.ToString())], "synthetic"));
var evidence = new ParentEligibilityEvidence(ParentEligibilityEvidenceChecks.CandidateContractVersion, task, version,
    null, null, null, null, null, null, null, null);
var context = new ParentRequestContext(principal, user, version, task, key, version, guardian, evidence);
await Pure("exact eight Context fields and nullable optional Evidence", () => {
    var parameters = typeof(ParentRequestContext).GetConstructors().Single().GetParameters();
    Assert(parameters.Select(p => p.Name).SequenceEqual(new[] { "Principal", "ActorUserId", "OrganizationId", "CaseId", "ConsentId", "DocumentVersionId", "GuardianId", "Evidence" }));
    Assert(parameters[7].ParameterType == typeof(ParentEligibilityEvidence) && parameters[7].IsOptional && parameters[7].DefaultValue is null);
    Assert(new NullabilityInfoContext().Create(parameters[7]).ReadState == NullabilityState.Nullable);
});
await Pure("Evidence is the sole production contract, not a pinned duplicate", () =>
    Assert(typeof(ParentEligibilityEvidence).Assembly == typeof(ParentSubmitCommand).Assembly &&
        Assembly.GetExecutingAssembly().GetType(typeof(ParentEligibilityEvidence).FullName!) is null));
await Pure("Mapper preserves original Context, Principal and Evidence references", () => {
    var kept = ParentBridgeDtoMapper.PreserveContext(context);
    Assert(ReferenceEquals(kept, context) && ReferenceEquals(kept.Evidence, evidence) && ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(kept), principal));
});
await Pure("null Evidence and unknown generations stay unknown; no Stamp generation", () => {
    var absent = context with { Evidence = null };
    Assert(ParentBridgeDtoMapper.PreserveContext(absent).Evidence is null);
    Assert(evidence.BindingGeneration is null && evidence.Aba is null && evidence.EligibilityStamp is null);
    Assert(ParentEligibilityEvidenceChecks.FindMissingCandidateFields(evidence).Count > 0);
});
await Pure("Context IDs do not replace original Principal claims", () =>
    Assert(ReferenceEquals(ParentBridgeDtoMapper.GetOriginalPrincipal(context with { ActorUserId = guardian }), principal) && principal.FindFirst("userId")!.Value == user.ToString()));
var draft = new ParentDraftRequest(version, "0", null, null, null, [], null);
var submit = new ParentSubmitRequest(version, "7", "Synthetic respondent", "2026-11-06", "MOTHER", [new("Q1", "0")], null);
IParentQuestionnaireGateway bridge = new ParentGatewayBridgeSkeleton();
foreach (var method in typeof(IParentQuestionnaireGateway).GetMethods())
    await Pure("four-method signature " + method.Name, () => {
        var actual = typeof(ParentGatewayBridgeSkeleton).GetMethod(method.Name)!;
        Assert(actual.ReturnType == method.ReturnType && actual.GetParameters().Select(p => (p.Name, p.ParameterType, p.IsOptional)).SequenceEqual(method.GetParameters().Select(p => (p.Name, p.ParameterType, p.IsOptional))));
    });
await Pure("Submit key is separate Guid, not DTO RequestId or answer hash", () => {
    var p = typeof(IParentQuestionnaireGateway).GetMethod("SubmitAsync")!.GetParameters()[2];
    Assert(p.Name == "idempotencyKey" && p.ParameterType == typeof(Guid));
    Assert(typeof(ParentSubmitRequest).GetProperty("RequestId") is null && typeof(ParentSubmitCommand).GetProperty("IdempotencyKey") is null);
    Assert(typeof(ParentRequestContext).GetProperty("IdempotencyKey") is null);
});
foreach (var text in new[] { "0", "1", "9007199254740991", "9007199254740992", "18446744073709551615" })
    await Pure("Revision exact " + text, () => {
        Assert(ParentBridgeDtoMapper.MapDraft(draft with { ExpectedRevision = text }).Payload.ExpectedRevision!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) == text);
        Assert(ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = text }).ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) == text);
    });
foreach (var invalid in new string?[] { null, "", " ", "01", "+1", "-1", "１", "1.0", "18446744073709551616" })
    await Pure("invalid Revision rejected", () => {
        try { ParentBridgeDtoMapper.MapSubmit(submit with { ExpectedRevision = invalid! }); throw new InvalidOperationException(); }
        catch (EarlyInterventionCare.Api.Services.Cases.CaseAccessException e) { Assert(e.Code == "INVALID_INPUT" && e.Status == 400); }
    });
await Pure("null draft Relationship and explicit submit Relationship preserved", () => {
    Assert(ParentBridgeDtoMapper.MapDraft(draft).RespondentRelationship is null);
    var mapped = ParentBridgeDtoMapper.MapSubmit(submit);
    Assert(mapped.RespondentRelationship == "MOTHER" && mapped.ExpectedRevision == 7 && mapped.Payload.QuestionnaireVersionId == version && mapped.Payload.Answers!.Single() == submit.Answers.Single());
});
await Pure("missing submit Relationship rejected", () => {
    try { ParentBridgeDtoMapper.MapSubmit(submit with { RespondentRelationship = null! }); throw new InvalidOperationException(); }
    catch (EarlyInterventionCare.Api.Services.Cases.CaseAccessException e) { Assert(e.Status == 400); }
});
await Pure("Draft output preserves version, maximal Revision and null Relationship", () => {
    var core = new QuestionnaireDraftResponse(task, version, ulong.MaxValue, null, null, [], null, null, "PENDING");
    var mapped = ParentBridgeDtoMapper.MapDraftResult(new(core, null), task, version);
    Assert(mapped.TaskId == task && mapped.QuestionnaireVersionId == version && mapped.Revision == "18446744073709551615" && mapped.RespondentRelationship is null);
});
await Pure("receipt mapping preserves both replay flags and exact UTC", () => {
    foreach (var replay in new[] { false, true }) {
        var core = new ParentQuestionnaireReceipt(user, task, version, new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), "SUBMITTED", replay);
        var mapped = ParentBridgeDtoMapper.MapReceipt(core, task, version);
        Assert(mapped.ResponseId == user && mapped.TaskId == task && mapped.QuestionnaireVersionId == version && mapped.SubmittedAtUtc == core.SubmittedAtUtc && mapped.Replayed == replay);
    }
});
foreach (var candidate in new[] { context, context with { Evidence = null }, context with { ActorUserId = guardian }, context with { Principal = new ClaimsPrincipal() }, null! })
foreach (var name in new[] { "GetDraft", "SaveDraft", "Submit", "GetReceipt" })
    await Check(name + " always denies (no authorization/Store acceptance)", async () => {
        try {
            switch (name) {
                case "GetDraft": await bridge.GetDraftAsync(candidate, task); break;
                case "SaveDraft": await bridge.SaveDraftAsync(candidate, task, draft); break;
                case "Submit": await bridge.SubmitAsync(candidate, task, key, submit); break;
                default: await bridge.GetReceiptAsync(candidate, task); break;
            }
            throw new InvalidOperationException("Unexpected successful call");
        } catch (QuestionnaireWorkflowException e) { Assert(e.Code == "PARENT_ACCESS_NOT_INTEGRATED" && e.Status == 503); }
    });
await Pure("skeleton has no Store, state or injected forwarding dependency", () => {
    var type = typeof(ParentGatewayBridgeSkeleton);
    Assert(!type.IsPublic && type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Length == 0);
    Assert(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single().GetParameters().Length == 0);
    Assert(typeof(ParentSubmitCommand).Assembly.GetType(type.FullName!) is null);
});
const string secretMarker = "SYNTHETIC_INTERNAL_DETAIL";
void SafeResult(ObjectResult result, int status, string code)
{
    Assert(result.StatusCode == status);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
    Assert(json.RootElement.GetProperty("code").GetString() == code && json.RootElement.GetProperty("traceId").GetString() == "synthetic-trace");
    Assert(!json.RootElement.GetRawText().Contains(secretMarker, StringComparison.Ordinal));
    Assert(json.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "code", "message", "traceId" }));
}
foreach (var (code, status) in new[] { ("TASK_NOT_FOUND", 404), ("RESPONSE_NOT_FOUND", 404), ("DRAFT_CONFLICT", 409), ("IDEMPOTENCY_CONFLICT", 409), ("ACCESS_PRECHECK_CHANGED", 409), ("IDEMPOTENCY_VERSION_UNSUPPORTED", 409), ("PARENT_ACCESS_NOT_INTEGRATED", 503), ("STORAGE_INCONSISTENT", 503) })
    await Pure("real error mapper " + code, () => SafeResult(ParentCoreHttpErrors.Result(new(code, secretMarker, status), "synthetic-trace"), status, code));
foreach (var (code, status) in new[] { ("UNKNOWN_INTERNAL_CODE", 500), ("TASK_NOT_FOUND", 409), ("IDEMPOTENCY_CONFLICT", 200) })
    await Pure("unknown/mismatched core error safely maps to 503", () => SafeResult(ParentCoreHttpErrors.Result(new(code, secretMarker, status), "synthetic-trace"), 503, "PARENT_CORE_UNAVAILABLE"));
ExceptionContext ErrorContext(Exception exception)
{
    var http = new DefaultHttpContext(); http.Request.Path = "/api/parent/questionnaires/synthetic"; http.TraceIdentifier = "synthetic-trace";
    return new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = exception };
}
await Pure("real Filter handles workflow exceptions and no-store", () => {
    var ctx = ErrorContext(new QuestionnaireWorkflowException("TASK_NOT_FOUND", secretMarker, 404));
    new ParentModuleErrors().OnException(ctx);
    Assert(ctx.ExceptionHandled && ctx.HttpContext.Response.Headers.CacheControl == "no-store");
    SafeResult((ObjectResult)ctx.Result!, 404, "TASK_NOT_FOUND");
});
await Pure("real Filter handles storage timeout safely", () => {
    var ctx = ErrorContext(new TimeoutException(secretMarker)); new ParentModuleErrors().OnException(ctx);
    Assert(ctx.ExceptionHandled); SafeResult((ObjectResult)ctx.Result!, 503, "SERVICE_UNAVAILABLE");
});
// The module Filter intentionally delegates arbitrary failures to the outer pipeline.
// This assertion does NOT prove that a safe outer handler exists.
foreach (var exception in new Exception[] { new InvalidOperationException(secretMarker), new ArgumentException(secretMarker) })
    await Pure("Filter preserves unexpected exception for outer pipeline without translation", () => {
        var ctx = ErrorContext(exception);
        var originalStatus = ctx.HttpContext.Response.StatusCode;
        new ParentModuleErrors().OnException(ctx);
        Assert(ReferenceEquals(ctx.Exception, exception) && !ctx.ExceptionHandled && ctx.Result is null);
        Assert(ctx.HttpContext.Response.StatusCode == originalStatus && !ctx.HttpContext.Response.HasStarted);
        Assert(!ctx.HttpContext.Response.Headers.ContainsKey("Cache-Control"));
    });
var pipeline = PipelineSafetyReadiness.Inspect();
await Pure("Pipeline source inventory matches reviewed baseline (NOT safety acceptance)", () => Assert(pipeline.BaselineMatches));
await Pure("Pipeline inventory identifies missing general handler and ProblemDetails registration", () =>
    Assert(!pipeline.HasExplicitGeneralHandler && !pipeline.HasProblemDetailsRegistration && pipeline.PermissionHandlerIsSpecific));
var blocked = new[] {
    "Authoritative generations/ABA/Stamp and shared qualification lock/revalidation protocol are not implemented",
    "Formal Bridge forwarding, Controller/DI wiring and real Guardian/Consent authorization remain disabled",
    "MySQL atomicity, Schema/Migration and HTTP pipeline end-to-end acceptance not performed",
    "DTO has no RequestId; no agreed RequestId-to-Idempotency-Key conversion may be invented",
    pipeline.BlockedReason
};
foreach (var b in blocked) Console.WriteLine("BLOCKED " + b);
Console.WriteLine($"RESULT: {pass} PASS, {fail} FAIL, {blocked.Length} BLOCKED; offline contract only");
return fail > 0 ? 1 : 2;
