namespace ProjectOS.Application.Common;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ProjectOS";
    public int ExpirationHours { get; set; } = 24;
}
