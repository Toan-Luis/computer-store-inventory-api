param([string]$Server = 'localhost')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inventoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $inventoryRoot
$inventoryDatabase = 'InventoryManagementSmoke_' + [Guid]::NewGuid().ToString('N')
$inventoryApp = $null
$inventoryEnvironment = @{}
$inventoryEnvironmentNames = @(
    'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__InventoryDatabase',
    'Jwt__SigningKey', 'BootstrapAdmin__Email', 'BootstrapAdmin__Password', 'NUGET_PACKAGES')
foreach ($inventoryName in $inventoryEnvironmentNames) {
    $inventoryEnvironment[$inventoryName] = [Environment]::GetEnvironmentVariable($inventoryName, 'Process')
}

function Send-Request {
    param([string]$Method, [string]$Path, [int]$ExpectedStatus, $Body = $null, [string]$Token = '')

    $arguments = @{
        Uri = "$inventoryBaseUrl$Path"
        Method = $Method
        SkipHttpErrorCheck = $true
        TimeoutSec = 15
    }
    if ($Token) { $arguments.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Compress
    }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -ne $ExpectedStatus) {
        throw "$Method $Path expected $ExpectedStatus but received $($response.StatusCode)."
    }
    if ($ExpectedStatus -ge 400) { return }
    if ($response.Content -and $response.Headers['Content-Type'] -match 'json') {
        $content = $response.Content
        if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
        return $content | ConvertFrom-Json
    }
}

try {
    $env:NUGET_PACKAGES = Join-Path $inventoryRoot 'obj/nuget-packages'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__InventoryDatabase = "Server=$Server;Database=$inventoryDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    $env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:BootstrapAdmin__Email = 'admin@example.test'
    $env:BootstrapAdmin__Password = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $inventoryStaffPassword = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $inventoryNewPassword = [Guid]::NewGuid().ToString('N') + 'bB!8'
    $inventoryDll = Join-Path $inventoryRoot 'src/Inventory.Api/bin/Debug/net10.0/Inventory.Api.dll'
    $inventoryOutput = New-Item -ItemType Directory -Force -Path 'obj/api-smoke'

    dotnet tool run dotnet-ef database update --project src/Inventory.Infrastructure --startup-project src/Inventory.Api --no-build > (Join-Path $inventoryOutput.FullName 'migration.log')
    dotnet $inventoryDll --bootstrap-admin > (Join-Path $inventoryOutput.FullName 'bootstrap.log')
    dotnet $inventoryDll --bootstrap-admin >> (Join-Path $inventoryOutput.FullName 'bootstrap.log')

    # Ask Windows for an available loopback port before starting the API.
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $inventoryPort = $listener.LocalEndpoint.Port
    $listener.Stop()
    $inventoryBaseUrl = "http://127.0.0.1:$inventoryPort"
    $env:ASPNETCORE_URLS = $inventoryBaseUrl
    $inventoryApp = Start-Process dotnet -ArgumentList @($inventoryDll) -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $inventoryOutput.FullName 'stdout.log') `
        -RedirectStandardError (Join-Path $inventoryOutput.FullName 'stderr.log')
    $inventoryReady = $false
    foreach ($attempt in 1..40) {
        if ($inventoryApp.HasExited) { throw 'API process stopped before becoming ready. Check obj/api-smoke logs.' }
        try {
            $response = Invoke-WebRequest "$inventoryBaseUrl/swagger/v1/swagger.json" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $inventoryReady = $true; break }
        }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$inventoryReady) { throw 'API did not become ready.' }

    $schema = $response.Content | ConvertFrom-Json
    if (!$schema.paths.'/api/categories' -or !$schema.components.securitySchemes.Bearer) {
        throw 'Swagger is missing category routes or Bearer authentication.'
    }

    Send-Request GET '/api/categories' 401
    Send-Request GET '/api/categories' 401 -Token 'invalid.token.value'
    Send-Request POST '/api/auth/login' 401 -Body @{ email = $env:BootstrapAdmin__Email; password = 'WrongPassword!9' }
    $adminLogin = Send-Request POST '/api/auth/login' 200 -Body @{ email = $env:BootstrapAdmin__Email; password = $env:BootstrapAdmin__Password }
    $adminToken = $adminLogin.accessToken
    $profile = Send-Request GET '/api/auth/me' 200 -Token $adminToken
    if ($profile.roles -notcontains 'Admin') { throw 'Bootstrap account lacks the Admin role.' }

    $staff = Send-Request POST '/api/users' 201 -Token $adminToken -Body @{ email = 'staff@example.test'; password = $inventoryStaffPassword; role = 'Admin' }
    if ($staff.role -ne 'Staff') { throw 'Employee creation allowed privilege escalation.' }
    Send-Request POST '/api/users' 409 -Token $adminToken -Body @{ email = 'STAFF@example.test'; password = $inventoryStaffPassword }
    Send-Request POST '/api/users' 400 -Token $adminToken -Body @{ email = 'weak@example.test'; password = 'weak' }
    $staffLogin = Send-Request POST '/api/auth/login' 200 -Body @{ email = 'staff@example.test'; password = $inventoryStaffPassword }
    $staffToken = $staffLogin.accessToken
    Send-Request POST '/api/users' 403 -Token $staffToken -Body @{ email = 'other@example.test'; password = $inventoryStaffPassword }
    Send-Request POST '/api/categories' 403 -Token $staffToken -Body @{ name = 'Forbidden' }
    Send-Request POST '/api/categories' 400 -Token $adminToken -Body @{ name = '   ' }
    Send-Request POST '/api/categories' 400 -Token $adminToken -Body @{ name = ('x' * 121) }
    $category = Send-Request POST '/api/categories' 201 -Token $adminToken -Body @{ name = '  Keyboards  ' }
    if ($category.name -ne 'Keyboards') { throw 'Category name was not trimmed.' }
    $categoryPath = '/api/categories/' + $category.id
    Send-Request GET $categoryPath 200 -Token $staffToken | Out-Null
    Send-Request GET '/api/categories/0' 400 -Token $staffToken
    Send-Request GET '/api/categories/2147483647' 404 -Token $staffToken
    Send-Request PUT $categoryPath 403 -Token $staffToken -Body @{ name = 'Blocked' }
    Send-Request PATCH "$categoryPath/status" 403 -Token $staffToken -Body @{ isActive = $false }
    Send-Request PATCH "$categoryPath/status" 400 -Token $adminToken -Body @{}
    $renamed = Send-Request PUT $categoryPath 200 -Token $adminToken -Body @{ name = 'Mechanical keyboards' }
    if ($renamed.name -ne 'Mechanical keyboards') { throw 'Category rename failed.' }
    $inactive = Send-Request PATCH "$categoryPath/status" 200 -Token $adminToken -Body @{ isActive = $false }
    if ($inactive.isActive) { throw 'Category deactivation failed.' }
    $listed = Send-Request GET '/api/categories' 200 -Token $staffToken
    if (!($listed | Where-Object { $_.id -eq $category.id -and !$_.isActive })) { throw 'Inactive category was lost.' }
    $active = Send-Request PATCH "$categoryPath/status" 200 -Token $adminToken -Body @{ isActive = $true }
    if (!$active.isActive) { throw 'Category reactivation failed.' }

    Send-Request POST '/api/auth/change-password' 400 -Token $staffToken -Body @{ currentPassword = 'WrongPassword!9'; newPassword = $inventoryNewPassword }
    Send-Request POST '/api/auth/change-password' 204 -Token $staffToken -Body @{ currentPassword = $inventoryStaffPassword; newPassword = $inventoryNewPassword }
    Send-Request GET '/api/auth/me' 401 -Token $staffToken
    Send-Request POST '/api/auth/login' 401 -Body @{ email = 'staff@example.test'; password = $inventoryStaffPassword }
    $newLogin = Send-Request POST '/api/auth/login' 200 -Body @{ email = 'staff@example.test'; password = $inventoryNewPassword }
    Send-Request GET '/api/categories' 200 -Token $newLogin.accessToken | Out-Null
    foreach ($attempt in 1..5) {
        Send-Request POST '/api/auth/login' 401 -Body @{ email = 'staff@example.test'; password = 'WrongPassword!9' }
    }
    sqlcmd -S $Server -E -C -b -l 5 -d $inventoryDatabase -Q "IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE NormalizedUserName = N'STAFF@EXAMPLE.TEST' AND LockoutEnd > SYSUTCDATETIME()) THROW 50000, 'Account lockout was not persisted.', 1;"
    # This next attempt reaches the per-IP rate limit set to ten logins/minute.
    Send-Request POST '/api/auth/login' 429 -Body @{ email = 'staff@example.test'; password = $inventoryNewPassword }
    Write-Output 'API smoke passed: JWT login, roles, account creation, validation, category lifecycle, password change, token revocation, account lockout and rate limiting.'
}
finally {
    if ($inventoryApp -and !$inventoryApp.HasExited) { Stop-Process -Id $inventoryApp.Id -Force }
    if ($inventoryDatabase -notmatch '^InventoryManagementSmoke_[a-f0-9]{32}$') { throw 'Unexpected smoke database name.' }
    sqlcmd -S $Server -E -C -b -l 5 -Q "IF DB_ID(N'$inventoryDatabase') IS NOT NULL BEGIN ALTER DATABASE [$inventoryDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$inventoryDatabase]; END"
    foreach ($inventoryName in $inventoryEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($inventoryName, $inventoryEnvironment[$inventoryName], 'Process')
    }
    Pop-Location
}
