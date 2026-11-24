param([switch]$CreateAdmin)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inventoryRoot = Split-Path $PSScriptRoot -Parent
$inventoryPreviousEnvironment = @{}
foreach ($name in @('ASPNETCORE_ENVIRONMENT', 'Jwt__SigningKey', 'BootstrapAdmin__Email', 'BootstrapAdmin__Password')) {
    $inventoryPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $inventoryRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if (!$env:Jwt__SigningKey) {
        $env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    }

    dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore --nologo
    dotnet run --project src/Inventory.Api --no-build --launch-profile http -- --migrate

    if ($CreateAdmin) {
        if (!$env:BootstrapAdmin__Email) { $env:BootstrapAdmin__Email = Read-Host 'Admin email' }
        if (!$env:BootstrapAdmin__Password) {
            $password = Read-Host 'Admin password' -AsSecureString
            $env:BootstrapAdmin__Password = [Net.NetworkCredential]::new('', $password).Password
        }
        dotnet run --project src/Inventory.Api --no-build --launch-profile http -- --bootstrap-admin
        [Environment]::SetEnvironmentVariable('BootstrapAdmin__Password', $null, 'Process')
    }

    Write-Host 'Swagger is available at http://localhost:5071/swagger'
    dotnet run --project src/Inventory.Api --no-build --launch-profile http
}
finally {
    foreach ($name in $inventoryPreviousEnvironment.Keys) {
        if ($null -eq $inventoryPreviousEnvironment[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $inventoryPreviousEnvironment[$name], 'Process')
        }
    }
    Pop-Location
}
