using Microsoft.Extensions.Options;

namespace Medipiel.Api.Services;

public class ScrapeScheduler : BackgroundService
{
    private readonly ILogger<ScrapeScheduler> _logger;
    private readonly ScrapeScheduleOptions _options;
    private DateOnly? _lastRunDate;

    public ScrapeScheduler(ILogger<ScrapeScheduler> logger, IOptions<ScrapeScheduleOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _logger.LogInformation("Scrape scheduler started. Daily run at {Hour:D2}:{Minute:D2}.", _options.DailyHour, _options.DailyMinute);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.Now;
            if (now.Hour != _options.DailyHour || now.Minute != _options.DailyMinute)
            {
                continue;
            }

            var today = DateOnly.FromDateTime(now);
            if (_lastRunDate == today)
            {
                continue;
            }

            _lastRunDate = today;
            _logger.LogInformation("Scheduled scrape triggered at {Timestamp}", now);
            // TODO: call scraping workflow
        }
    }
}
