using Microsoft.AspNetCore.DataProtection;

namespace EarlyInterventionCare.Api.Options;

public static class LocalIsolationRuntime
{
    // No disk key ring, DPAPI, or Windows Event Log in the disposable test host.
    // Protected tokens deliberately expire when the process exits.
    public static void Configure(WebApplicationBuilder builder)
    {
        if (builder.Environment.EnvironmentName != LocalIsolationSettings.EnvironmentName) return;
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        // .NET 8 registers a separate disk-key-ring warmup even with an ephemeral
        // IDataProtectionProvider. It is not used by this provider. Remove ONLY
        // that initializer after all application registrations, never other hosts.
        foreach (var descriptor in builder.Services.Where(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.FullName == "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService").ToArray())
            builder.Services.Remove(descriptor);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
    }
}
