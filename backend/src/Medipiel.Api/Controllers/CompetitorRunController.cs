using Medipiel.Api.Data;
using Medipiel.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/competitors")]
public class CompetitorRunController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CompetitorRunService _runner;
    private readonly SchedulerExecutionService _executionService;
    private readonly IServiceScopeFactory _scopeFactory;

    public CompetitorRunController(
        AppDbContext db,
        CompetitorRunService runner,
        SchedulerExecutionService executionService,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _runner = runner;
        _executionService = executionService;
        _scopeFactory = scopeFactory;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] CompetitorRunRequest request, CancellationToken ct)
    {
        var hasProducts = await _db.Products.AnyAsync(ct);
        if (!hasProducts)
        {
            return BadRequest("No se puede ejecutar el proceso sin productos cargados.");
        }

        var run = await _executionService.TryStartRunAsync("Manual", ct);
        if (run is null)
        {
            return Conflict("Ya hay una corrida en ejecucion.");
        }

        var competitorId = request.CompetitorId;
        var onlyNew = request.OnlyNew ?? true;
        var batchSize = request.BatchSize ?? 0;

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<CompetitorRunService>();
            var execution = scope.ServiceProvider.GetRequiredService<SchedulerExecutionService>();
            try
            {
                var summary = await service.RunAsync(run.Id, competitorId, onlyNew, batchSize, CancellationToken.None);
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
}

public sealed record CompetitorRunRequest(int? CompetitorId, bool? OnlyNew, int? BatchSize);
