using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Middleware;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
DefaultHttpContext Context(bool started = false)
{
    var context = new DefaultHttpContext();
    if (started) context.Features.Set<IHttpResponseFeature>(new StartedResponse());
    context.Response.Body = new MemoryStream();
    context.TraceIdentifier = "synthetic-request-01";
    return context;
}
string Body(HttpContext ctx) => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
void Safe500(HttpContext ctx, string? trace = "synthetic-request-01")
{
    Assert(ctx.Response.StatusCode == 500 && ctx.Response.ContentType == "application/json; charset=utf-8");
    Assert(ctx.Response.Headers.CacheControl == "no-store");
    using var json = JsonDocument.Parse(Body(ctx));
    Assert(json.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "code", "message", "traceId" }));
    Assert(json.RootElement.GetProperty("code").GetString() == "INTERNAL_ERROR");
    Assert(json.RootElement.GetProperty("message").GetString() == "系統發生錯誤，請稍後再試。");
    Assert(json.RootElement.GetProperty("traceId").GetString() == (trace ?? ctx.TraceIdentifier));
}
async Task Propagates(Func<Task> action, Exception expected)
{
    try { await action(); throw new InvalidOperationException("Expected propagation"); }
    catch (Exception e) { Assert(ReferenceEquals(e, expected)); }
}
await Check("normal response remains byte-for-byte unchanged and next called once", async () => {
    var ctx = Context(); var count = 0;
    await new GlobalExceptionMiddleware(async c => {
        count++; c.Response.StatusCode = 201; c.Response.ContentType = "text/plain";
        c.Response.Headers["X-Synthetic"] = "kept"; await c.Response.WriteAsync("normal body");
    }).InvokeAsync(ctx);
    Assert(count == 1 && ctx.Response.StatusCode == 201 && ctx.Response.ContentType == "text/plain" && ctx.Response.Headers["X-Synthetic"] == "kept" && Body(ctx) == "normal body");
});
await Check("unknown exception produces exact safe 500 JSON", async () => {
    var ctx = Context();
    await new GlobalExceptionMiddleware(_ => throw new InvalidOperationException("SYNTHETIC_SECRET")).InvokeAsync(ctx);
    Safe500(ctx);
    Assert(!Body(ctx).Contains("SYNTHETIC_SECRET", StringComparison.Ordinal));
});
await Check("async unknown exception is also caught exactly once", async () => {
    var ctx = Context(); var count = 0;
    await new GlobalExceptionMiddleware(async _ => { count++; await Task.Yield(); throw new Exception("SYNTHETIC_SECRET"); }).InvokeAsync(ctx);
    Safe500(ctx); Assert(count == 1);
});
await Check("exception, inner detail, synthetic path and stack trace are never serialized", async () => {
    var ctx = Context(); var e = new InvalidOperationException("SYNTHETIC_SECRET /synthetic/internal/file", new Exception("SYNTHETIC_INNER"));
    await new GlobalExceptionMiddleware(_ => throw e).InvokeAsync(ctx);
    Safe500(ctx);
    foreach (var forbidden in new[] { "SYNTHETIC_SECRET", "SYNTHETIC_INNER", "/synthetic/internal/file", "InvalidOperationException", "stackTrace", "exception" })
        Assert(!Body(ctx).Contains(forbidden, StringComparison.Ordinal));
});
await Check("unstarted partial response and stale headers cleared before one JSON response", async () => {
    var ctx = Context();
    await new GlobalExceptionMiddleware(async c => {
        c.Response.StatusCode = 202; c.Response.Headers["X-Internal"] = "SYNTHETIC_SECRET";
        c.Response.ContentType = "text/plain"; c.Response.ContentLength = 999;
        await c.Response.WriteAsync("SYNTHETIC_PARTIAL"); throw new Exception("SYNTHETIC_SECRET");
    }).InvokeAsync(ctx);
    Safe500(ctx); Assert(!ctx.Response.Headers.ContainsKey("X-Internal") && !Body(ctx).Contains("SYNTHETIC_PARTIAL", StringComparison.Ordinal));
});
foreach (var trace in new[] { "", " " })
    await Check("missing server trace ID gets nonempty request-linked fallback", async () => {
        var ctx = Context(); ctx.TraceIdentifier = trace;
        await new GlobalExceptionMiddleware(_ => throw new Exception()).InvokeAsync(ctx);
        Assert(Guid.TryParseExact(ctx.TraceIdentifier, "N", out _)); Safe500(ctx, null);
    });
await Check("trace ID is not taken from client-supplied headers", async () => {
    var ctx = Context(); ctx.Request.Headers["X-Trace-Id"] = "SYNTHETIC_CLIENT_VALUE";
    await new GlobalExceptionMiddleware(_ => throw new Exception()).InvokeAsync(ctx);
    Safe500(ctx); Assert(!Body(ctx).Contains("SYNTHETIC_CLIENT_VALUE", StringComparison.Ordinal));
});
foreach (var status in new[] { 401, 403, 404, 409, 503 })
    await Check($"existing handled {status} response preserved unchanged", async () => {
        var ctx = Context();
        await new GlobalExceptionMiddleware(async c => {
            c.Response.StatusCode = status;
            await c.Response.WriteAsJsonAsync(new { code = "SYNTHETIC_KNOWN", message = "safe known response", traceId = c.TraceIdentifier });
        }).InvokeAsync(ctx);
        Assert(ctx.Response.StatusCode == status && Body(ctx).Contains("SYNTHETIC_KNOWN", StringComparison.Ordinal) && !Body(ctx).Contains("INTERNAL_ERROR", StringComparison.Ordinal));
    });
await Check("actual permission lookup Middleware keeps its 503 semantics downstream", async () => {
    var ctx = Context();
    var specific = new PermissionLookupExceptionMiddleware(_ => throw new PermissionLookupException());
    await new GlobalExceptionMiddleware(specific.InvokeAsync).InvokeAsync(ctx);
    Assert(ctx.Response.StatusCode == 503 && ctx.Response.Headers.CacheControl == "no-store");
    using var json = JsonDocument.Parse(Body(ctx));
    Assert(json.RootElement.GetProperty("code").GetString() == "AUTHORIZATION_UNAVAILABLE");
    Assert(!json.RootElement.TryGetProperty("traceId", out _)); // Existing gap preserved, not silently repaired.
});
foreach (var exception in new Exception[] { new QuestionnaireOperationException("VERSION_NOT_PUBLISHED", "safe known business message", 409), new TimeoutException("SYNTHETIC_SECRET") })
    await Check("actual known Filter result preserved downstream (direct composition, not MVC HTTP)", async () => {
        var ctx = Context();
        await new GlobalExceptionMiddleware(async c => {
            var ec = new ExceptionContext(new ActionContext(c, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = exception };
            new QuestionnaireErrorsAttribute().OnException(ec);
            Assert(ec.ExceptionHandled && ec.Result is ObjectResult);
            var result = (ObjectResult)ec.Result!;
            c.Response.StatusCode = result.StatusCode!.Value; await c.Response.WriteAsJsonAsync(result.Value);
        }).InvokeAsync(ctx);
        Assert(ctx.Response.StatusCode == (exception is TimeoutException ? 503 : 409));
        Assert(!Body(ctx).Contains("INTERNAL_ERROR", StringComparison.Ordinal) && !Body(ctx).Contains("SYNTHETIC_SECRET", StringComparison.Ordinal));
    });
await Check("unclassified timeout is 500, never guessed to be 503", async () => {
    var ctx = Context(); await new GlobalExceptionMiddleware(_ => throw new TimeoutException("SYNTHETIC_SECRET")).InvokeAsync(ctx); Safe500(ctx);
});
await Check("already-started unknown failure propagates original exception without rewriting", async () => {
    var ctx = Context(started: true); ctx.Response.StatusCode = 202; ctx.Response.Headers["X-Synthetic"] = "kept";
    await ctx.Response.WriteAsync("started body"); var before = Body(ctx); var e = new Exception("SYNTHETIC_SECRET");
    await Propagates(() => new GlobalExceptionMiddleware(_ => throw e).InvokeAsync(ctx), e);
    Assert(ctx.Response.StatusCode == 202 && ctx.Response.Headers["X-Synthetic"] == "kept" && Body(ctx) == before && !ctx.Response.Headers.ContainsKey("Cache-Control"));
});
foreach (var taskCanceled in new[] { false, true })
    await Check("request cancellation propagates original exception and writes nothing", async () => {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var ctx = Context(); ctx.RequestAborted = cts.Token;
        Exception e = taskCanceled ? new TaskCanceledException("SYNTHETIC_SECRET") : new OperationCanceledException(cts.Token);
        await Propagates(() => new GlobalExceptionMiddleware(_ => throw e).InvokeAsync(ctx), e);
        Assert(ctx.Response.StatusCode == 200 && Body(ctx) == "" && !ctx.Response.Headers.ContainsKey("Cache-Control"));
    });
await Check("normal cancelled request is not forced into an error response", async () => {
    using var cts = new CancellationTokenSource(); cts.Cancel(); var ctx = Context(); ctx.RequestAborted = cts.Token;
    await new GlobalExceptionMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx);
    Assert(ctx.Response.StatusCode == 200 && Body(ctx) == "");
});
await Check("exception after request abort is not masked as internal error", async () => {
    using var cts = new CancellationTokenSource(); cts.Cancel(); var ctx = Context(); ctx.RequestAborted = cts.Token;
    var e = new IOException("SYNTHETIC_SECRET");
    await Propagates(() => new GlobalExceptionMiddleware(_ => throw e).InvokeAsync(ctx), e);
    Assert(Body(ctx) == "");
});
await Check("unrelated OperationCanceledException without request abort is generic 500", async () => {
    var ctx = Context(); await new GlobalExceptionMiddleware(_ => throw new OperationCanceledException()).InvokeAsync(ctx); Safe500(ctx);
});
await Check("response write failure propagates and is not retried", async () => {
    var ctx = Context(); var e = new IOException("SYNTHETIC_WRITE_FAILURE"); var stream = new FailingStream(() => e); ctx.Response.Body = stream;
    await Propagates(() => new GlobalExceptionMiddleware(_ => throw new Exception()).InvokeAsync(ctx), e);
    Assert(stream.Attempts == 1);
});
await Check("cancellation during error response write propagates without a second response", async () => {
    using var cts = new CancellationTokenSource(); var ctx = Context(); ctx.RequestAborted = cts.Token;
    var e = new OperationCanceledException(cts.Token);
    var stream = new FailingStream(() => { cts.Cancel(); return e; }); ctx.Response.Body = stream;
    await Propagates(() => new GlobalExceptionMiddleware(_ => throw new Exception()).InvokeAsync(ctx), e);
    Assert(cts.IsCancellationRequested && stream.Attempts == 1);
});
await Check("candidate has only RequestDelegate dependency and no auto-registration", () => {
    Assert(typeof(GlobalExceptionMiddleware).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(RequestDelegate) }));
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var program = File.ReadAllText(Path.Combine(root, "EarlyInterventionCare.Api/Program.cs"));
    Assert(!program.Contains("GlobalExceptionMiddleware", StringComparison.Ordinal));
    return Task.CompletedTask;
});
var blocked = new[] {
    "Candidate is NOT registered: real HTTP/MVC/filter/authentication pipeline order and environment behavior remain unaccepted",
    "INTERNAL_ERROR literal/message and server trace correlation/logging policy need final shared review before registration; no exception details logged by this candidate",
    "Existing specialized 503/business responses are deliberately untouched and may still lack traceId; common format harmonization remains pending",
    "Request-abort/response-started tests use synthetic HttpContext/features/streams, not real Kestrel disconnect/partial response acceptance"
};
foreach (var b in blocked) Console.WriteLine("BLOCKED " + b);
Console.WriteLine($"RESULT: {pass} PASS, {fail} FAIL, {blocked.Length} BLOCKED; no host/socket/database; middleware remains unregistered");
return fail > 0 ? 1 : 2;

sealed class StartedResponse : HttpResponseFeature { public override bool HasStarted => true; }
sealed class FailingStream(Func<Exception> failure) : MemoryStream
{
    public int Attempts { get; private set; }
    private Exception Fail() { Attempts++; return failure(); }
    public override void Write(byte[] buffer, int offset, int count) => throw Fail();
    public override void Write(ReadOnlySpan<byte> buffer) => throw Fail();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromException(Fail());
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => ValueTask.FromException(Fail());
}
