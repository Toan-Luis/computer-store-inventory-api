using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("InventoryDatabase")
        ?? throw new InvalidOperationException("Connection string 'InventoryDatabase' is required.");

    options.UseSqlServer(connection, sql => sql.UseCompatibilityLevel(150));
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
