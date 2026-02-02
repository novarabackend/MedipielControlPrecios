using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Services;

public class ScrapeScheduler : BackgroundService
{
    private readonly ILogger<ScrapeScheduler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScrapeScheduler(ILogger<ScrapeScheduler> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _logger.LogInformation("Scrape scheduler started.");

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settingsService = scope.ServiceProvider.GetRequiredService<SchedulerSettingsService>();
            var executionService = scope.ServiceProvider.GetRequiredService<SchedulerExecutionService>();

            var settings = await settingsService.GetOrCreateAsync(stoppingToken);
            if (!settings.Enabled)
            {
                continue;
            }

            var now = DateTime.Now;
            if (!IsDayEnabled(settings.DaysOfWeekMask, now.DayOfWeek))
            {
                continue;
            }

            if (now.Hour != settings.DailyTime.Hours || now.Minute != settings.DailyTime.Minutes)
            {
                continue;
            }

            var today = now.Date;
            var alreadyRan = await db.SchedulerRuns.AnyAsync(
                x => x.TriggerType == "Scheduled" && x.StartedAt >= today && x.StartedAt < today.AddDays(1),
                stoppingToken
            );
            if (alreadyRan)
            {
                continue;
            }

            var run = await executionService.TryStartRunAsync("Scheduled", stoppingToken);
            if (run is null)
            {
                continue;
            }

            _logger.LogInformation("Scheduled scrape triggered at {Timestamp}", now);
            await SimulateRunAsync(executionService, run.Id, stoppingToken);
        }
    }

    private static bool IsDayEnabled(int mask, DayOfWeek day)
    {
        var index = day == DayOfWeek.Sunday ? 6 : ((int)day - 1);
        var flag = 1 << index;
        return (mask & flag) != 0;
    }

    private static async Task SimulateRunAsync(
        SchedulerExecutionService executionService,
        int runId,
        CancellationToken ct
    )
    {
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        await executionService.CompleteRunAsync(runId, "Success", "Run completed (stub).", ct);
    }
}
