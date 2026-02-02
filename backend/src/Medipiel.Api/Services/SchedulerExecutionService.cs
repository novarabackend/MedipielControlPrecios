using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Services;

public class SchedulerExecutionService
{
    private readonly AppDbContext _db;

    public SchedulerExecutionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SchedulerRun?> TryStartRunAsync(string trigger, CancellationToken ct = default)
    {
        var isRunning = await _db.SchedulerRuns.AnyAsync(x => x.Status == "Running", ct);
        if (isRunning)
        {
            return null;
        }

        var run = new SchedulerRun
        {
            TriggerType = trigger,
            Status = "Running",
            StartedAt = DateTime.UtcNow
        };

        _db.SchedulerRuns.Add(run);
        try
        {
            await _db.SaveChangesAsync(ct);
            return run;
        }
        catch (DbUpdateException)
        {
            return null;
        }
    }

    public async Task CompleteRunAsync(int runId, string status, string? message, CancellationToken ct = default)
    {
        var run = await _db.SchedulerRuns.FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null)
        {
            return;
        }

        run.Status = status;
        run.Message = message;
        run.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
