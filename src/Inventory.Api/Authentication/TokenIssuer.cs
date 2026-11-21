using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Api.Authentication;

public sealed class TokenIssuer(JwtSettings settings, TimeProvider timeProvider)
{
    public TokenResponse Create(IdentityUser user, IEnumerable<string> roles)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("security_stamp", user.SecurityStamp ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var token = new JwtSecurityToken(
            settings.Issuer, settings.Audience, claims,
            notBefore: now.UtcDateTime, expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, "Bearer");
    }
}

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType);
