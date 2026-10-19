using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Email;

internal static class ParentEmailVerificationChecks
{
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var clock = new Clock();
        var mail = new Sender();
        var service = new ParentEmailVerification(mail, clock);
        var first = await service.RequestAsync("Parent@EXAMPLE.test");
        var firstCode = mail.Code;
        Check(first.ChallengeId.Length == 64 && first.ResendAfterSeconds == 60, "opaque challenge and cooldown");
        Check(first.ExpiresAtUtc == clock.GetUtcNow().AddMinutes(5), "five minute TTL");
        Check(mail.Email == "Parent@example.test" && mail.Minutes == 5 && firstCode.Length == 6, "original mailbox bound and server code");
        using (var serialized = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(first)))
            Check(serialized.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "ChallengeId", "ExpiresAtUtc", "ResendAfterSeconds" }), "challenge DTO only exposes intended fields");
        try { await service.RequestAsync("parent@example.test"); throw new Exception("cooldown expected"); }
        catch (CaseAccessException ex) { Check(ex.Status == 429 && mail.Count == 1, "case change cannot bypass cooldown"); }
        var verified = await service.VerifyAsync(first.ChallengeId, firstCode);
        Check(verified?.Email == "Parent@example.test", "trusted address from challenge only");
        Check(await service.VerifyAsync(first.ChallengeId, firstCode) == null, "one-time consumption");
        Check(await service.VerifyAsync(new string('a', 64), firstCode) == null, "unknown challenge");
        Check(await service.VerifyAsync("bad", firstCode) == null, "malformed challenge");

        clock.Advance(60);
        var expired = await service.RequestAsync("Parent@example.test");
        var expiredCode = mail.Code;
        clock.Advance(300);
        Check(await service.VerifyAsync(expired.ChallengeId, expiredCode) == null, "expiry boundary denied");
        var attempts = await service.RequestAsync("Parent@example.test");
        var attemptCode = mail.Code;
        for (var i=0; i<5; i++) Check(await service.VerifyAsync(attempts.ChallengeId, "bad") == null, "failed attempt");
        Check(await service.VerifyAsync(attempts.ChallengeId, attemptCode) == null, "five failures invalidate code");
        clock.Advance(60);
        var old = await service.RequestAsync("Parent@example.test");
        var oldCode = mail.Code;
        clock.Advance(60);
        var replacement = await service.RequestAsync("Parent@example.test");
        var replacementCode = mail.Code;
        Check(old.ChallengeId != replacement.ChallengeId, "new unpredictable challenge on resend");
        Check(await service.VerifyAsync(old.ChallengeId, oldCode) == null, "replacement invalidates old code");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => service.VerifyAsync(replacement.ChallengeId, replacementCode))));
        Check(concurrent.Count(r => r != null) == 1, "one concurrent verification wins");
        var other = await service.RequestAsync("other@example.test");
        var otherCode = mail.Code;
        Check(await new ParentEmailVerification(mail, clock).VerifyAsync(other.ChallengeId, otherCode) == null, "restart invalidates challenges");

        foreach (var address in new[] { "", "invalid", "Name <parent@example.test>", "a@example.test,b@example.test", "parent@example.test\r\nBcc: other@example.test" })
        {
            var before = mail.Count;
            try { await service.RequestAsync(address); throw new Exception("invalid address accepted"); }
            catch (CaseAccessException ex) { Check(ex.Code == "INVALID_INPUT" && before == mail.Count, "reject malformed address before delivery"); }
        }
        var failMail = new Sender { Fail=true };
        var failed = new ParentEmailVerification(failMail, clock);
        try { await failed.RequestAsync("a@example.test"); throw new Exception("send failure expected"); }
        catch (CaseAccessException ex) { Check(ex.Code == "EMAIL_SERVICE_UNAVAILABLE" && ex.InnerException == null, "safe send error"); }
        try { await failed.RequestAsync("a@example.test"); throw new Exception("cooldown expected"); }
        catch (CaseAccessException ex) { Check(ex.Status == 429 && failMail.Count == 1, "failed send does not allow spam retry"); }

        var pendingSender = new Sender { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var pending = new ParentEmailVerification(pendingSender, clock);
        var pendingRequest = pending.RequestAsync("pending@example.test");
        // While sending, the caller has no challenge and cannot verify early.
        Check(!pendingRequest.IsCompleted, "challenge not returned before delivery");
        pendingSender.Pending.SetResult();
        var ready = await pendingRequest;
        Check(await pending.VerifyAsync(ready.ChallengeId, pendingSender.Code) != null, "ready after accepted send");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var beforeCancel = mail.Count;
        try { await service.RequestAsync("cancel@example.test", cancel.Token); throw new Exception("cancel expected"); }
        catch (OperationCanceledException) { Check(beforeCancel == mail.Count, "cancel does not send"); }
        // Bounded cardinality: arbitrary addresses cannot grow the memory store forever.
        var bounded = new ParentEmailVerification(new Sender(), clock);
        for (var i=0; i<4096; i++) await bounded.RequestAsync($"p{i}@example.test");
        try { await bounded.RequestAsync("overflow@example.test"); throw new Exception("capacity expected"); }
        catch (CaseAccessException ex) { Check(ex.Status == 503, "bounded storage"); }
        clock.Advance(300);
        Check((await bounded.RequestAsync("overflow@example.test")).ChallengeId.Length == 64, "expired entries cleaned");
        Console.WriteLine($"PASS: {count} Email OTP core checks; test sender only, no real email/database/JWT.");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class Sender : IParentEmailSender
    {
        public int Count; public string Email=""; public string Code=""; public int Minutes;
        public bool Fail; public TaskCompletionSource? Pending;
        public Task SendCodeAsync(string email, string code, int validMinutes, CancellationToken ct = default)
        {
            Count++; Email=email; Code=code; Minutes=validMinutes;
            return Fail ? Task.FromException(new IOException("private transport details")) : Pending?.Task ?? Task.CompletedTask;
        }
    }
}
