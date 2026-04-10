using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/tone-profile")]
[AllowAnonymous]
public class ToneProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ToneProfileController> _logger;

    public ToneProfileController(AppDbContext db, IConfiguration config, ILogger<ToneProfileController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private IActionResult? ValidateOrg(Guid organizationId)
    {
        var allowed = _config["DefaultOrganizationId"];
        if (string.IsNullOrEmpty(allowed))
            return BadRequest(new { error = "Organization not configured. Set DefaultOrganizationId." });
        if (!organizationId.ToString().Equals(allowed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[org_mismatch] endpoint=tone-profile allowed={AllowedOrg}", allowed);
            return Forbid();
        }
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? organizationId, CancellationToken ct)
    {
        var orgId = ResolveOrgId(organizationId);
        if (orgId == Guid.Empty)
            return BadRequest(new { error = "organizationId required" });

        var guard = ValidateOrg(orgId);
        if (guard is not null) return guard;

        var profile = await _db.UserToneProfiles
            .FirstOrDefaultAsync(p => p.OrganizationId == orgId, ct);

        if (profile is null)
        {
            return Ok(new
            {
                id = (Guid?)null,
                organizationId = orgId,
                formality = "professional",
                responseLength = "medium",
                addressStyle = "neutral",
                primaryTraits = "clear",
                avoidTraits = "robotic",
                upsetStyle = "empathetic",
                salesStyle = "consultative",
                signature = "",
                example1 = "",
                example2 = "",
                isDefault = true
            });
        }

        return Ok(MapToResponse(profile));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] ToneProfileRequest req, CancellationToken ct)
    {
        var orgId = ResolveOrgId(req.OrganizationId);
        if (orgId == Guid.Empty)
            return BadRequest(new { error = "organizationId required" });

        var guard = ValidateOrg(orgId);
        if (guard is not null) return guard;

        var profile = await _db.UserToneProfiles
            .FirstOrDefaultAsync(p => p.OrganizationId == orgId, ct);

        if (profile is null)
        {
            profile = new UserToneProfile { OrganizationId = orgId };
            _db.UserToneProfiles.Add(profile);
        }

        profile.Formality = req.Formality ?? profile.Formality;
        profile.ResponseLength = req.ResponseLength ?? profile.ResponseLength;
        profile.AddressStyle = req.AddressStyle ?? profile.AddressStyle;
        profile.PrimaryTraits = req.PrimaryTraits ?? profile.PrimaryTraits;
        profile.AvoidTraits = req.AvoidTraits ?? profile.AvoidTraits;
        profile.UpsetStyle = req.UpsetStyle ?? profile.UpsetStyle;
        profile.SalesStyle = req.SalesStyle ?? profile.SalesStyle;
        profile.Signature = req.Signature;
        profile.Example1 = req.Example1;
        profile.Example2 = req.Example2;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[tone_profile_saved] org={OrgId}", orgId);

        return Ok(MapToResponse(profile));
    }

    private Guid ResolveOrgId(Guid? provided)
    {
        if (provided.HasValue && provided.Value != Guid.Empty) return provided.Value;
        return Guid.TryParse(_config["DefaultOrganizationId"], out var cfg) ? cfg : Guid.Empty;
    }

    private static object MapToResponse(UserToneProfile p) => new
    {
        p.Id,
        p.OrganizationId,
        p.Formality,
        p.ResponseLength,
        p.AddressStyle,
        p.PrimaryTraits,
        p.AvoidTraits,
        p.UpsetStyle,
        p.SalesStyle,
        p.Signature,
        p.Example1,
        p.Example2,
        isDefault = false
    };
}

public record ToneProfileRequest(
    Guid? OrganizationId,
    string? Formality,
    string? ResponseLength,
    string? AddressStyle,
    string? PrimaryTraits,
    string? AvoidTraits,
    string? UpsetStyle,
    string? SalesStyle,
    string? Signature,
    string? Example1,
    string? Example2
);
