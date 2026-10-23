# Sprint 3 load-testing contribution — progress at 18 October 2026

Repository location updated 23 October 2026: this material is now under `sellora-order/performance`. Run the commands below from the `performance` directory. References to the project root below mean that performance workspace root. Original test dates and measurements are unchanged.

## Implemented scope

Authenticated Order List testing through WSO2 API Manager and directly against Azure, a bounded ten-order create/retrieve batch, Notification List read load and correlation of notification processing records. JMeter 5.6.3 clients share one Sales Rep account for Order and one Company Admin account for Notification. This does not test distinct user identities, login capacity or payment.

The plan is `../../List Orders.jmx`. It checks status 200 and the existence of the JSON `items` field. This structural check does not establish the correctness of every order returned. Authorization reads `SELLORA_ACCESS_TOKEN` from the environment. Do not save real tokens in source control.

Parameters: `users` (default 10), `rampup` (5 seconds), `duration` (60 seconds), `thinkTime` (1000 milliseconds). The plan uses infinite loops with a duration limit, a 5000 ms connect timeout and a 10000 ms response timeout. Timeouts were added after the recorded runs below. They are not strict total transaction deadlines.

## Saved evidence

Paths below are relative to the project root. Preserve complete report folders, not just index.html.

| Run folder under jmeter-results | Samples | Errors | p95 ms | Provisional gate |
| --- | ---: | ---: | ---: | --- |
| 20261018-111639 | 343 | 0% | 1731.0 | PASS |
| orders-10users-20261018-114507 | 257 | 0% | 3563.1 | FAIL |
| 20261018-110949 | 432 | 100% | Not valid for successful-operation comparison | Authentication failure: 401 |

Both successful runs used the intended 10-client, 60-second workload with a 5-second ramp-up and a 1-second pause. Statistics include startup. Results vary between runs; no bottleneck or capacity limit has been established. Reports measure the full client/network/gateway/service path. The green dashboard request summary is not a performance acceptance decision.

Existing CSV summaries in the project root: `orders-5users-60s-aggregate.csv` and `orders-10users-60s-aggregate.csv`. These describe earlier GUI runs, not necessarily the command-line runs above.

## Repeat a run from PowerShell

Open PowerShell in the project root. Java and JMeter must be installed, the test environment must be reachable, and the Sales Rep test user must have access to Order List. Obtain a fresh access token through the team's existing login process.

```powershell
$secureToken = Read-Host 'Paste access token WITHOUT Bearer' -AsSecureString
$env:SELLORA_ACCESS_TOKEN = [System.Net.NetworkCredential]::new('', $secureToken).Password
Remove-Variable secureToken

$runFolder = Join-Path $PWD ('jmeter-results\orders-10users-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runFolder -Force | Out-Null

& 'C:\tools\apache-jmeter-5.6.3\bin\jmeter.bat' -n -t '.\List Orders.jmx' '-Jusers=10' '-Jrampup=5' '-Jduration=60' '-JthinkTime=1000' '-Jjmeter.reportgenerator.overall_granularity=5000' -l "$runFolder\requests.jtl" -j "$runFolder\jmeter.log" -e -o "$runFolder\html"
if ($LASTEXITCODE -ne 0) { throw 'JMeter execution or report generation failed.' }

powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath "$runFolder\html\statistics.json"
$gateExitCode = $LASTEXITCODE
Write-Host "Performance checker exit code: $gateExitCode"
Invoke-Item "$runFolder\html\index.html"
```

The execution-policy argument applies only to the child PowerShell process. A new report folder is required for each run. Do not reuse a populated HTML output folder. Tokens are inherited only by processes launched from the configured shell; an already-running JMeter GUI will not acquire a newly set environment variable.

## Performance check

`Check-Performance.ps1` defaults to p95 <= 2000 ms and error percentage < 1%. These are provisional learning targets, not an agreed service SLA or assessment requirement. It assumes JMeter's default dashboard percentile configuration (90, 95, 99), where pct2ResTime is p95. Exit codes: 0 = met, 1 = missed, 2 = invalid/missing input. It has been verified against both successful-operation reports above. A pipeline must propagate its exit code to fail the job; printing FAIL alone is insufficient.

## Remaining assessment work

- Record the student's name/ID and confirm contribution ownership with the team.
- Agree the performance targets and justify expected workload against project usage.
- Integrate the JMeter command and checker into the group's unified script or CI job; supply secrets through that environment and preserve reports on failures.
- Confirm cleanup of the created orders and reconcile stock/reservation state with supported business operations.
- Prepare the required group slides (maximum five) or one-page architecture PDF, and a reliable two-minute individual demo.
- Include the project source, tests, configurations and setup/ownership README in the team's submission.

Current status: the planned Order and Notification tests are complete, with reports, saved evidence and a performance gate. Group-pipeline integration, final ownership details and cleanup verification remain separate tasks. No further load runs are planned.

## Final capacity findings

Capacity runs lasted 180 seconds with a 10 second ramp up, a 1000 ms pause per client and the first 30 seconds excluded from the measurement report. Raw results retain startup failures. Run-CapacityStep.ps1 saves the exact plan used, workload settings, raw and filtered results, logs and HTML report. It returns the gate exit status. For direct Azure, add -DirectAzure; otherwise it uses the gateway plan.

Direct Azure passed at 5, 10, 20 and 30 clients. At 50 clients the first run failed with 1.371% errors and p95 1999.4 ms; the repeat passed with zero measured errors and p95 870.8 ms. The repeat still had four HTTP 503 responses during warmup. Its measured throughput was 36.79 requests per second. Gateway runs at 1 and 5 clients did not consistently meet targets and included socket errors/timeouts. These paths must be reported separately.

Azure Metrics displayed 71 server errors during the first 50-client failure window, matching JMeter's 39 HTTP 500 and 32 HTTP 503 responses. This corroborates the failure but does not establish root cause. CPU Time is not CPU percentage.

Conclusion: the direct Order List endpoint met the proposed targets at up to 50 concurrent clients in one measured run. Results at 50 clients varied, so reliable maximum capacity was not established. Testing was intentionally stopped after that repeat; no higher load runs are needed for this submission.

See Sellora_Sprint_3_Load_Testing_Report.docx for the updated report, Capacity-Results.csv for measured comparisons and DEMO-GUIDE.md for the two-minute presentation. The submission archive preserves both passing and failing evidence; do not cherry-pick the passing run.

## Additional completed Order and Notification scenarios

- Create and Read Order Load.jmx used five configured clients, a five-second ramp up and two iterations each. All ten POSTs returned 201 and all ten correlated GETs returned 200. POST p95 2422 ms missed the provisional target; GET p95 323 ms met it. Ten samples per operation do not establish write capacity. The ledger is jmeter-results/create-load-20261018-145949/Created-Orders.csv. The earlier UI order and JMeter single-order baseline are additional records, not part of this ten-order batch.
- Notification List Baseline.jmx performed one Company Admin read. Notification List Load.jmx then used five clients for 60 seconds with a 1000 ms pause. The whole run including startup had 188 requests, no failures, p95 1647.4 ms and throughput 3.18 requests per second. It passed the provisional targets. Evidence: jmeter-results/notification-load-20261018-154729.
- Inspect-OrderNotifications.ps1 made one read-only lookup per existing batch order. All ten had one OrderPlaced notification request with two recipients marked Sent. Stored event-to-completion time ranged from 5860.66 to 23970.59 ms, averaging 15934.67 ms. This later snapshot is not a live backlog-drain or maximum-throughput test. Evidence: jmeter-results/notification-correlation-20261018-152055-028, including CSV, timestamp snapshots and inbox screenshots.
- The inbox screenshot shows the ten matching references and one opened example email. It corroborates displayed inbox arrival, not independent delivery to both intended addresses or precise inbox latency.

Notification plans use SELLORA_ADMIN_ACCESS_TOKEN. Set it using the same hidden Read-Host method, but with a Company Admin token. Keep it separate from SELLORA_ACCESS_TOKEN, which is the Sales Rep token. The notification direct host is sellora-notification-staging-g6csbfcqc6gmhtgd.eastasia-01.azurewebsites.net.

Evaluate mixed plans per sampler. Check-Performance.ps1 now accepts -TransactionName; the default remains Total for single-endpoint reports. For example, this offline check is expected to return exit code 1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath '.\jmeter-results\create-load-20261018-145949\html\statistics.json' -TransactionName 'POST - Create Order'
$LASTEXITCODE
```

Substitute 'GET - Read Created Order' to check reads separately (expected exit code 0). For Notification use its report statistics with the default Total scope (expected exit code 0).

The order tracking CSV was saved before notification inspection and still contains its original unchecked labels. Use the later correlation CSV and snapshots for notification outcomes. Cleanup has not been performed or verified; do not treat a Sent notification as cleanup evidence. The archive contains the current plans; capacity runs additionally preserve their exact plan-used.jmx snapshots.
