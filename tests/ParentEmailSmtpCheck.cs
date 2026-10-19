using EarlyInterventionCare.Api.Services.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

// Explicit opt-in network diagnostic. Sending requires --send-test-to-self.
// Never builds the website host, accesses a DB, or prints secrets/SMTP details.
internal static class ParentEmailSmtpCheck
{
    public static async Task<int> RunAsync(bool sendTestToSelf = false)
    {
        ParentEmailOptions settings;
        try
        {
            var config = new ConfigurationBuilder()
                .AddUserSecrets(typeof(ParentEmailSender).Assembly, optional: true).Build();
            try { settings = config.GetSection("Email").Get<ParentEmailOptions>() ?? new(); }
            finally { (config as IDisposable)?.Dispose(); }
        }
        catch (Exception)
        {
            Console.WriteLine("FAIL: Could not read User Secrets JSON. No settings were printed.");
            return 1;
        }
        // This diagnostic is deliberately Gmail-only; do not send these credentials
        // to another server if a config entry was accidentally edited.
        if (!string.Equals(settings.SmtpHost, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) ||
            settings.SmtpPort is not (587 or 465) || string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.Password) ||
            !string.Equals(settings.Username, settings.FromAddress, StringComparison.OrdinalIgnoreCase) ||
            settings.FromAddress.Any(char.IsControl) ||
            !MailboxAddress.TryParse(settings.FromAddress, out var mailbox) || mailbox is null ||
            !string.IsNullOrEmpty(mailbox.Name) || mailbox.Address != settings.FromAddress)
        {
            Console.WriteLine("FAIL: Check Email settings: Gmail host, port 587/465, credentials and matching FromAddress.");
            return 1;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var client = new SmtpClient { Timeout = 15000 };
        var stage = "TLS_CONNECTION";
        try
        {
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort,
                settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                timeout.Token);
            if (!client.IsSecure) throw new InvalidOperationException();
            Console.WriteLine("PASS: Gmail encrypted SMTP connection.");
            stage = "AUTHENTICATION";
            await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
            Console.WriteLine("PASS: Gmail SMTP authentication. No email sent; no database accessed.");
            if (sendTestToSelf)
            {
                stage = "SENDING_TEST";
                using var message = new MimeMessage();
                message.From.Add(new MailboxAddress("早療智慧動態照顧系統", mailbox.Address));
                message.To.Add(new MailboxAddress("", mailbox.Address));
                message.Subject = "早療智慧動態照顧系統：寄信連線測試";
                message.Body = new TextPart("plain") { Text = "這是一封由您手動執行的寄信測試。\n\n若您收到這封信，代表系統可以寄信到此信箱。\n此信不是登入驗證碼，不會建立帳號、綁定孩子或修改資料庫。" };
                // Exactly one attempt, to the configured sender's own mailbox only.
                await client.SendAsync(message, timeout.Token);
                Console.WriteLine("PASS: Gmail accepted one test email to the sender's own mailbox. Check inbox/spam to confirm receipt.");
                stage = "ACCEPTED";
            }
            try { await client.DisconnectAsync(true, timeout.Token); }
            catch (Exception) { /* Authentication result is already known. */ }
            return 0;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            Console.WriteLine("FAIL: Gmail rejected authentication. Check the full Gmail address and app password (without display spaces).");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(stage == "SENDING_TEST"
                ? "FAIL: Send result uncertain after timeout. Check inbox/spam before retrying; no automatic resend."
                : $"FAIL: {stage} timed out. No email sent.");
        }
        catch (Exception)
        {
            Console.WriteLine(stage == "SENDING_TEST"
                ? "FAIL: Test send did not complete normally. Check inbox/spam before retrying; no automatic resend or SMTP details printed."
                : $"FAIL: {stage}. Check network/TLS or Gmail settings. No SMTP details or credentials printed.");
        }
        return 1;
    }
}
