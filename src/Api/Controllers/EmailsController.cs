using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectOS.Application.Interfaces;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmailsController : ControllerBase
{
    private readonly IEmailIngestionService _ingestionService;
    private readonly ILogger<EmailsController> _logger;

    public EmailsController(IEmailIngestionService ingestionService, ILogger<EmailsController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] Guid organizationId, CancellationToken ct)
    {
        _logger.LogInformation("Email sync triggered for organization {OrgId}", organizationId);

        var result = await _ingestionService.IngestAsync(organizationId, ct);

        return Ok(new
        {
            result.Fetched,
            result.Saved,
            result.Duplicates,
            Message = $"Sync complete: {result.Saved} new emails saved, {result.Duplicates} duplicates skipped"
        });
    }
}
