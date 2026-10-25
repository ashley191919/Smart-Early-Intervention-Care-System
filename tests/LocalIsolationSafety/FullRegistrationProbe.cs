using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EarlyInterventionCare.Api.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

// Invoke the real entry point, but abort at HostBuilt BEFORE app.Run / server start.
// Only the DP hosted initializer is then run manually, with a trap repository.
static class FullRegistrationProbe
{
    public static async Task<int> RunAsync(bool reproduceWarmup)
    {
        var names = new[] { "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "EARLYCARE_LOCAL_SETTINGS" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"appsettings.v53-probe-{Guid.NewGuid():N}.Local.json");
        var trap = new TrapRepository();
        using var observer = new RegistrationObserver(trap, reproduceWarmup);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                ConnectionStrings = new { DefaultConnection = "Server=127.0.0.1;Port=33326;Database=earlycare_v53_synthetic;User=earlycare_v53_app;Password=synthetic-unregistered-probe;AllowLoadLocalInfile=false" },
                Jwt = new { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), Issuer = "synthetic-probe", Audience = "synthetic-probe", ExpirationMinutes = 30 },
                Logging = new { LogLevel = new Dictionary<string, string> { ["Default"] = "None" } }
            }));
            Environment.SetEnvironmentVariable(names[0], "IsolatedTest");
            Environment.SetEnvironmentVariable(names[1], "IsolatedTest");
            Environment.SetEnvironmentVariable(names[2], path);
            var entry = typeof(LocalIsolationRuntime).Assembly.EntryPoint!;
            try
            {
                var result = entry.Invoke(null, new object[] { Array.Empty<string>() });
                if (result is Task task) await task;
                throw new Exception("Real entry point unexpectedly returned without inspection abort");
            }
            catch (Exception e) when (ContainsAbort(e)) { }
            using var host = observer.Host ?? throw new Exception("HOST_BUILT_NOT_CAPTURED");
            var services = host.Services;
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
            Check(services.GetRequiredService<IHostEnvironment>().EnvironmentName == "IsolatedTest", "real Program environment");
            LocalIsolationSettings.ValidateConnection(services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"));
            Check(true, "real Program uses synthetic loopback DB configuration without connecting");
            Check(services.GetRequiredService<IHostLifetime>().GetType().Name == "ConsoleLifetime", "real Program uses ConsoleLifetime");
            Check(services.GetServices<IHostLifetime>().Count() == 1, "real Program has exactly one HostLifetime");
            Check(services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout == TimeSpan.FromSeconds(5), "real Program shutdown timeout five seconds");
            Check(services.GetRequiredService<IDataProtectionProvider>() is EphemeralDataProtectionProvider, "real Program final provider ephemeral");
            Check(ReferenceEquals(services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository, trap), "trap repository installed before any initializer");
            var hosted = services.GetServices<IHostedService>().ToArray();
            foreach (var service in hosted) Console.WriteLine("HOSTED_SERVICE=" + service.GetType().FullName);
            var initializer = hosted.SingleOrDefault(s => s.GetType().FullName?.Contains("DataProtectionHostedService") == true);
            Check(trap.Reads == 0 && trap.Writes == 0, "building real Program did not access repository");
            Check(hosted.Any(s => s.GetType().FullName == "Microsoft.AspNetCore.Hosting.GenericWebHostService"), "real web hosted service retained but never started");
            if (reproduceWarmup)
            {
                Check(initializer is not null, "baseline initializer restored only by test");
                await initializer!.StartAsync(default);
                await initializer.StopAsync(default);
                Check(trap.Reads > 0, "DP hosted initializer independently accesses key repository");
                Console.WriteLine("REPOSITORY_CALL_STACK=" + trap.Stack);
            }
            else
            {
                Check(initializer is null, "unused disk-ring initializer removed in real Program");
                var antiforgery = services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
                context.Request.Scheme = "https";
                var tokens = antiforgery.GetAndStoreTokens(context);
                Check(!string.IsNullOrEmpty(tokens.RequestToken), "real Program antiforgery initialization works");
                var protection = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("synthetic-real-program");
                Check(protection.Unprotect(protection.Protect("synthetic-only")) == "synthetic-only", "real Program protection roundtrip");
                Check(trap.Reads == 0, "antiforgery and provider never access trap repository");
                var options = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value;
                Check(options.Cookie.SecurePolicy == Microsoft.AspNetCore.Http.CookieSecurePolicy.Always && options.Cookie.SameSite == Microsoft.AspNetCore.Http.SameSiteMode.Strict, "real Program secure CSRF cookie unchanged");
            }
            Check(trap.Writes == 0, "test disabled generation; no write attempt");
            Console.WriteLine($"{checks} full-registration checks PASS; HTTP/MySQL never started; all repository access trapped; reproduceWarmup={reproduceWarmup}");
            return 0;
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
            File.Delete(path);
            Console.WriteLine("PROBE_SETTINGS_REMOVED=" + !File.Exists(path));
            Console.WriteLine("PROBE_ENV_RESTORED=" + names.Select(Environment.GetEnvironmentVariable).SequenceEqual(previous));
        }
    }
    static bool ContainsAbort(Exception e) => e is InspectionAbort || e.InnerException is not null && ContainsAbort(e.InnerException);
    sealed class InspectionAbort : Exception { }
    sealed class RegistrationObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        readonly TrapRepository trap;
        readonly List<IDisposable> subscriptions = [];
        public IHost? Host;
        readonly bool reproduceWarmup;
        public RegistrationObserver(TrapRepository trap, bool reproduceWarmup) { this.trap = trap; this.reproduceWarmup = reproduceWarmup; subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this)); }
        public void OnNext(DiagnosticListener listener) { if (listener.Name == "Microsoft.Extensions.Hosting") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> e)
        {
            if (e.Key == "HostBuilding" && e.Value is IHostBuilder builder)
                builder.ConfigureServices(s =>
                {
                    s.PostConfigure<KeyManagementOptions>(o => { o.XmlRepository = trap; o.AutoGenerateKeys = false; });
                    if (reproduceWarmup)
                    {
                        var type = typeof(EphemeralDataProtectionProvider).Assembly.GetType("Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService", throwOnError: true)!;
                        s.AddSingleton(typeof(IHostedService), type);
                    }
                });
            if (e.Key == "HostBuilt" && e.Value is IHost host) { Host = host; throw new InspectionAbort(); }
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
    }
    sealed class TrapRepository : IXmlRepository
    {
        public int Reads, Writes;
        public string? Stack;
        public IReadOnlyCollection<XElement> GetAllElements() { Reads++; Stack = new StackTrace().ToString(); return Array.Empty<XElement>(); }
        public void StoreElement(XElement element, string friendlyName) { Writes++; throw new Exception("TRAPPED_WRITE_NO_FILE_IO"); }
    }
}
