using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly AppDbContext _db;

    public CatalogController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("import")]
    public IActionResult Import()
    {
        return StatusCode(StatusCodes.Status501NotImplemented, new { message = "Import will be implemented" });
    }
}
