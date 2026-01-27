using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("excel")]
    public IActionResult ExportExcel([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? brandId, [FromQuery] int? categoryId)
    {
        return StatusCode(StatusCodes.Status501NotImplemented, new { message = "Export will be implemented" });
    }
}
