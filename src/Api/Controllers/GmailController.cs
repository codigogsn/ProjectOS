using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectOS.Application.Interfaces;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/gmail")]
[AllowAnonymous]
public class GmailController : ControllerBase
{
    private readonly IEmailIngestionService _ingestionService;
    private readonly IConfiguration _config;
    private readonly ILogger<GmailController> _logger;

    public GmailController(
        IEmailIngestionService ingestionService,
        IConfiguration config,
        ILogger<GmailController> logger)
    {
        _ingestionService = ingestionService;
        _config = config;
        _logger = logger;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("Gmail controller alive");
    }

    [HttpGet("sync")]
    public async Task<IActionResult> Sync([FromQuery] Guid? organizationId, CancellationToken ct)
    {
        _logger.LogInformation("GMAIL SYNC TRIGGERED");

        // Resolve org ID: query param > config > generate default
        var orgId = organizationId
            ?? (Guid.TryParse(_config["DefaultOrganizationId"], out var configOrg) ? configOrg : Guid.Empty);

        if (orgId == Guid.Empty)
        {
            _logger.LogWarning("No organizationId provided and DefaultOrganizationId not configured");
            return BadRequest(new { error = "organizationId is required (pass as query param or set DefaultOrganizationId)" });
        }

        var result = await _ingestionService.IngestAsync(orgId, ct);

        _logger.LogInformation("Gmail sync complete: {Fetched} fetched, {Saved} saved, {Duplicates} duplicates",
            result.Fetched, result.Saved, result.Duplicates);

        return Ok(new
        {
            result.Fetched,
            result.Saved,
            result.Duplicates,
            Message = $"Sync complete: {result.Saved} new emails saved, {result.Duplicates} duplicates skipped"
        });
    }
}
