using Medipiel.Api.Data;
using Medipiel.Api.Services;
using Medipiel.Api.Controllers;
using Medipiel.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection("Otp"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = false,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppAuthorization.AllowedRolesPolicy, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => AppAuthorization.HasRequiredRole(context.User));
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<SchedulerSettingsService>();
builder.Services.AddScoped<SchedulerExecutionService>();
builder.Services.AddScoped<CompetitorRunService>();
builder.Services.AddHostedService<ScrapeScheduler>();
builder.Services.AddSingleton<CompetitorAdapterRegistry>();
builder.Services.AddHostedService<CompetitorAdapterLoader>();

var app = builder.Build();

app.UseCors("default");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers().RequireAuthorization(AppAuthorization.AllowedRolesPolicy);
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
