param([string]$Server = 'localhost', [string]$AppPath = '')

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
    [Environment]::SetEnvironmentVariable('Jwt__SigningKey', $null, 'Process')
    $env:BootstrapAdmin__Email = 'admin@example.test'
    $env:BootstrapAdmin__Password = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $inventoryStaffPassword = [Guid]::NewGuid().ToString('N') + 'aA!9'
    $inventoryNewPassword = [Guid]::NewGuid().ToString('N') + 'bB!8'
    $inventoryDll = Join-Path $inventoryRoot 'src/Inventory.Api/bin/Debug/net10.0/Inventory.Api.dll'
    if ($AppPath) { $inventoryDll = (Resolve-Path -LiteralPath $AppPath).Path }
    $inventoryOutput = New-Item -ItemType Directory -Force -Path 'obj/api-smoke'

    dotnet $inventoryDll --migrate > (Join-Path $inventoryOutput.FullName 'migration.log')
    $env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
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
    $duplicateUserUrl = "$inventoryBaseUrl/api/users"
    $duplicateUserBody = @{ email = 'concurrent-staff@example.test'; password = $inventoryStaffPassword } | ConvertTo-Json -Compress
    $duplicateUserStatuses = @(1..2 | ForEach-Object -Parallel {
        $result = Invoke-WebRequest $using:duplicateUserUrl -Method POST -ContentType 'application/json' -Body $using:duplicateUserBody `
            -Headers @{ Authorization = "Bearer $using:adminToken" } -SkipHttpErrorCheck -TimeoutSec 20
        [int]$result.StatusCode
    } -ThrottleLimit 2)
    if (@($duplicateUserStatuses | Where-Object { $_ -eq 201 }).Count -ne 1 -or @($duplicateUserStatuses | Where-Object { $_ -eq 409 }).Count -ne 1) {
        throw "Concurrent account creation returned unexpected statuses: $duplicateUserStatuses."
    }
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

    # Product catalog and transactional stock movements.
    $productA = Send-Request POST '/api/products' 201 -Token $adminToken -Body @{ sku = ' kb-001 '; name = 'Keyboard'; categoryId = $category.id; unit = 'cái'; quantityOnHand = 999 }
    $productB = Send-Request POST '/api/products' 201 -Token $adminToken -Body @{ sku = 'MS-001'; name = 'Mouse'; categoryId = $category.id; unit = 'cái' }
    if ($productA.sku -ne 'KB-001' -or $productA.quantityOnHand -ne 0) { throw 'Product SKU or initial stock is invalid.' }
    Send-Request POST '/api/products' 409 -Token $adminToken -Body @{ sku = 'kb-001'; name = 'Duplicate'; categoryId = $category.id; unit = 'cái' }
    Send-Request POST '/api/products' 400 -Token $adminToken -Body @{ sku = 'BAD'; name = 'Bad category'; categoryId = 2147483647; unit = 'cái' }
    Send-Request POST '/api/products' 403 -Token $staffToken -Body @{ sku = 'FORBIDDEN'; name = 'Forbidden'; categoryId = $category.id; unit = 'cái' }
    Send-Request GET '/api/products?pageSize=0' 400 -Token $staffToken
    $products = @(Send-Request GET '/api/products?search=KB-001' 200 -Token $staffToken)
    if ($products.Count -ne 1 -or $products[0].id -ne $productA.id) { throw 'Product search failed.' }
    $productPath = '/api/products/' + $productA.id
    $updated = Send-Request PUT $productPath 200 -Token $adminToken -Body @{ name = 'Mechanical keyboard'; categoryId = $category.id; quantityOnHand = 999 }
    if ($updated.name -ne 'Mechanical keyboard' -or $updated.quantityOnHand -ne 0) { throw 'Product update allowed direct stock editing.' }

    Send-Request POST '/api/stock-movements/receipts' 401 -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }) }
    Send-Request POST '/api/stock-movements/receipts' 400 -Token $staffToken -Body @{ lines = @() }
    Send-Request POST '/api/stock-movements/receipts' 400 -Token $staffToken -Body @{ lines = @($null) }
    Send-Request POST '/api/stock-movements/receipts' 400 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 0 }) }
    Send-Request POST '/api/stock-movements/receipts' 400 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }, @{ productId = $productA.id; quantity = 1 }) }
    $receipt = Send-Request POST '/api/stock-movements/receipts' 201 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 10 }, @{ productId = $productB.id; quantity = 2 }); createdByUserId = 'spoofed' }
    $staffProfile = Send-Request GET '/api/auth/me' 200 -Token $staffToken
    if ($receipt.createdByUserId -ne $staffProfile.id -or $receipt.lines.Count -ne 2) { throw 'Movement actor or lines were not persisted.' }
    $issue = Send-Request POST '/api/stock-movements/issues' 201 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 4 }, @{ productId = $productB.id; quantity = 1 }) }
    $balance = Send-Request GET $productPath 200 -Token $staffToken
    if ($balance.quantityOnHand -ne 6) { throw 'Receipt and issue did not produce the expected balance.' }
    $historyBefore = @(Send-Request GET '/api/stock-movements' 200 -Token $staffToken)
    Send-Request POST '/api/stock-movements/issues' 409 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }, @{ productId = $productB.id; quantity = 99 }) }
    Send-Request POST '/api/stock-movements/receipts' 404 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }, @{ productId = 2147483647; quantity = 1 }) }
    $balance = Send-Request GET $productPath 200 -Token $staffToken
    $historyAfter = @(Send-Request GET '/api/stock-movements' 200 -Token $staffToken)
    if ($balance.quantityOnHand -ne 6 -or $historyAfter.Count -ne $historyBefore.Count) { throw 'Failed multi-line movement left partial stock or history.' }
    Send-Request PATCH "$productPath/status" 200 -Token $adminToken -Body @{ isActive = $false } | Out-Null
    Send-Request POST '/api/stock-movements/receipts' 409 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }) }
    Send-Request POST '/api/stock-movements/issues' 201 -Token $staffToken -Body @{ lines = @(@{ productId = $productA.id; quantity = 1 }) } | Out-Null
    $balance = Send-Request GET $productPath 200 -Token $staffToken
    if ($balance.quantityOnHand -ne 5) { throw 'Inactive product could not be sold from remaining stock.' }
    $issues = @(Send-Request GET "/api/stock-movements?type=2&productId=$($productA.id)" 200 -Token $staffToken)
    if ($issues.Count -ne 2 -or ($issues | Where-Object { $_.type -ne 2 })) { throw 'Movement history filtering failed.' }
    Send-Request GET "/api/stock-movements/$($receipt.id)" 200 -Token $staffToken | Out-Null
    Send-Request GET '/api/stock-movements?type=99' 400 -Token $staffToken
    Send-Request GET '/api/stock-movements?from=2026-12-01&to=2026-01-01' 400 -Token $staffToken

    # Two concurrent issues each ask for four units from a stock of five.
    $raceProduct = Send-Request POST '/api/products' 201 -Token $adminToken -Body @{ sku = 'RACE'; name = 'Concurrency test'; categoryId = $category.id; unit = 'cái' }
    Send-Request POST '/api/stock-movements/receipts' 201 -Token $staffToken -Body @{ lines = @(@{ productId = $raceProduct.id; quantity = 5 }) } | Out-Null
    $raceBody = @{ lines = @(@{ productId = $raceProduct.id; quantity = 4 }) } | ConvertTo-Json -Compress
    $raceUrl = "$inventoryBaseUrl/api/stock-movements/issues"
    $raceStatuses = @(1..2 | ForEach-Object -Parallel {
        $result = Invoke-WebRequest $using:raceUrl -Method POST -ContentType 'application/json' -Body $using:raceBody `
            -Headers @{ Authorization = "Bearer $using:staffToken" } -SkipHttpErrorCheck -TimeoutSec 20
        [int]$result.StatusCode
    } -ThrottleLimit 2)
    $raceBalance = Send-Request GET "/api/products/$($raceProduct.id)" 200 -Token $staffToken
    if (@($raceStatuses | Where-Object { $_ -eq 201 }).Count -ne 1 -or @($raceStatuses | Where-Object { $_ -eq 409 }).Count -ne 1 -or $raceBalance.quantityOnHand -ne 1) {
        throw "Concurrent stock issue failed. Statuses $raceStatuses, balance $($raceBalance.quantityOnHand)."
    }

    $limitProduct = Send-Request POST '/api/products' 201 -Token $adminToken -Body @{ sku = 'LIMIT'; name = 'Integer limit test'; categoryId = $category.id; unit = 'cái' }
    Send-Request POST '/api/stock-movements/receipts' 201 -Token $staffToken -Body @{ lines = @(@{ productId = $limitProduct.id; quantity = 2147483647 }) } | Out-Null
    Send-Request POST '/api/stock-movements/receipts' 409 -Token $staffToken -Body @{ lines = @(@{ productId = $limitProduct.id; quantity = 1 }) }

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
    Write-Output 'API smoke passed: authentication, roles, catalog, multi-line stock transactions, rollback, concurrent issues, history, password changes and login protection.'
}
finally {
    if ($inventoryApp -and !$inventoryApp.HasExited) { Stop-Process -Id $inventoryApp.Id -Force }
    if ($inventoryDatabase -notmatch '^InventoryManagementSmoke_[a-f0-9]{32}$') { throw 'Unexpected smoke database name.' }
    sqlcmd -S $Server -E -C -b -l 5 -Q "IF DB_ID(N'$inventoryDatabase') IS NOT NULL BEGIN ALTER DATABASE [$inventoryDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$inventoryDatabase]; END"
    foreach ($inventoryName in $inventoryEnvironmentNames) {
        if ($null -eq $inventoryEnvironment[$inventoryName]) {
            Remove-Item -LiteralPath "Env:$inventoryName" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($inventoryName, $inventoryEnvironment[$inventoryName], 'Process')
        }
    }
    Pop-Location
}
