<#
Runs one Order List workload level. Execute from a shell containing
SELLORA_ACCESS_TOKEN. Reports exclude the first 30 seconds by sample start.
This explores endpoint capacity for one shared identity, not whole-system capacity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 100)]
    [int]$Users,
    [string]$JMeterPath = 'C:\tools\apache-jmeter-5.6.3\bin\jmeter.bat',
    [switch]$DirectAzure
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$planPath = Join-Path $projectRoot 'List Orders.jmx'
if ($DirectAzure) { $planPath = Join-Path $projectRoot 'List Orders Direct Azure.jmx' }
$checkerPath = Join-Path $PSScriptRoot 'Check-Performance.ps1'

if ([string]::IsNullOrWhiteSpace($env:SELLORA_ACCESS_TOKEN)) {
    throw 'Set SELLORA_ACCESS_TOKEN in this PowerShell window before running.'
}
foreach ($path in @($JMeterPath, $planPath, $checkerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing file: $path" }
}

$targetLabel = 'gateway'
if ($DirectAzure) { $targetLabel = 'direct-azure' }
$runFolder = Join-Path $projectRoot ('jmeter-results\capacity-' + $targetLabel + '-' + $Users + 'users-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runFolder | Out-Null
$rawPath = Join-Path $runFolder 'requests.jtl'
$steadyPath = Join-Path $runFolder 'steady-requests.jtl'
$reportPath = Join-Path $runFolder 'html'

@{
    users = $Users
    target = $targetLabel
    durationSeconds = 180
    rampupSeconds = 10
    thinkTimeMs = 1000
    warmupExcludedSeconds = 30
    measurement = 'Requests starting 30 to 180 seconds after first sample start'
    identityModel = 'Concurrent clients sharing one Sales Rep token'
    targets = 'Provisional: p95 <= 2000 ms, errors < 1%'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runFolder 'workload.json') -Encoding UTF8
Copy-Item -LiteralPath $planPath -Destination (Join-Path $runFolder 'plan-used.jmx')

Write-Host "Starting $Users clients for 180 seconds. Keep this window open."
Write-Host "Output: $runFolder"
& $JMeterPath -n -t $planPath "-Jusers=$Users" '-Jrampup=10' '-Jduration=180' '-JthinkTime=1000' '-Jjmeter.save.saveservice.output_format=csv' '-Jjmeter.save.saveservice.print_field_names=true' '-Jjmeter.save.saveservice.timestamp_format=ms' -l $rawPath -j (Join-Path $runFolder 'jmeter.log')
if ($LASTEXITCODE -ne 0) { throw "JMeter execution failed. See $runFolder\jmeter.log" }

$all = @(Import-Csv -LiteralPath $rawPath)
if ($all.Count -eq 0) { throw 'No requests recorded; no capacity conclusion is possible.' }
foreach ($field in @('timeStamp','elapsed','responseCode','success')) {
    if ($field -notin $all[0].PSObject.Properties.Name) { throw "Missing JTL column: $field" }
}
$firstStart = ($all | ForEach-Object { [long]$_.timeStamp } | Measure-Object -Minimum).Minimum
$steady = @($all | Where-Object {
    [long]$_.timeStamp -ge ($firstStart + 30000) -and
    [long]$_.timeStamp -lt ($firstStart + 180000)
})
if ($steady.Count -eq 0) { throw 'No requests in the measurement window; no capacity conclusion is possible.' }
# Windows PowerShell Export-Csv quotes header names and adds a UTF-8 BOM.
# Preserve JMeter's original unquoted header so report generation recognizes it.
$csvLines = @($steady | ConvertTo-Csv -NoTypeInformation)
$csvLines[0] = Get-Content -LiteralPath $rawPath -TotalCount 1
[System.IO.File]::WriteAllLines($steadyPath, [string[]]$csvLines, [System.Text.UTF8Encoding]::new($false))

& $JMeterPath -g $steadyPath -o $reportPath '-Jjmeter.reportgenerator.overall_granularity=5000' '-Jaggregate_rpt_pct1=90' '-Jaggregate_rpt_pct2=95' '-Jaggregate_rpt_pct3=99' -j (Join-Path $runFolder 'report.log')
if ($LASTEXITCODE -ne 0) { throw "Report generation failed. See $runFolder\report.log" }

powershell.exe -NoProfile -ExecutionPolicy Bypass -File $checkerPath -StatisticsPath (Join-Path $reportPath 'statistics.json')
$gateCode = $LASTEXITCODE
$steady | Group-Object responseCode | Select-Object Name,Count | Format-Table -AutoSize
Write-Host 'Report covers the measurement window, not the warmup.'
Write-Host "Open report: $reportPath\index.html"

# Invalid authentication is a setup failure. Throttling is a gateway-path limit,
# not evidence of backend saturation. Surface both before increasing load.
if (@($all | Where-Object { $_.responseCode -in @('401','403') }).Count -gt 0) {
    Write-Host 'Authentication or authorization errors occurred. Fix these before drawing a capacity conclusion.'
    exit 2
}
if (@($all | Where-Object { $_.responseCode -eq '429' }).Count -gt 0) {
    Write-Host '429 throttling occurred. Inspect the gateway policy before increasing load.'
}
$span = ($steady | ForEach-Object { [long]$_.timeStamp } | Measure-Object -Maximum -Minimum)
if ($steady.Count -lt 30 -or ($span.Maximum - $span.Minimum) -lt 120000) {
    Write-Host 'Insufficient samples or measurement coverage. Treat this run as inconclusive.'
    exit 2
}
Write-Host 'Repeat boundary workloads before claiming the highest acceptable tested level.'
exit $gateCode
