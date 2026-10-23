<#
Checks a JMeter dashboard generated with its default percentile settings
(90, 95, 99). pct2ResTime therefore represents p95.
Exit codes: 0 = targets met, 1 = targets missed, 2 = invalid input.
Targets are provisional assessment targets, not an agreed service SLA.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StatisticsPath,

    [string]$TransactionName = 'Total',

    [ValidateRange(1, 2147483647)]
    [int]$MaxP95Ms = 2000,

    [ValidateRange(0.000001, 100)]
    [double]$MaxErrorPercent = 1
)

$ErrorActionPreference = 'Stop'

try {
    $statistics = Get-Content -LiteralPath $StatisticsPath -Raw | ConvertFrom-Json
    $total = $statistics.PSObject.Properties[$TransactionName].Value
    if ($null -eq $total) { throw "Statistics for '$TransactionName' are missing." }

    foreach ($field in @('sampleCount', 'pct2ResTime', 'errorPct')) {
        if ($null -eq $total.$field) { throw "Missing required field: $field" }
    }

    $samples = [double]$total.sampleCount
    $p95 = [double]$total.pct2ResTime
    $errorPercent = [double]$total.errorPct

    foreach ($value in @($samples, $p95, $errorPercent)) {
        if ([double]::IsNaN($value) -or [double]::IsInfinity($value)) {
            throw 'Statistics must contain finite numbers.'
        }
    }
    if ($samples -le 0 -or $samples -ne [math]::Floor($samples)) {
        throw 'A positive integer sample count is required.'
    }
    if ($p95 -lt 0 -or $errorPercent -lt 0 -or $errorPercent -gt 100) {
        throw 'Statistics contain an invalid response time or error percentage.'
    }

    $latencyPassed = $p95 -le $MaxP95Ms
    $errorsPassed = $errorPercent -lt $MaxErrorPercent

    Write-Output "Scope: $TransactionName"
    Write-Output "Requests: $samples"
    Write-Output "p95: $p95 ms; target <= $MaxP95Ms ms; met: $latencyPassed"
    Write-Output "Errors: $errorPercent%; target < $MaxErrorPercent%; met: $errorsPassed"

    if ($latencyPassed -and $errorsPassed) {
        Write-Output 'PERFORMANCE CHECK: PASS'
        exit 0
    }

    Write-Output 'PERFORMANCE CHECK: FAIL'
    exit 1
}
catch {
    Write-Output "PERFORMANCE CHECK: INVALID INPUT - $($_.Exception.Message)"
    exit 2
}
