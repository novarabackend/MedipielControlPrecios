using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Services;

public class SchedulerSettingsService
{
    private readonly AppDbContext _db;

    public SchedulerSettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SchedulerSettings> GetOrCreateAsync(CancellationToken ct = default)
    {
        var settings = await _db.SchedulerSettings.FirstOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new SchedulerSettings();
        _db.SchedulerSettings.Add(settings);
        await _db.SaveChangesAsync(ct);
        return settings;
    }
}
