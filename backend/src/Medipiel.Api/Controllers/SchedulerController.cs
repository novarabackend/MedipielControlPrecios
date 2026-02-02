using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Medipiel.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/scheduler")]
public class SchedulerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SchedulerSettingsService _settingsService;
    private readonly SchedulerExecutionService _executionService;
    private readonly IServiceScopeFactory _scopeFactory;
    public SchedulerController(
        AppDbContext db,
        SchedulerSettingsService settingsService,
        SchedulerExecutionService executionService,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _settingsService = settingsService;
        _executionService = executionService;
        _scopeFactory = scopeFactory;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _settingsService.GetOrCreateAsync(ct);
        return Ok(Map(settings));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SchedulerSettingsUpdate input, CancellationToken ct)
    {
        if (!TimeSpan.TryParse(input.DailyTime, out var dailyTime))
        {
            return BadRequest("DailyTime must be in HH:mm format.");
        }

        if (input.DaysOfWeekMask < 0 || input.DaysOfWeekMask > 127)
        {
            return BadRequest("DaysOfWeekMask must be between 0 and 127.");
        }

        var settings = await _settingsService.GetOrCreateAsync(ct);
        settings.DailyTime = dailyTime;
        settings.DaysOfWeekMask = input.DaysOfWeekMask;
        settings.Enabled = input.Enabled;
        settings.Mode = "Complete";
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(Map(settings));
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var running = await _db.SchedulerRuns
            .Where(x => x.Status == "Running")
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);

        var last = await _db.SchedulerRuns
            .Where(x => x.Status != "Running")
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            running = running is not null,
            runningSince = running?.StartedAt,
            lastRunAt = last?.StartedAt,
            lastStatus = last?.Status,
            lastMessage = last?.Message
        });
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunManual(CancellationToken ct)
    {
        var hasProducts = await _db.Products.AnyAsync(ct);
        if (!hasProducts)
        {
            return BadRequest("No se puede ejecutar el proceso sin productos cargados.");
        }

        var run = await _executionService.TryStartRunAsync("Manual", ct);
        if (run is null)
        {
            return Conflict("There is already a run in progress.");
        }

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<CompetitorRunService>();
            var execution = scope.ServiceProvider.GetRequiredService<SchedulerExecutionService>();
            try
            {
                var summary = await runner.RunAsync(run.Id, null, onlyNew: true, batchSize: 0, CancellationToken.None);
                var message = summary.Messages.Count > 0 ? string.Join(" | ", summary.Messages) : "OK";
                await execution.CompleteRunAsync(run.Id, "Success", message, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await execution.CompleteRunAsync(run.Id, "Failed", ex.Message, CancellationToken.None);
            }
        });

        return Accepted(new { runId = run.Id });
    }

    private static SchedulerSettingsDto Map(SchedulerSettings settings)
    {
        return new SchedulerSettingsDto(
            settings.DailyTime.ToString(@"hh\:mm"),
            settings.DaysOfWeekMask,
            settings.Enabled,
            settings.Mode
        );
    }
}

public record SchedulerSettingsDto(string DailyTime, int DaysOfWeekMask, bool Enabled, string Mode);
public record SchedulerSettingsUpdate(string DailyTime, int DaysOfWeekMask, bool Enabled);
