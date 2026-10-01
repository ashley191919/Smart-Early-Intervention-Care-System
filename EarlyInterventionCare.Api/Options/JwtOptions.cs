using System.Globalization;

namespace EarlyInterventionCare.Api.Options;

public sealed class JwtOptions
{
    public string SigningKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 30;

    public static bool TryLoad(IConfiguration configuration, out JwtOptions options, out byte[] signingKey,
        out string error)
    {
        options = new JwtOptions();
        signingKey = Array.Empty<byte>();
        error = string.Empty;
        var encodedKey = configuration["Jwt:SigningKey"];
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(encodedKey))
            error = "Jwt:SigningKey is required.";
        else if (string.IsNullOrWhiteSpace(issuer))
            error = "Jwt:Issuer is required.";
        else if (string.IsNullOrWhiteSpace(audience))
            error = "Jwt:Audience is required.";
        else if (!int.TryParse(configuration["Jwt:ExpirationMinutes"], NumberStyles.None,
                     CultureInfo.InvariantCulture, out var minutes) || minutes != 30)
            error = "Jwt:ExpirationMinutes must be 30 in this phase.";

        if (error.Length > 0)
            return false;

        try
        {
            signingKey = Convert.FromBase64String(encodedKey!);
        }
        catch (FormatException)
        {
            error = "Jwt:SigningKey must be valid Base64.";
            return false;
        }

        if (signingKey.Length < 32)
        {
            error = "Jwt:SigningKey must decode to at least 32 bytes.";
            signingKey = Array.Empty<byte>();
            return false;
        }

        options = new JwtOptions
        {
            SigningKey = encodedKey!,
            Issuer = issuer!,
            Audience = audience!,
            ExpirationMinutes = 30
        };
        return true;
    }
}
