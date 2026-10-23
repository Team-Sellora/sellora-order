<# Read-only snapshot: one lookup for each order in the existing ten-order batch. #>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ledgerPath = Join-Path $projectRoot 'jmeter-results\create-load-20261018-145949\Created-Orders.csv'
$baseUrl = 'https://sellora-notification-staging-g6csbfcqc6gmhtgd.eastasia-01.azurewebsites.net'
if ([string]::IsNullOrWhiteSpace($env:SELLORA_ADMIN_ACCESS_TOKEN)) {
    throw 'Set SELLORA_ADMIN_ACCESS_TOKEN in this PowerShell window first.'
}
$orders = @(Import-Csv -LiteralPath $ledgerPath)
if ($orders.Count -ne 10 -or @($orders.OrderId | Sort-Object -Unique).Count -ne 10) {
    throw 'Expected the ten distinct orders from the saved batch.'
}
$folder = Join-Path $projectRoot ('jmeter-results\notification-correlation-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $folder | Out-Null
$headers = @{ Authorization = 'Bearer ' + $env:SELLORA_ADMIN_ACCESS_TOKEN; Accept = 'application/json' }

function Get-MillisecondsBetween($start, $end) {
    if ($null -eq $start -or $null -eq $end -or "$start" -eq '' -or "$end" -eq '') { return $null }
    return [math]::Round((([DateTimeOffset]$end) - ([DateTimeOffset]$start)).TotalMilliseconds, 2)
}

$summary = @()
$snapshots = @()
foreach ($order in $orders) {
    $id = [guid]$order.OrderId
    try {
        $response = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/notifications?orderId=$id&page=1&pageSize=100" -Headers $headers -TimeoutSec 20
        $items = @($response.items | Where-Object { $_.eventType -eq 'OrderPlaced' -and $_.orderId -eq "$id" })
        if ($response.totalCount -gt 100) { throw 'Order query exceeds one page; inspect pagination before interpreting this order.' }
        if ($items.Count -eq 0) {
            $summary += [pscustomobject]@{ OrderId="$id"; OrderReference=$order.OrderReference; NotificationRequestId=''; SourceEventId=''; Status='NotFound'; RequestsForOrder=0; RecipientCount=0; SentRecipients=0; ExpectedKindsPresent=$false; FullySent=$false; IntakeLagMs=$null; EventToCompletionMs=$null; SendGapMs=$null; WithinTolerance=$null }
            continue
        }
        foreach ($item in $items) {
            $recipients = @($item.recipients)
            $sent = @($recipients | Where-Object { $_.deliveryStatus -eq 'Sent' }).Count
            $kinds = @($recipients | ForEach-Object { $_.kind })
            # The domain names these recipients Shop and Agency; retain actual
            # kinds in snapshots so any deployed-contract discrepancy is visible.
            $expected = $kinds -contains 'Shop' -and $kinds -contains 'Agency'
            $complete = $item.status -eq 'Sent' -and $recipients.Count -eq 2 -and $sent -eq 2 -and $expected
            $summary += [pscustomobject]@{
                OrderId="$id"; OrderReference=$order.OrderReference
                NotificationRequestId=$item.notificationRequestId; SourceEventId=$item.sourceEventId
                Status=$item.status; RequestsForOrder=$items.Count
                RecipientCount=$recipients.Count; SentRecipients=$sent
                ExpectedKindsPresent=$expected; FullySent=$complete
                IntakeLagMs=(Get-MillisecondsBetween $item.occurredAt $item.receivedAt)
                EventToCompletionMs=$(if ($complete) { Get-MillisecondsBetween $item.occurredAt $item.dispatch.completedAt } else { $null })
                SendGapMs=$item.dispatch.sendGapMilliseconds; WithinTolerance=$item.dispatch.withinTolerance
            }
            $snapshots += [pscustomobject]@{
                orderId=$item.orderId; notificationRequestId=$item.notificationRequestId
                sourceEventId=$item.sourceEventId; eventType=$item.eventType; status=$item.status
                occurredAt=$item.occurredAt; receivedAt=$item.receivedAt; dispatch=$item.dispatch
                recipients=@($recipients | Select-Object kind,recipientId,deliveryStatus,sentAt,attempts,lastAttemptAt)
            }
        }
    }
    catch {
        $status = 'LookupError'
        if ($null -ne $_.Exception.Response) { $status = 'HTTP ' + [int]$_.Exception.Response.StatusCode }
        $summary += [pscustomobject]@{ OrderId="$id"; OrderReference=$order.OrderReference; NotificationRequestId=''; SourceEventId=''; Status=$status; RequestsForOrder=$null; RecipientCount=$null; SentRecipients=$null; ExpectedKindsPresent=$false; FullySent=$false; IntakeLagMs=$null; EventToCompletionMs=$null; SendGapMs=$null; WithinTolerance=$null }
        Write-Warning "Order $id lookup was inconclusive: $status"
    }
}
$summary | Export-Csv -LiteralPath (Join-Path $folder 'Order-Notification-Correlation.csv') -NoTypeInformation -Encoding UTF8
@{ observedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceLedger=$ledgerPath; notifications=$snapshots } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder 'Notification-Snapshots.json') -Encoding UTF8
$summary | Select-Object OrderReference,Status,RequestsForOrder,SentRecipients,FullySent,EventToCompletionMs | Format-Table -AutoSize
Write-Host ('Fully sent orders with exactly one matching request: ' + @($summary | Where-Object { $_.FullySent -and $_.RequestsForOrder -eq 1 }).Count + ' / 10')
Write-Host "Evidence saved: $folder"
Write-Host 'This is a later snapshot with stored processing timestamps, not a live backlog-drain or inbox-delivery measurement.'
