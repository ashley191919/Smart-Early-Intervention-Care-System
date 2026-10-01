using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

if (!JwtOptions.TryLoad(builder.Configuration, out var jwtOptions, out var decodedSigningKey, out var jwtError))
{
    Console.WriteLine(jwtError);
    Environment.ExitCode = 1;
    return;
}
var signingKey = new SymmetricSecurityKey(decodedSigningKey);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "username",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "貼上 JWT 本身；Swagger 會自動加入 Bearer 前綴。"
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
