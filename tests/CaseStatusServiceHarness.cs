using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services.Cases;

// Offline service tests: the fake transaction serializes one case without EF, MySQL, secrets, or a web host.
var checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}

async Task Error(Func<Task> action, string code, int status)
{
    try { await action(); }
    catch (CaseStatusOperationException ex) { Check(ex.Code == code && ex.Status == status, code); return; }
    throw new Exception("Expected " + code);
}

(FakeStore Store, FakeActor Actor, FixedClock Clock, CaseStatusService Service) Fixture(string status = CaseStatusPolicy.New)
{
    var store = new FakeStore(status);
    var actor = new FakeActor(Guid.NewGuid());
    var clock = new FixedClock();
    return (store, actor, clock, new CaseStatusService(store, actor, clock));
}

{
    var f = Fixture();
    var result = await f.Service.ChangeManuallyAsync(f.Store.CaseId, CaseStatusPolicy.ToContact, "已開始聯繫");
    Check(result.OldStatus == CaseStatusPolicy.New && result.NewStatus == CaseStatusPolicy.ToContact &&
        result.ChangedByUserId == f.Actor.UserId, "manual change returns trusted actor and statuses");
    Check(f.Store.Case!.CaseStatus == CaseStatusPolicy.ToContact &&
        f.Store.Case.UpdatedAtUtc == FixedClock.Now.UtcDateTime, "case changes at history UTC time");
    var history = f.Store.Histories.Single();
    Check(history.OldStatus == CaseStatusPolicy.New && history.NewStatus == CaseStatusPolicy.ToContact &&
        history.ChangedByUserId == f.Actor.UserId && history.TriggerSource == CaseStatusHistoryPolicy.Manual &&
        history.Reason == "已開始聯繫", "history is committed with manual source and reason");
}

{
    var f = Fixture(); f.Actor.UserId = null;
    await Error(() => f.Service.ChangeManuallyAsync(f.Store.CaseId, CaseStatusPolicy.ToContact, null),
        "AUTHENTICATED_USER_REQUIRED", 401);
    Check(f.Store.OpenCount == 0 && f.Store.Histories.Count == 0, "missing actor never opens or writes a case");
}

{
    var f = Fixture(); f.Store.ActorActive = false;
    await Error(() => f.Service.ChangeManuallyAsync(f.Store.CaseId, CaseStatusPolicy.ToContact, null),
        "AUTHENTICATED_USER_REQUIRED", 401);
    Check(f.Store.OpenCount == 0 && f.Store.Histories.Count == 0, "inactive actor never opens or writes a case");
}

{
    var f = Fixture();
    await Error(() => f.Service.ChangeManuallyAsync(Guid.Empty, CaseStatusPolicy.ToContact, null), "INVALID_INPUT", 400);
    await Error(() => f.Service.ChangeManuallyAsync(Guid.NewGuid(), CaseStatusPolicy.ToContact, null), "CASE_NOT_FOUND", 404);
}

foreach (var target in new[] { CaseStatusPolicy.Completed, CaseStatusPolicy.New, "UNKNOWN" })
{
    var f = Fixture();
    await Error(() => f.Service.ChangeManuallyAsync(f.Store.CaseId, target, null), "INVALID_STATUS_TRANSITION", 409);
    Check(f.Store.Case!.CaseStatus == CaseStatusPolicy.New && f.Store.Histories.Count == 0,
        "invalid transition leaves case and history unchanged");
}

{
    var f = Fixture(); f.Store.FailCommit = true;
    try { await f.Service.ChangeManuallyAsync(f.Store.CaseId, CaseStatusPolicy.ToContact, null); throw new Exception("failure expected"); }
    catch (IOException) { }
    Check(f.Store.Case!.CaseStatus == CaseStatusPolicy.New && f.Store.Histories.Count == 0,
        "failed transaction commits neither case nor history");
}

{
    var f = Fixture();
    async Task<int> Change()
    {
        try { await f.Service.ChangeManuallyAsync(f.Store.CaseId, CaseStatusPolicy.ToContact, null); return 200; }
        catch (CaseStatusOperationException ex) { return ex.Status; }
    }
    var results = await Task.WhenAll(Change(), Change());
    Check(results.Order().SequenceEqual(new[] { 200, 409 }) && f.Store.Histories.Count == 1 &&
        f.Store.Case!.CaseStatus == CaseStatusPolicy.ToContact, "serialized concurrent changes leave one history entry");
}

Console.WriteLine($"PASS: {checks} offline CaseStatusService checks; trusted actor, policy validation, atomic history and serialized concurrency.");

sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class FakeActor(Guid? userId) : ICaseStatusActorContext
{
    public Guid? UserId { get; set; } = userId;
    public Guid? GetAuthenticatedUserId() => UserId;
}

sealed class FakeStore : ICaseStatusStore
{
    private readonly SemaphoreSlim gate = new(1);
    public Guid CaseId { get; } = Guid.NewGuid();
    public CaseRecord? Case { get; private set; }
    public List<CaseStatusHistory> Histories { get; } = new();
    public bool ActorActive { get; set; } = true;
    public bool FailCommit { get; set; }
    public int OpenCount { get; private set; }

    public FakeStore(string status) => Case = new CaseRecord
    {
        CaseId = CaseId,
        CaseStatus = status,
        CreatedAtUtc = FixedClock.Now.UtcDateTime,
        UpdatedAtUtc = FixedClock.Now.UtcDateTime
    };

    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) => Task.FromResult(ActorActive && userId != Guid.Empty);

    public async Task<ICaseStatusTransaction?> OpenAsync(Guid caseId, CancellationToken ct)
    {
        OpenCount++;
        await gate.WaitAsync(ct);
        await Task.Yield();
        if (Case is null || caseId != CaseId) { gate.Release(); return null; }
        return new Transaction(this, Clone(Case));
    }

    private static CaseRecord Clone(CaseRecord source) => new()
    {
        CaseId = source.CaseId,
        CaseCode = source.CaseCode,
        ChildName = source.ChildName,
        BirthDate = source.BirthDate,
        Sex = source.Sex,
        CaseStatus = source.CaseStatus,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc
    };

    private sealed class Transaction(FakeStore owner, CaseRecord working) : ICaseStatusTransaction
    {
        private readonly List<CaseStatusHistory> pendingHistory = new();
        private bool committed;
        public CaseRecord Case => working;
        public void AddHistory(CaseStatusHistory history) => pendingHistory.Add(history);
        public Task CommitAsync(CancellationToken ct)
        {
            if (owner.FailCommit) { owner.FailCommit = false; throw new IOException("Synthetic commit failure."); }
            owner.Case = Clone(working);
            owner.Histories.AddRange(pendingHistory);
            committed = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (!committed) { /* Discard cloned Case and pending History. */ }
            owner.gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
