using System.Threading.RateLimiting;
using System.Security.Cryptography;
using System.Buffers.Binary;
using Microsoft.AspNetCore.RateLimiting;

namespace EarlyInterventionCare.Api.Security;

/// <summary>One shared per-connection-IP budget for both password login routes.</summary>
public sealed class StaffLoginRateLimitPolicy : IRateLimiterPolicy<string>
{
    public const string Name = "staff-password-login";
    public const int DefaultPermitLimit = 60;
    public const int DefaultWindowSeconds = 60;
    public const int BucketCount = 4096;
    private readonly byte[] _bucketKey = RandomNumberGenerator.GetBytes(32);
    private readonly int _permitLimit;
    private readonly TimeSpan _window;

    public StaffLoginRateLimitPolicy(int permitLimit = DefaultPermitLimit, int windowSeconds = DefaultWindowSeconds)
    {
        if (permitLimit is < 1 or > 1000 || windowSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(permitLimit), "Login rate limit configuration is out of range.");
        _permitLimit = permitLimit;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    public RateLimitPartition<string> GetPartition(HttpContext context)
    {
        // Do not read X-Forwarded-For. A trusted proxy may set RemoteIpAddress only
        // after separately configured, trusted forwarded-header processing.
        var address = context.Connection.RemoteIpAddress;
        // A fixed keyed bucket space bounds the limiter's partition count even when
        // arbitrary addresses arrive. Collisions share a budget, never bypass it.
        var key = address is null ? "unknown" : GetBucket(address.MapToIPv4IfMapped());
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = _permitLimit,
            Window = _window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private string GetBucket(System.Net.IPAddress address)
    {
        var hash = HMACSHA256.HashData(_bucketKey, address.GetAddressBytes());
        return (BinaryPrimitives.ReadUInt32BigEndian(hash) % BucketCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            code = "LOGIN_RATE_LIMITED",
            message = "登入請求過於頻繁，請稍後再試。"
        }, cancellationToken);
    }
}

internal static class StaffLoginIpAddressExtensions
{
    public static System.Net.IPAddress MapToIPv4IfMapped(this System.Net.IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
