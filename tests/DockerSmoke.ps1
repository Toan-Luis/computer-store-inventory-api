$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inventoryRoot = Split-Path $PSScriptRoot -Parent
$inventoryProject = 'inventory-smoke-' + [Guid]::NewGuid().ToString('N')
$inventoryEnvironment = @{}
foreach ($name in @('SQL_PASSWORD', 'JWT_SIGNING_KEY', 'API_PORT', 'BootstrapAdmin__Email', 'BootstrapAdmin__Password')) {
    $inventoryEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$compose = @('compose', '--project-name', $inventoryProject, '--file', (Join-Path $inventoryRoot 'compose.yaml'))
Push-Location $inventoryRoot

function Send-DockerRequest {
    param([string]$Method, [string]$Path, [int]$ExpectedStatus, $Body = $null, [string]$Token = '')
    $arguments = @{ Uri = "$inventoryBaseUrl$Path"; Method = $Method; SkipHttpErrorCheck = $true; TimeoutSec = 20 }
    if ($Token) { $arguments.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Compress
    }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -ne $ExpectedStatus) {
        throw "$Method $Path expected $ExpectedStatus, received $($response.StatusCode)."
    }
    if ($ExpectedStatus -lt 400 -and $response.Content) {
        $content = $response.Content
        if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
        return $content | ConvertFrom-Json
    }
}

function Wait-DockerApi {
    foreach ($attempt in 1..40) {
        try {
            Invoke-WebRequest "$inventoryBaseUrl/swagger/v1/swagger.json" -TimeoutSec 2 | Out-Null
            return
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    throw 'Docker API did not become ready.'
}

try {
    $env:SQL_PASSWORD = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $env:JWT_SIGNING_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:API_PORT = '0'
    $env:BootstrapAdmin__Email = 'docker-admin@example.test'
    $env:BootstrapAdmin__Password = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $staffPassword = [Guid]::NewGuid().ToString('N') + 'bB!8'
    $outputDirectory = New-Item -ItemType Directory -Force -Path obj/docker-smoke

    & docker @compose up -d db --wait
    & docker @compose run --rm api --migrate > (Join-Path $outputDirectory.FullName 'migration.log')
    & docker @compose run --rm -e BootstrapAdmin__Email -e BootstrapAdmin__Password api --bootstrap-admin > (Join-Path $outputDirectory.FullName 'bootstrap.log')
    & docker @compose up -d --no-build api
    $address = (& docker @compose port api 8080).Trim()
    $inventoryBaseUrl = "http://$address"
    Wait-DockerApi

    Send-DockerRequest GET '/api/products' 401
    $admin = Send-DockerRequest POST '/api/auth/login' 200 -Body @{ email = $env:BootstrapAdmin__Email; password = $env:BootstrapAdmin__Password }
    Send-DockerRequest POST '/api/users' 201 -Token $admin.accessToken -Body @{ email = 'docker-staff@example.test'; password = $staffPassword } | Out-Null
    $staff = Send-DockerRequest POST '/api/auth/login' 200 -Body @{ email = 'docker-staff@example.test'; password = $staffPassword }
    $category = Send-DockerRequest POST '/api/categories' 201 -Token $admin.accessToken -Body @{ name = 'Keyboards' }
    $body = @{ sku = 'DOCKER-KB'; name = 'Keyboard'; categoryId = $category.id; unit = 'cái' }
    Send-DockerRequest POST '/api/products' 403 -Token $staff.accessToken -Body $body
    $product = Send-DockerRequest POST '/api/products' 201 -Token $admin.accessToken -Body $body
    Send-DockerRequest POST '/api/stock-movements/receipts' 201 -Token $staff.accessToken -Body @{ lines = @(@{ productId = $product.id; quantity = 10 }) } | Out-Null
    Send-DockerRequest POST '/api/stock-movements/issues' 201 -Token $staff.accessToken -Body @{ lines = @(@{ productId = $product.id; quantity = 4 }) } | Out-Null
    Send-DockerRequest POST '/api/stock-movements/issues' 409 -Token $staff.accessToken -Body @{ lines = @(@{ productId = $product.id; quantity = 7 }) }
    $balance = Send-DockerRequest GET "/api/products/$($product.id)" 200 -Token $staff.accessToken
    if ($balance.quantityOnHand -ne 6) { throw 'Docker stock balance is incorrect.' }

    # Recreate containers while retaining the volume to verify data persistence.
    & docker @compose down
    & docker @compose up -d --no-build --wait
    $address = (& docker @compose port api 8080).Trim()
    $inventoryBaseUrl = "http://$address"
    Wait-DockerApi
    $balance = Send-DockerRequest GET "/api/products/$($product.id)" 200 -Token $staff.accessToken
    if ($balance.quantityOnHand -ne 6) { throw 'Docker volume did not preserve stock.' }
    Write-Output 'Docker smoke passed: migration, admin bootstrap, login, roles, stock movements and volume persistence.'
}
finally {
    try {
        if ($inventoryProject -notmatch '^inventory-smoke-[a-f0-9]{32}$') { throw 'Unexpected Docker test project name.' }
        & docker @compose down --volumes
    }
    finally {
        foreach ($name in $inventoryEnvironment.Keys) {
            if ($null -eq $inventoryEnvironment[$name]) {
                Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($name, $inventoryEnvironment[$name], 'Process')
            }
        }
        Pop-Location
    }
}
