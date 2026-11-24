using System.ComponentModel.DataAnnotations;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public sealed class UsersController(UserManager<IdentityUser> users, InventoryDbContext context) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreatedStaff>> CreateStaff(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await users.FindByNameAsync(email) is not null)
        {
            return Conflict(new ProblemDetails { Title = "This account already exists.", Status = 409 });
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new IdentityUser { UserName = email, Email = email, LockoutEnabled = true };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return IdentityErrors(created);
            }

            var assigned = await users.AddToRoleAsync(user, "Staff");
            if (!assigned.Succeeded)
            {
                return IdentityErrors(assigned);
            }

            await transaction.CommitAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new CreatedStaff(user.Id, email, "Staff"));
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new ProblemDetails { Title = "This account already exists.", Status = 409 });
        }
    }

    private ActionResult IdentityErrors(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
        {
            return Conflict(new ProblemDetails { Title = "This account already exists.", Status = 409 });
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}

public sealed record CreateStaffRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password);
public sealed record CreatedStaff(string Id, string Email, string Role);
