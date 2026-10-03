using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services;
var builder = WebApplication.CreateBuilder(args);
// ASP.NET request-start messages include query strings; do not log bearer links.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAuditLogService, InMemoryAuditLogService>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ITeacherGrantService, InMemoryTeacherGrantService>();
}
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

app.UseAuthorization();

app.MapControllers();

app.Run();
