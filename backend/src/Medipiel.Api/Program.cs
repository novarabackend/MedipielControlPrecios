using Medipiel.Api.Data;
using Medipiel.Api.Services;
using Medipiel.Api.Controllers;
using Microsoft.EntityFrameworkCore;

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
