using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Api.Authentication;

public sealed class JwtSettings
{
    public string Issuer { get; init; } = "InventoryManagement";
    public string Audience { get; init; } = "InventoryManagement.Client";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience)
            || Encoding.UTF8.GetByteCount(SigningKey) < 32
            || AccessTokenMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "Configure Jwt issuer, audience, a signing key of at least 32 UTF-8 bytes, and AccessTokenMinutes between 1 and 60.");
        }
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = "sub",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(10)
    };
}
