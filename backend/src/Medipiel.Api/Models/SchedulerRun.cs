namespace Medipiel.Api.Models;

public class SchedulerRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "Running";
    public string TriggerType { get; set; } = "Scheduled";
    public string? Message { get; set; }
}
