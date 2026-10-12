using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

async Task Error(Func<Task> action, string code, int status)
{
    try { await action(); }
    catch (CaseStatusHistoryQueryOperationException ex)
    {
        Check(ex.Code == code && ex.Status == status, message: code);
        return;
    }
    throw new Exception("Expected " + code);
}

var caseId = Guid.Parse("10000000-0000-4000-8000-000000000001");
var data = new[]
{
    Entry("10000000-0000-4000-8000-000000000001", 10),
    Entry("20000000-0000-4000-8000-000000000001", 10),
    Entry("30000000-0000-4000-8000-000000000001", 9),
    Entry("40000000-0000-4000-8000-000000000001", 8),
};
var store = new FakeStore(caseId, data);
var service = new CaseStatusHistoryQueryService(store);

{
    var page = await service.GetPageAsync(caseId, page: 1, pageSize: 2);
    Check(page.TotalCount == 4 && page.Items.Count == 2, "returns total count and requested page size");
    Check(page.Items.Select(item => item.HistoryId).SequenceEqual(new[] { data[1].HistoryId, data[0].HistoryId }),
        "sorts newest first then HistoryId descending");
    var second = await service.GetPageAsync(caseId, page: 2, pageSize: 2);
    Check(second.Items.Select(item => item.HistoryId).SequenceEqual(new[] { data[2].HistoryId, data[3].HistoryId }),
        "returns deterministic second page");
}

await Error(() => service.GetPageAsync(Guid.Empty), "INVALID_INPUT", 400);
await Error(() => service.GetPageAsync(caseId, page: 0), "INVALID_INPUT", 400);
await Error(() => service.GetPageAsync(caseId, pageSize: CaseStatusHistoryQueryService.MaximumPageSize + 1), "INVALID_INPUT", 400);
await Error(() => service.GetPageAsync(Guid.NewGuid()), "CASE_NOT_FOUND", 404);

CaseStatusHistoryController Controller(ICaseStatusHistoryAccessAuthorizer authorizer) => new(service, authorizer)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
};

{
    var controller = Controller(new DenyAllCaseStatusHistoryAccessAuthorizer());
    var response = await controller.GetHistory(caseId);
    var denied = response.Result as ObjectResult;
    Check(denied?.StatusCode == 403 && store.QueryCount == 3, "fail-closed access never reads history");
}

{
    var controller = Controller(new DenyAllCaseStatusHistoryAccessAuthorizer());
    var response = await controller.GetHistory(Guid.Empty);
    Check((response.Result as ObjectResult)?.StatusCode == 400, "empty CaseId is rejected before authorization");
}

{
    var controller = Controller(new AllowAuthorizer(canViewReason: false));
    var response = await controller.GetHistory(caseId, page: 1, pageSize: 1);
    var ok = response.Result as OkObjectResult;
    var body = ok?.Value as CaseStatusHistoryPageResponse;
    Check(ok?.StatusCode == 200 && body?.Items.Single().Reason is null, "authorized caller without reason scope gets no reason");

    controller = Controller(new AllowAuthorizer(canViewReason: true));
    response = await controller.GetHistory(caseId, page: 1, pageSize: 1);
    body = (response.Result as OkObjectResult)?.Value as CaseStatusHistoryPageResponse;
    Check(body?.Items.Single().Reason == "原因", "authorized reason scope receives reason");
}

Check(typeof(CaseStatusHistoryController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Length == 1,
    "history controller requires authentication");
Console.WriteLine($"PASS: {checks} offline CaseStatusHistory query checks; pagination, ordering and fail-closed access.");

CaseStatusHistoryData Entry(string id, int second) => new(Guid.Parse(id), caseId, "NEW", "TO_CONTACT",
    new DateTime(2026, 10, 12, 8, 0, second, DateTimeKind.Utc), "MANUAL", "原因");

sealed class FakeStore(Guid existingCaseId, IReadOnlyList<CaseStatusHistoryData> records) : ICaseStatusHistoryStore
{
    public int QueryCount { get; private set; }
    public Task<CaseStatusHistoryPageData?> GetPageAsync(Guid requestedCaseId, int page, int pageSize, CancellationToken ct)
    {
        QueryCount++;
        if (requestedCaseId != existingCaseId) return Task.FromResult<CaseStatusHistoryPageData?>(null);
        var ordered = records.OrderByDescending(item => item.ChangedAtUtc).ThenByDescending(item => item.HistoryId).ToArray();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult<CaseStatusHistoryPageData?>(new(existingCaseId, ordered.Length, items));
    }
}

sealed class AllowAuthorizer(bool canViewReason) : ICaseStatusHistoryAccessAuthorizer
{
    public Task<CaseStatusHistoryAccessDecision> AuthorizeReadAsync(System.Security.Claims.ClaimsPrincipal user,
        Guid requestedCaseId, CancellationToken ct) => Task.FromResult(CaseStatusHistoryAccessDecision.Granted(canViewReason));
}
