using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace EarlyInterventionCare.Api.Options;

/// <summary>Explicit communication-only host; no configuration files, secrets, EF,
/// authentication, Controllers, static files or business routes are composed here.
/// Normal application composition and authorization remain unchanged.</summary>
public static class LocalCommunicationHost
{
    public const string Url = "http://127.0.0.1:5186";

    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions {
            Args = [], EnvironmentName = "LocalCommunication",
            ApplicationName = typeof(LocalCommunicationHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseKestrelCore().UseUrls(Url);
        builder.Services.AddRouting();
        builder.Logging.AddSimpleConsole();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        await using var app = builder.Build();
        app.Use(async (context, next) => {
            if (context.Connection.RemoteIpAddress?.ToString() != "127.0.0.1" || context.Request.Host.Value != "127.0.0.1:5186") {
                context.Response.StatusCode = 403; return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
        app.MapGet("/health/local", () => Results.Json(new { status = "alive", databaseChecked = false }));
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        if (addresses.Count != 1 || !addresses.Contains(Url)) {
            await app.StopAsync(); throw new InvalidOperationException("COMMUNICATION_LISTENER_REJECTED");
        }
        Console.WriteLine($"COMMUNICATION_READY pid={Environment.ProcessId} url={Url} database=not-registered");
        // Console-only cooperative shutdown; no HTTP shutdown route or process killing.
        _ = Task.Run(async () => {
            while (await Console.In.ReadLineAsync() is { } line)
                if (line == "stop") { app.Lifetime.StopApplication(); return; }
        });
        await app.WaitForShutdownAsync();
        Console.WriteLine("COMMUNICATION_STOPPED");
    }
}
