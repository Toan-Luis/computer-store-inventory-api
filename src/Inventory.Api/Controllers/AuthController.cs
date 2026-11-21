using System.ComponentModel.DataAnnotations;
using Inventory.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn,
    TokenIssuer tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByNameAsync(request.Email.Trim());
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        return Ok(tokens.Create(user, await users.GetRolesAsync(user)));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserProfile> Me() => Ok(new UserProfile(
        User.FindFirst("sub")!.Value,
        User.FindAll("role").Select(claim => claim.Value).ToArray()));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(User.FindFirst("sub")!.Value);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        return NoContent();
    }
}

public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record UserProfile(string Id, IReadOnlyList<string> Roles);
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
