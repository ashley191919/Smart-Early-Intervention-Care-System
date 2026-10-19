using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Email;
using Microsoft.Extensions.Options;
using MimeKit;

if (args.Contains("--check-smtp") && args.Contains("--send-test-to-self"))
{
    Console.WriteLine("FAIL: Select either --check-smtp or --send-test-to-self.");
    Environment.ExitCode = 1;
    return;
}
if (args.Contains("--check-smtp") || args.Contains("--send-test-to-self"))
{
    Environment.ExitCode = await ParentEmailSmtpCheck.RunAsync(args.Contains("--send-test-to-self"));
    return;
}

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
ParentEmailOptions Settings() => new() { SmtpHost="smtp.example.test", SmtpPort=587,
    Username="sender@example.test", Password="test-only-secret", FromAddress="sender@example.test" };
async Task Denied(ParentEmailOptions settings, string recipient, string code, int minutes, string error)
{
    var transport = new CaptureTransport();
    try { await new ParentEmailSender(Options.Create(settings), transport).SendCodeAsync(recipient, code, minutes); throw new Exception("accepted invalid input"); }
    catch (CaseAccessException ex) { Check(ex.Code == error && transport.Count == 0, "deny before SMTP"); }
}
foreach (var mutate in new Action<ParentEmailOptions>[] {
    s=>s.Password="", s=>s.Username="", s=>s.SmtpHost="", s=>s.SmtpHost="smtp.test\r\n",
    s=>s.SmtpPort=25, s=>s.SmtpPort=0, s=>s.FromAddress="invalid",
    s=>s.FromAddress="sender@example.test\r\nBcc: other@example.test", s=>s.FromName="hospital\nheader"
}) {
    var settings = Settings(); mutate(settings);
    await Denied(settings, "parent@example.test", "123456", 5, "EMAIL_SERVICE_UNAVAILABLE");
}
foreach (var recipient in new[] { "", "not-email", "one@example.test,two@example.test", "Name <parent@example.test>", "parent@example.test\r\nBcc: other@example.test" })
    await Denied(Settings(), recipient, "123456", 5, "INVALID_INPUT");
foreach (var code in new[] { "12345", "1234567", "１２３４５６", "12\n456", "abcdef" })
    await Denied(Settings(), "parent@example.test", code, 5, "INVALID_INPUT");
foreach (var minutes in new[] { 0, 11 })
    await Denied(Settings(), "parent@example.test", "123456", minutes, "INVALID_INPUT");

var capture = new CaptureTransport();
await new ParentEmailSender(Options.Create(Settings()), capture).SendCodeAsync("parent@example.test", "012345", 5);
Check(capture.Count == 1 && capture.To == "parent@example.test", "one intended recipient");
Check(capture.Text.Contains("012345") && capture.Text.Contains("5 分鐘"), "code preserves leading zero and expiry");
Check(!capture.Text.Contains("test-only-secret") && !capture.Text.Contains("<html"), "no credential in plaintext body");
var failing = new CaptureTransport { Fail = true };
try { await new ParentEmailSender(Options.Create(Settings()), failing).SendCodeAsync("parent@example.test", "123456", 5); throw new Exception("failure expected"); }
catch (CaseAccessException ex) {
    Check(ex.Status == 503 && ex.Code == "EMAIL_SERVICE_UNAVAILABLE", "safe transport failure");
    Check(ex.InnerException == null && !ex.ToString().Contains("sensitive"), "no leaked SMTP detail");
    Check(failing.Count == 1, "no automatic resend");
}
var cancelled = new CaptureTransport();
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
try { await new ParentEmailSender(Options.Create(Settings()), cancelled).SendCodeAsync("parent@example.test", "123456", 5, cancellation.Token); throw new Exception("cancellation expected"); }
catch (OperationCanceledException) { Check(cancelled.Count == 0, "cancel before delivery"); }
Console.WriteLine($"PASS: {checks} Email delivery checks; no SMTP connection, email, database or secrets used.");
await ParentEmailVerificationChecks.RunAsync();

sealed class CaptureTransport : IParentEmailTransport
{
    public int Count; public bool Fail; public string To = ""; public string Text = "";
    public Task SendAsync(ParentEmailOptions options, MimeMessage message, CancellationToken ct)
    {
        Count++; To = message.To.Mailboxes.Single().Address; Text = message.TextBody ?? "";
        return Fail ? Task.FromException(new IOException("sensitive SMTP detail")) : Task.CompletedTask;
    }
}
