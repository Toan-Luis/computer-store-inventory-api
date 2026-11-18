# Inventory Management API

An ASP.NET Core backend for a computer and accessories store with one warehouse. Stock is counted in whole units.

## Current status

The .NET 10 solution, inventory domain models, SQL Server schema, and xUnit domain tests are in place. The next milestones are catalog endpoints, JWT authentication with admin and staff roles, and transactional stock receipts and issues with movement history.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `Inventory.Api` | ASP.NET Core host and API endpoints |
| `Inventory.Application` | Application use cases |
| `Inventory.Domain` | Categories, products, stock movements, and domain rules |
| `Inventory.Infrastructure` | EF Core configuration, SQL Server migrations, and Identity storage |
| `Inventory.Tests` | xUnit tests |

## Local setup

Install the .NET 10 SDK and have a SQL Server 2019 or newer instance available. The Development configuration uses Windows authentication against `localhost` and targets `InventoryManagementDev`. For a different instance, set `ConnectionStrings__InventoryDatabase` in your environment.

From the solution directory, run:

```powershell
dotnet restore
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project src/Inventory.Infrastructure --startup-project src/Inventory.Api
dotnet test InventoryManagement.slnx
```
