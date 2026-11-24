param([switch]$CreateAdmin)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inventoryRoot = Split-Path $PSScriptRoot -Parent
$inventoryPreviousEnvironment = @{}
foreach ($name in @('BootstrapAdmin__Email', 'BootstrapAdmin__Password')) {
    $inventoryPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $inventoryRoot
try {
    if (!(Test-Path -LiteralPath '.env')) {
        $sqlPassword = [Guid]::NewGuid().ToString('N') + 'aA!9'
        $signingKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        New-Item -ItemType File -Path '.env' -Value "SQL_PASSWORD=$sqlPassword`nJWT_SIGNING_KEY=$signingKey`n" | Out-Null
    }

    docker compose config --quiet
    docker compose build api
    docker compose up -d db --wait
    docker compose run --rm api --migrate

    if ($CreateAdmin) {
        if (!$env:BootstrapAdmin__Email) { $env:BootstrapAdmin__Email = Read-Host 'Admin email' }
        if (!$env:BootstrapAdmin__Password) {
            $password = Read-Host 'Admin password' -AsSecureString
            $env:BootstrapAdmin__Password = [Net.NetworkCredential]::new('', $password).Password
        }
        docker compose run --rm -e BootstrapAdmin__Email -e BootstrapAdmin__Password api --bootstrap-admin
    }

    Remove-Item Env:BootstrapAdmin__Password -ErrorAction SilentlyContinue
    docker compose up -d --no-build api
    $address = (docker compose port api 8080).Trim()
    Write-Host "Swagger is available at http://$address/swagger"
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
