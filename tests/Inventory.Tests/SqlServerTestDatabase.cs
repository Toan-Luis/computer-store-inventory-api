using Inventory.Domain;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("InventoryTestConnectionString")))
        {
            Skip = "Set InventoryTestConnectionString to run SQL Server integration tests.";
        }
    }
}

public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    private readonly string databaseName = "InventoryManagementTests_" + Guid.NewGuid().ToString("N");
    private string? connectionString;
    public string UserId { get; } = Guid.NewGuid().ToString();
    public int CategoryId { get; private set; }

    public InventoryDbContext CreateContext() => new(new DbContextOptionsBuilder<InventoryDbContext>()
        .UseSqlServer(connectionString ?? throw new InvalidOperationException("Test database has not been initialized."))
        .Options);

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("InventoryTestConnectionString");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };
        connectionString = builder.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var category = new Category("Integration tests");
        context.Categories.Add(category);
        context.Users.Add(new IdentityUser { Id = UserId, UserName = UserId, NormalizedUserName = UserId });
        await context.SaveChangesAsync();
        CategoryId = category.Id;
    }

    public async Task<Product[]> CreateProductsAsync(int count)
    {
        await using var context = CreateContext();
        var products = Enumerable.Range(1, count)
            .Select(index => new Product(Guid.NewGuid().ToString("N"), $"Product {index}", CategoryId, "cái"))
            .ToArray();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        return products;
    }

    public async Task DisposeAsync()
    {
        if (connectionString is null)
        {
            return;
        }
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != databaseName
            || !databaseName.StartsWith("InventoryManagementTests_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected test database.");
        }
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
