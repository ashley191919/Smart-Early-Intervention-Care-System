using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Services.Authentication;
if (args.Contains("--verify-db-read") && args.Contains("--create-dev-user"))
{
    Console.WriteLine("Only one database utility mode may be selected.");
    Environment.ExitCode = 1;
    return;
}
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--verify-db-read" && arg != "--create-dev-user").ToArray());
if (args.Contains("--create-dev-user"))
{
    Environment.ExitCode = await DevelopmentUserCreator.RunAsync(builder);
    return;
}
if (args.Contains("--verify-db-read"))
{
    Environment.ExitCode = await DatabaseReadVerifier.RunAsync(builder);
    return;
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
builder.Services.AddScoped<AuthenticationService>();
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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
