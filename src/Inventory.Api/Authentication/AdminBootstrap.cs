using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Inventory.Api.Authentication;

public static class AdminBootstrap
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("BootstrapAdmin:Email and BootstrapAdmin:Password are required.");
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        foreach (var role in new[] { "Admin", "Staff" })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
            }
        }

        var existing = await users.FindByNameAsync(email);
        if (existing is not null)
        {
            if (!await users.IsInRoleAsync(existing, "Admin"))
            {
                throw new InvalidOperationException("Bootstrap email belongs to an existing non-admin account.");
            }
        }
        else
        {
            var user = new IdentityUser { UserName = email, Email = email, LockoutEnabled = true };
            EnsureSuccess(await users.CreateAsync(user, password));
            EnsureSuccess(await users.AddToRoleAsync(user, "Admin"));
        }

        await transaction.CommitAsync();
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}
