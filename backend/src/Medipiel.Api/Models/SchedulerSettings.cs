namespace Medipiel.Api.Models;

public class SchedulerSettings
{
    public int Id { get; set; } = 1;
    public TimeSpan DailyTime { get; set; } = new(6, 0, 0);
    public int DaysOfWeekMask { get; set; } = 127;
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = "Complete";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
