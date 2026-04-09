using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectOS.Application.Common;
using ProjectOS.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/gmail")]
[AllowAnonymous]
public class GmailController : ControllerBase
{
    private readonly IEmailIngestionService _ingestionService;
    private readonly IConfiguration _config;
    private readonly GmailOptions _gmailOptions;
    private readonly ILogger<GmailController> _logger;

    public GmailController(
        IEmailIngestionService ingestionService,
        IConfiguration config,
        IOptions<GmailOptions> gmailOptions,
        ILogger<GmailController> logger)
    {
        _ingestionService = ingestionService;
        _config = config;
        _gmailOptions = gmailOptions.Value;
        _logger = logger;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "alive",
            gmailConfigured = !string.IsNullOrEmpty(_gmailOptions.ResolveClientId()),
            hasRefreshToken = !string.IsNullOrEmpty(_gmailOptions.ResolveRefreshToken())
        });
    }

    [HttpGet("sync")]
    public async Task<IActionResult> Sync([FromQuery] Guid? organizationId, CancellationToken ct)
    {
        _logger.LogInformation("GMAIL SYNC TRIGGERED — orgId param: {OrgId}", organizationId);

        // 1. Resolve org ID
        var orgId = organizationId
            ?? (Guid.TryParse(_config["DefaultOrganizationId"], out var configOrg) ? configOrg : Guid.Empty);

        if (orgId == Guid.Empty)
        {
            _logger.LogWarning("No organizationId provided and DefaultOrganizationId not configured");
            return BadRequest(new
            {
                error = "gmail_sync_failed",
                stage = "resolve_org",
                message = "organizationId is required (pass as query param or set DefaultOrganizationId env var)"
            });
        }

        _logger.LogInformation("Resolved organizationId: {OrgId}", orgId);

        // 2. Check Gmail credentials before attempting
        var clientId = _gmailOptions.ResolveClientId();
        var clientSecret = _gmailOptions.ResolveClientSecret();
        var refreshToken = _gmailOptions.ResolveRefreshToken();

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogError("Gmail credentials missing — ClientId: {HasId}, ClientSecret: {HasSecret}, RefreshToken: {HasToken}",
                !string.IsNullOrEmpty(clientId), !string.IsNullOrEmpty(clientSecret), !string.IsNullOrEmpty(refreshToken));
            return BadRequest(new
            {
                error = "gmail_sync_failed",
                stage = "credentials_check",
                message = "Gmail OAuth credentials not configured. Set GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET, GMAIL_REFRESH_TOKEN env vars.",
                hasClientId = !string.IsNullOrEmpty(clientId),
                hasClientSecret = !string.IsNullOrEmpty(clientSecret),
                hasRefreshToken = !string.IsNullOrEmpty(refreshToken)
            });
        }

        _logger.LogInformation("Gmail credentials verified present — starting ingestion");

        // 3. Run ingestion with full error capture
        try
        {
            var result = await _ingestionService.IngestAsync(orgId, ct);

            _logger.LogInformation("Gmail sync complete: {Fetched} fetched, {Saved} saved, {Duplicates} duplicates, {Failed} failed",
                result.Fetched, result.Saved, result.Duplicates, result.Failed);

            return Ok(new
            {
                organizationId = orgId,
                result.Fetched,
                result.Saved,
                result.Duplicates,
                result.Skipped,
                result.Failed,
                result.Failures,
                Message = $"Sync complete: {result.Saved} saved, {result.Duplicates} duplicates, {result.Skipped} skipped, {result.Failed} failed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gmail sync pipeline failed — type: {ExType}, message: {ExMsg}", ex.GetType().Name, ex.Message);

            var stage = ex switch
            {
                Google.GoogleApiException => "gmail_api",
                Google.Apis.Auth.OAuth2.Responses.TokenResponseException => "gmail_token_refresh",
                HttpRequestException => "http_request",
                InvalidOperationException when ex.Message.Contains("ClientId") => "gmail_credentials",
                _ => "unknown"
            };

            return StatusCode(500, new
            {
                error = "gmail_sync_failed",
                stage,
                exceptionType = ex.GetType().Name,
                message = ex.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }
    }
}
