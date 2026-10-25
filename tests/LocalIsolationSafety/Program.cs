using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EarlyInterventionCare.Api.Options;

if (args.Contains("--full-registration")) return await FullRegistrationProbe.RunAsync(args.Contains("--reproduce-warmup"));
if (args.Contains("--stop-lifecycle")) return await StopLifecycleChecks.RunAsync();

int passed = 0, failed = 0;
void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (ok) passed++; else failed++; }
// Build service containers only: no HTTP host, no key-ring access in baseline, no DB.
foreach (var environment in new[] { "IsolatedTest", "Development", "Production" })
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, Args = [] });
    if (environment == "IsolatedTest") Console.WriteLine("CREATE_BUILDER_DP_REGISTRATIONS=" + builder.Services.Count(d => d.ServiceType == typeof(IDataProtectionProvider)) + "; DP_HOSTED=" + builder.Services.Count(d => d.ImplementationType?.FullName == "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService"));
    builder.Services.AddAntiforgery();
    if (environment == "IsolatedTest") Console.WriteLine("AFTER_ANTIFORGERY_DP_REGISTRATIONS=" + builder.Services.Count(d => d.ServiceType == typeof(IDataProtectionProvider)) + "; DP_HOSTED=" + builder.Services.Count(d => d.ImplementationType?.FullName == "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService"));
    var before = builder.Services.Where(d => d.ServiceType == typeof(ILoggerProvider)).ToArray();
    var beforeHosted = builder.Services.Where(d => d.ServiceType == typeof(IHostedService)).ToArray();
    var beforeProtection = builder.Services.Where(d => d.ServiceType == typeof(IDataProtectionProvider)).ToArray();
    if (environment == "IsolatedTest") Console.WriteLine("BASELINE_PROVIDERS=" + string.Join(",", before.Select(d => d.ImplementationType?.Name ?? "factory")));
    LocalIsolationRuntime.Configure(builder);
    LocalIsolationRuntime.Configure(builder); // repeated registration stays isolated
    using var services = builder.Services.BuildServiceProvider();
    var providers = services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name).ToArray();
    if (environment != "IsolatedTest")
    {
        Check(before.SequenceEqual(builder.Services.Where(d => d.ServiceType == typeof(ILoggerProvider))), environment + " logging registrations unchanged");
        Check(beforeHosted.SequenceEqual(builder.Services.Where(d => d.ServiceType == typeof(IHostedService))), environment + " hosted services unchanged");
        Check(beforeProtection.SequenceEqual(builder.Services.Where(d => d.ServiceType == typeof(IDataProtectionProvider))), environment + " provider descriptors unchanged");
        Check(services.GetRequiredService<IDataProtectionProvider>() is not EphemeralDataProtectionProvider, environment + " retains default DP registration");
        continue;
    }
    Check(providers.Length == 1 && providers[0] == "ConsoleLoggerProvider", "isolated console only; no EventLog");
    var dp = services.GetRequiredService<IDataProtectionProvider>();
    Check(dp is EphemeralDataProtectionProvider, "isolated in-memory DP; no user key repository");
    Check(services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout == TimeSpan.FromSeconds(5), "isolated bounded shutdown");
    // Generic host only: no Kestrel, no HTTP, no application/database services.
    var probe = new StopProbe();
    using (var host = new HostBuilder().ConfigureServices(s =>
    {
        foreach (var configuration in services.GetServices<IConfigureOptions<HostOptions>>())
            s.AddSingleton(configuration);
        s.AddSingleton<IHostedService>(probe);
    }).Build())
    {
        Check(host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout == TimeSpan.FromSeconds(5), "real host receives isolated stop timeout");
        await host.StartAsync();
        await host.StopAsync();
        Check(probe.Stopped, "generic host cooperative stop delivered");
        Check(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested, "generic host stopped signal");
    }
    // Never invoke default DP: it could access existing user keys.
    if (dp is EphemeralDataProtectionProvider)
    {
        var protector = dp.CreateProtector("synthetic-csrf-contract");
        var encrypted = protector.Protect("synthetic-only");
        Check(protector.Unprotect(encrypted) == "synthetic-only", "same-process roundtrip");
        var independent = new EphemeralDataProtectionProvider().CreateProtector("synthetic-csrf-contract");
        try { independent.Unprotect(encrypted); Check(false, "independent process key rejected"); }
        catch (System.Security.Cryptography.CryptographicException) { Check(true, "independent process key rejected"); }
        try { dp.CreateProtector("other-purpose").Unprotect(encrypted); Check(false, "other purpose rejected"); }
        catch (System.Security.Cryptography.CryptographicException) { Check(true, "other purpose rejected"); }
        var antiforgery = services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        var tokens = antiforgery.GetAndStoreTokens(context);
        Check(!string.IsNullOrEmpty(tokens.RequestToken) && !string.IsNullOrEmpty(tokens.CookieToken), "antiforgery still issues protected tokens");
        var validation = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        validation.Request.Scheme = "https";
        validation.Request.Method = "POST";
        var cookieName = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value.Cookie.Name;
        validation.Request.Headers.Cookie = cookieName + "=" + tokens.CookieToken;
        validation.Request.Headers[tokens.HeaderName!] = tokens.RequestToken;
        await antiforgery.ValidateRequestAsync(validation);
        Check(true, "valid antiforgery pair accepted without HTTP host");
        // Antiforgery caches validation per request: use a fresh request for rejection.
        var invalid = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        invalid.Request.Scheme = "https";
        invalid.Request.Method = "POST";
        invalid.Request.Headers.Cookie = cookieName + "=" + tokens.CookieToken;
        invalid.Request.Headers[tokens.HeaderName!] = "synthetic-invalid-token";
        try { await antiforgery.ValidateRequestAsync(invalid); Check(false, "invalid antiforgery token rejected"); }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { Check(true, "invalid antiforgery token rejected"); }
    }
}
Console.WriteLine($"{passed} passed, {failed} failed; no HTTP/DB started");
return failed == 0 ? 0 : 1;

sealed class StopProbe : IHostedService
{
    public bool Stopped { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) { Stopped = true; return Task.CompletedTask; }
}

// No HTTP server, DbContext, application controllers, process signals or business services.
static class StopLifecycleChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IsolatedTest", Args = [] });
        LocalIsolationRuntime.Configure(builder);
        using var configuration = builder.Services.BuildServiceProvider();
        var options = configuration.GetServices<IConfigureOptions<HostOptions>>().ToArray();
        IHost Create(List<string> events, LifecycleService service, bool shortTimeout = false)
        {
            var host = new HostBuilder().ConfigureServices(s =>
            {
                foreach (var option in options) s.AddSingleton(option);
                if (shortTimeout) s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(100));
                s.AddSingleton<IHostedService>(service);
            }).Build();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() => events.Add("stopping"));
            lifetime.ApplicationStopped.Register(() => events.Add("stopped"));
            return host;
        }
        try
        {
            var events = new List<string>();
            var service = new LifecycleService(events);
            using (var host = Create(events, service))
            {
                await host.StartAsync();
                Check(host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout == TimeSpan.FromSeconds(5), "application helper contributes IsolatedTest five-second timeout");
                Check(host.Services.GetServices<IHostLifetime>().Count() == 1 && host.Services.GetRequiredService<IHostLifetime>().GetType().Name == "ConsoleLifetime", "single ConsoleLifetime in generic host");
                host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                Check(events.Contains("stopping"), "StopApplication triggers ApplicationStopping");
                Check(!events.Contains("service-stop"), "StopApplication alone does not execute service StopAsync");
                await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Check(events.SequenceEqual(new[] { "start", "stopping", "service-stop", "stopped" }), "explicit StopAsync completes ordered lifecycle");
            }
            events = [];
            service = new LifecycleService(events);
            using (var host = Create(events, service))
            {
                await host.StartAsync();
                var shutdown = host.WaitForShutdownAsync();
                host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
                Check(events.SequenceEqual(new[] { "start", "stopping", "service-stop", "stopped" }), "WaitForShutdown bridges StopApplication to StopAsync");
                Check(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested, "wait loop reports ApplicationStopped");
            }
            events = [];
            service = new LifecycleService(events);
            using (var host = Create(events, service))
            {
                await host.StartAsync();
                await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Check(events.SequenceEqual(new[] { "start", "stopping", "service-stop", "stopped" }), "direct StopAsync triggers and completes lifecycle");
            }
            events = [];
            service = new LifecycleService(events) { WaitForCancellation = true };
            using (var host = Create(events, service, shortTimeout: true))
            {
                await host.StartAsync();
                await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Check(service.CancellationObserved, "short test-only timeout cancels cooperative service token");
                Check(events.Contains("stopped"), "cooperative service permits shutdown completion");
            }
            events = [];
            service = new LifecycleService(events) { IgnoreCancellation = true };
            using (var host = Create(events, service, shortTimeout: true))
            {
                await host.StartAsync();
                var stop = host.StopAsync();
                try
                {
                    await Task.Delay(300);
                    Check(!stop.IsCompleted, "shutdown timeout does not forcibly terminate noncooperative service");
                }
                finally { service.Release.TrySetResult(true); await stop.WaitAsync(TimeSpan.FromSeconds(3)); }
                Check(events.Contains("stopped"), "noncooperative fixture released and fully cleaned up");
            }
            Check(configuration.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout == TimeSpan.FromSeconds(5), "short fixture timeout never mutates application options");
            Console.WriteLine($"{checks} stop-lifecycle checks PASS; no HTTP/DB/OS signals/process termination");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("STOP_LIFECYCLE_FAILED: " + e.Message); return 1; }
    }
    sealed class LifecycleService(List<string> events) : IHostedService
    {
        public bool WaitForCancellation, IgnoreCancellation, CancellationObserved;
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(CancellationToken ct) { events.Add("start"); return Task.CompletedTask; }
        public async Task StopAsync(CancellationToken ct)
        {
            events.Add("service-stop");
            if (WaitForCancellation)
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { CancellationObserved = true; }
            if (IgnoreCancellation) await Release.Task;
        }
    }
}
