using System.IdentityModel.Tokens.Jwt;
using Inventory.Api.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Tests;

public sealed class TokenIssuerTests
{
    private static JwtSettings Settings() => new()
    {
        SigningKey = "test-only-signing-key-32-bytes-minimum-12345"
    };

    [Fact]
    public void IssuedTokenPreservesSubjectRolesAndExpiration()
    {
        var settings = Settings();
        var user = new IdentityUser { Id = "staff-user" };
        var now = DateTimeOffset.UtcNow;
        var response = new TokenIssuer(settings, TimeProvider.System).Create(user, ["Staff"]);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var principal = handler.ValidateToken(response.AccessToken, settings.ValidationParameters(), out var validated);

        Assert.Equal("staff-user", principal.Identity!.Name);
        Assert.True(principal.IsInRole("Staff"));
        Assert.False(principal.IsInRole("Admin"));
        Assert.Equal("Bearer", response.TokenType);
        Assert.InRange(validated.ValidTo, now.AddMinutes(29).UtcDateTime, now.AddMinutes(31).UtcDateTime);
        Assert.Equal(response.ExpiresAt.ToUnixTimeSeconds(), new DateTimeOffset(validated.ValidTo).ToUnixTimeSeconds());
    }

    [Fact]
    public void TokenWithDifferentKeyIsRejected()
    {
        var settings = Settings();
        var response = new TokenIssuer(settings, TimeProvider.System).Create(new IdentityUser(), ["Admin"]);
        var wrongSettings = new JwtSettings { SigningKey = new string('x', 48) };

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler()
            .ValidateToken(response.AccessToken, wrongSettings.ValidationParameters(), out _));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    public void TokenFromAnotherIssuerOrAudienceIsRejected(string field)
    {
        var settings = Settings();
        var response = new TokenIssuer(settings, TimeProvider.System).Create(new IdentityUser(), ["Staff"]);
        var parameters = settings.ValidationParameters();
        if (field == "issuer")
        {
            parameters.ValidIssuer = "Other";
        }
        else
        {
            parameters.ValidAudience = "Other";
        }

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler()
            .ValidateToken(response.AccessToken, parameters, out _));
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        var settings = Settings();
        var issuer = new TokenIssuer(settings, new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(-2)));
        var response = issuer.Create(new IdentityUser(), ["Staff"]);

        Assert.Throws<SecurityTokenExpiredException>(() => new JwtSecurityTokenHandler()
            .ValidateToken(response.AccessToken, settings.ValidationParameters(), out _));
    }

    [Fact]
    public void MissingSigningKeyFailsConfigurationValidation()
    {
        Assert.Throws<InvalidOperationException>(() => new JwtSettings().Validate());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
