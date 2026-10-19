using System.Globalization;
using EarlyInterventionCare.Api.Services.Cases;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EarlyInterventionCare.Api.Services.Email;

public sealed class ParentEmailOptions
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "早療智慧動態照顧系統";
}

public interface IParentEmailSender
{
    // Only the trusted OTP service may supply a code. Delivery is not verification.
    Task SendCodeAsync(string email, string code, int validMinutes, CancellationToken ct = default);
}

public interface IParentEmailTransport
{
    Task SendAsync(ParentEmailOptions options, MimeMessage message, CancellationToken ct);
}

public sealed class MailKitParentEmailTransport : IParentEmailTransport
{
    public async Task SendAsync(ParentEmailOptions options, MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient { Timeout = 15000 };
        // Require TLS, retaining the platform certificate validation. No plaintext fallback.
        await client.ConnectAsync(options.SmtpHost, options.SmtpPort,
            options.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(options.Username, options.Password, ct);
        await client.SendAsync(message, ct);
        // A QUIT failure after acceptance must not prompt an automatic duplicate send.
        try { await client.DisconnectAsync(true, ct); }
        catch (Exception) { /* Delivery was already accepted; no protocol logging. */ }
    }
}

public sealed class ParentEmailSender(IOptions<ParentEmailOptions> configured, IParentEmailTransport transport)
    : IParentEmailSender
{
    public async Task SendCodeAsync(string email, string code, int validMinutes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var options = configured.Value;
        if (string.IsNullOrWhiteSpace(options.SmtpHost) || options.SmtpHost.Any(char.IsWhiteSpace) ||
            options.SmtpPort is not (587 or 465) || string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.Password) || !TryAddress(options.FromAddress, out var from) ||
            options.FromName.Length > 100 || options.FromName.Any(char.IsControl))
            throw Unavailable();

        if (!TryAddress(email, out var recipient) || code is null || code.Length != 6 ||
            code.Any(c => c is < '0' or > '9') || validMinutes is < 1 or > 10)
            throw new CaseAccessException("INVALID_INPUT", 400, "請確認電子郵件及驗證資料。");

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, from!.Address));
        message.To.Add(recipient!);
        message.Subject = "早療智慧動態照顧系統：登入驗證碼";
        message.Body = new TextPart("plain")
        {
            Text = $"您的登入驗證碼是：{code}\n\n有效時間為 {validMinutes.ToString(CultureInfo.InvariantCulture)} 分鐘。\n請勿將驗證碼提供給他人。若非您本人操作，請忽略此信件。"
        };
        try
        {
            // One attempt only; SMTP can fail after the server has accepted a message.
            await transport.SendAsync(options, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // SMTP exceptions can contain credentials, recipients or server replies.
            // Do not attach an inner exception or expose/log the original message.
            throw Unavailable();
        }
    }

    private static bool TryAddress(string? value, out MailboxAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsControl)) return false;
        var trimmed = value.Trim();
        return MailboxAddress.TryParse(trimmed, out address) && address is not null && string.IsNullOrEmpty(address.Name) &&
            address.Address == trimmed && trimmed.Contains('@');
    }

    private static CaseAccessException Unavailable() =>
        new("EMAIL_SERVICE_UNAVAILABLE", 503, "登入服務暫時無法使用，請稍後重試。");
}
