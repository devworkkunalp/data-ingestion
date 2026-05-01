param(
    [string]$FunctionAppName = "orchestratorSmtp",
    [string]$MasterKey = ""
)

if ([string]::IsNullOrWhiteSpace($MasterKey)) {
    Write-Host "Please provide your Master Key (Host Key). You can find this in the Azure Portal under App keys -> _master." -ForegroundColor Yellow
    Write-Host "Usage: .\Trigger-AllFunctions.ps1 -MasterKey `"YOUR_KEY`""
    exit
}

$appUrl = "https://$FunctionAppName.azurewebsites.net"

$functions = @(
    "FxRateSync",
    "UsSalarySync", "UsH1bSync", "UsUniversitySync",
    "UkSalarySync", "UkVisaSync", "UkUniversitySync",
    "CanadaSalarySync", "CanadaVisaSync", "CanadaUniSync",
    "AustraliaSalarySync", "AustraliaVisaSync", "AustraliaUniSync",
    "JapanSalarySync", "JapanVisaSync", "JapanUniSync",
    "ProjectionEngineSync"
)

Write-Host "Starting manual trigger of all 17 functions on $appUrl..." -ForegroundColor Cyan

foreach ($name in $functions) {
    Write-Host "Triggering $name... " -NoNewline
    
    $triggerUrl = "$appUrl/admin/functions/$name"
    
    try {
        $response = Invoke-RestMethod -Uri $triggerUrl -Method Post -Headers @{"x-functions-key"=$MasterKey} -Body "{}" -ContentType "application/json"
        Write-Host "Success!" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed ($($_.Exception.Message))" -ForegroundColor Red
    }
    
    # Wait a second to avoid overwhelming the HTTP connection pool
    Start-Sleep -Seconds 1
}

Write-Host "`nAll functions have been triggered asynchronously! Check the 'Invocations' tab in the Azure Portal to see their execution logs." -ForegroundColor Green
