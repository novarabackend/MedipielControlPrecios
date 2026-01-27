namespace Medipiel.Api.Models;

public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int RowsTotal { get; set; }
    public int RowsProcessed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
