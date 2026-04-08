using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ProjectOS.Api.Auth;

public static class AdminAuth
{
    public static bool IsAuthorizedForOrganization(ClaimsPrincipal user, HttpRequest request, IConfiguration config, Guid organizationId)
    {
        if (IsJwtAuthorizedForOrganization(user, organizationId))
            return true;

        return IsAdminKeyValid(request, config);
    }

    public static bool IsJwtAuthorizedForOrganization(ClaimsPrincipal user, Guid organizationId)
    {
        var orgClaim = user.FindFirst("organizationId")?.Value;
        if (orgClaim is not null && Guid.TryParse(orgClaim, out var claimOrgId))
        {
            if (claimOrgId == organizationId)
                return true;
        }

        var orgsClaim = user.FindFirst("organizationIds")?.Value;
        if (orgsClaim is not null)
        {
            var orgIds = orgsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (orgIds.Any(id => Guid.TryParse(id.Trim(), out var parsedId) && parsedId == organizationId))
                return true;
        }

        return false;
    }

    public static bool IsAdminKeyValid(HttpRequest request, IConfiguration config)
    {
        var adminKey = config["AdminKey"];
        if (string.IsNullOrEmpty(adminKey))
            return false;

        var providedKey = request.Headers["X-Admin-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
            return false;

        var expected = Encoding.UTF8.GetBytes(adminKey);
        var actual = Encoding.UTF8.GetBytes(providedKey);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static List<Guid> GetOrganizationIds(ClaimsPrincipal user)
    {
        var orgsClaim = user.FindFirst("organizationIds")?.Value;
        if (orgsClaim is not null)
        {
            return orgsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => Guid.TryParse(id.Trim(), out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }

        var orgClaim = user.FindFirst("organizationId")?.Value;
        if (orgClaim is not null && Guid.TryParse(orgClaim, out var singleId))
            return new List<Guid> { singleId };

        return new List<Guid>();
    }

    public static bool IsOwnerOrAdmin(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        return role is "Owner" or "Admin";
    }
}
