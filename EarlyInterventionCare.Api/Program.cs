using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services;
using System.Threading.RateLimiting;
var builder = WebApplication.CreateBuilder(args);
// ASP.NET request-start messages include query strings; do not log bearer links.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAuditLogService, InMemoryAuditLogService>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ITeacherGrantService, InMemoryTeacherGrantService>();
    builder.Services.AddSingleton<TeacherWorkspaceService>();
}
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("teacher-workspace-code", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    )
);
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Reject Development-only routes before HTTPS redirects as well.
app.UseDevelopmentTeacherAccess();

app.UseHttpsRedirection();

// Serve the login prototype and its local assets from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
