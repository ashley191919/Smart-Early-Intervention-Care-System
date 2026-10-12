using System.Text.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

CaseStatusController Controller(FakeStore store, ICaseStatusAccessAuthorizer authorizer)
{
    var actor = new FakeActor(Guid.NewGuid());
    var service = new CaseStatusService(store, actor, new FixedClock());
    return new CaseStatusController(service, authorizer)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
}

{
    var store = new FakeStore();
    var controller = Controller(store, new DenyAllCaseStatusAccessAuthorizer());
    var response = await controller.ChangeStatus(store.CaseId,
        new ChangeCaseStatusRequest { NewStatus = CaseStatusPolicy.ToContact, Reason = "測試" }, default);
    var denied = response.Result as ObjectResult;
    Check(denied?.StatusCode == 403, "default authorizer fails closed");
    Check(store.OpenCount == 0 && store.Histories.Count == 0, "denied request never writes");
}

{
    var store = new FakeStore();
    var controller = Controller(store, new AllowAuthorizer());
    var response = await controller.ChangeStatus(store.CaseId,
        new ChangeCaseStatusRequest { NewStatus = CaseStatusPolicy.ToContact, Reason = "已開始聯繫" }, default);
    var ok = response.Result as OkObjectResult;
    var body = ok?.Value as ChangeCaseStatusResponse;
    Check(ok?.StatusCode == 200 && body?.NewStatus == CaseStatusPolicy.ToContact, "authorized request maps service result");
    Check(store.Case.CaseStatus == CaseStatusPolicy.ToContact && store.Histories.Single().ChangedByUserId != Guid.Empty,
        "controller cannot supply an untrusted actor id");
}

{
    var store = new FakeStore();
    var controller = Controller(store, new AllowAuthorizer());
    try
    {
        await controller.ChangeStatus(store.CaseId,
            new ChangeCaseStatusRequest { NewStatus = CaseStatusPolicy.Completed }, default);
        throw new Exception("expected policy error");
    }
    catch (CaseStatusOperationException ex)
    {
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>()) { Exception = ex };
        new CaseStatusErrorsAttribute().OnException(context);
        var error = context.Result as ObjectResult;
        Check(error?.StatusCode == 409 && context.ExceptionHandled, "service conflict maps to standard error response");
    }
}

{
    var properties = typeof(ChangeCaseStatusRequest).GetProperties().Select(property => property.Name).Order().ToArray();
    Check(properties.SequenceEqual(new[] { "NewStatus", "Reason" }), "request DTO exposes only permitted fields");
    try
    {
        JsonSerializer.Deserialize<ChangeCaseStatusRequest>("{\"newStatus\":\"TO_CONTACT\",\"changedByUserId\":\"00000000-0000-0000-0000-000000000001\"}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        throw new Exception("unmapped input should be rejected");
    }
    catch (JsonException) { Check(true, "untrusted audit fields are rejected by JSON binding"); }
}

Check(typeof(CaseStatusController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Length == 1,
    "controller requires authentication");
Console.WriteLine($"PASS: {checks} offline CaseStatusController checks; fail-closed access, DTO boundary and error mapping.");

sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
}

sealed class FakeActor(Guid userId) : ICaseStatusActorContext
{
    public Guid? GetAuthenticatedUserId() => userId;
}

sealed class AllowAuthorizer : ICaseStatusAccessAuthorizer
{
    public Task<CaseStatusAccessDecision> AuthorizeChangeAsync(System.Security.Claims.ClaimsPrincipal user,
        Guid caseId, CancellationToken ct) => Task.FromResult(CaseStatusAccessDecision.Granted());
}

sealed class FakeStore : ICaseStatusStore
{
    public Guid CaseId { get; } = Guid.NewGuid();
    public CaseRecord Case { get; } = new() { CaseStatus = CaseStatusPolicy.New };
    public List<CaseStatusHistory> Histories { get; } = new();
    public int OpenCount { get; private set; }

    public FakeStore() => Case.CaseId = CaseId;
    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
    public Task<ICaseStatusTransaction?> OpenAsync(Guid caseId, CancellationToken ct)
    {
        OpenCount++;
        return Task.FromResult<ICaseStatusTransaction?>(caseId == CaseId ? new Transaction(this) : null);
    }

    private sealed class Transaction(FakeStore owner) : ICaseStatusTransaction
    {
        public CaseRecord Case => owner.Case;
        public void AddHistory(CaseStatusHistory history) => owner.Histories.Add(history);
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
