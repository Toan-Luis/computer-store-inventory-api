# Inventory Management API

An ASP.NET Core backend for a computer and accessories store with one warehouse. Stock is counted in whole units.

## Current status

The .NET 10 solution, inventory domain models, SQL Server schema, and xUnit tests are in place. Category application services support listing, lookup, creation, renaming, deactivation, and reactivation through an EF Core repository. Category names are trimmed and limited to the SQL column's 120 characters.

JWT login, admin-created staff accounts, password changes, and protected category endpoints are available. Admin users manage categories and create staff accounts. Staff users can read categories. The next milestones are product endpoints and transactional stock receipts and issues with movement history.

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
$env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet ef database update --project src/Inventory.Infrastructure --startup-project src/Inventory.Api
dotnet test InventoryManagement.slnx
```

Keep the same signing key in your local environment across API restarts to preserve issued tokens. A fresh key invalidates existing tokens. The API validates the signing key at startup. The default access token lifetime is 30 minutes, configurable through `Jwt__AccessTokenMinutes` from 1 to 60.

Create the first administrator through the local bootstrap command. Supply your own email and password. Passwords require at least 12 characters including uppercase, lowercase, digit, and symbol.

```powershell
$env:BootstrapAdmin__Email = Read-Host 'Admin email'
$inventoryPassword = Read-Host 'Admin password' -AsSecureString
$env:BootstrapAdmin__Password = [System.Net.NetworkCredential]::new('', $inventoryPassword).Password
dotnet run --project src/Inventory.Api --no-launch-profile -- --bootstrap-admin
Remove-Item Env:BootstrapAdmin__Password
dotnet run --project src/Inventory.Api --launch-profile http
```

Bootstrap creates the Admin and Staff roles and the initial Admin account in one transaction. Running it again for that administrator preserves the existing password. It rejects an email already assigned to a non-admin account.

Open `http://localhost:5071/swagger` in Development. Call login, copy the returned `accessToken`, and paste it into Swagger's Authorize dialog. All catalog requests require a bearer token.

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/auth/login` | Anonymous, valid credentials required |
| GET | `/api/auth/me` | Authenticated user |
| POST | `/api/auth/change-password` | Authenticated user |
| POST | `/api/users` | Admin, creates a Staff account |
| GET | `/api/categories` | Admin or Staff |
| GET | `/api/categories/{id}` | Admin or Staff |
| POST | `/api/categories` | Admin |
| PUT | `/api/categories/{id}` | Admin |
| PATCH | `/api/categories/{id}/status` | Admin |

Login and staff creation accept `email` and `password`. Category creation and renaming accept `name`. The status endpoint accepts `isActive`. Password changes accept `currentPassword` and `newPassword`, and require a fresh login afterward. The security stamp is checked on every authenticated request so a password change invalidates previous tokens.

The API returns 400 for invalid input, 401 for missing or invalid credentials, 403 for insufficient permissions, 404 for a missing category, and 409 for a duplicate account. Login is limited to ten attempts per minute per client IP. Five failed password attempts lock an account for five minutes. For deployments behind a proxy, configure trusted forwarded headers before relying on client-IP rate limiting.

## HTTP smoke test

After building, run the following with PowerShell 7 and `sqlcmd` available. It creates a uniquely named SQL Server test database, exercises real HTTP login and category operations, and removes that database when finished.

```powershell
dotnet build InventoryManagement.slnx
./tests/ApiSmoke.ps1
```

Use `./tests/ApiSmoke.ps1 -Server 'localhost'` to select the SQL Server instance. The Windows user running the test needs permission to create and drop its temporary database.
