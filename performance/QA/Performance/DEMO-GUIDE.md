# Two minute load testing demonstration

Run command examples from the repository's `performance` directory. This is the testing workspace root after relocation on 23 October 2026.

## Before the slot

- Add your name and student ID to the group ownership section or submission cover.
- Keep the group project source and configurations alongside this load-testing contribution.
- Use a fresh Sales Rep access token in SELLORA_ACCESS_TOKEN in the shell launching JMeter. Never show the token in a slide or terminal output.
- Open the Word report, the first 50-client failure dashboard and the repeated 50-client passing dashboard in advance.
- Confirm the test host is reachable. A demonstration may legitimately miss a target; explain the result rather than changing thresholds.

## Suggested timing and explanation

**0 to 20 seconds**

My contribution is load and performance testing for Sprint 3 Order and Notification. I used JMeter with status, JSON structure and correlated order-ID assertions. Order used a Sales Rep token and Notification a Company Admin token. Clients shared one account per role.

**20 to 45 seconds**

Show the saved plan or runner configuration. Explain users, ramp up, the one second pause, token environment variable and proposed targets: p95 at most two seconds and errors below one percent. The capacity experiments lasted three minutes and excluded the first 30 seconds from the comparison.

**45 to 90 seconds**

Show the report results tables. Direct Order List passed at 5 through 30 clients; 50 clients failed once and passed once, so a reliable maximum is unknown. The ten-order batch created and retrieved every order, but POST p95 2422 ms missed the target. Notification List passed with 188 requests and p95 1647.4 ms. All ten orders had notification records with both recipients Sent; inbox screenshots corroborated arrival. Keep the Azure screenshot and detailed dashboards ready for questions rather than trying to explain every chart.

**90 to 120 seconds**

Run the checker against the saved POST sampler and show its nonzero exit code. Explain that a request can succeed while being too slow and that the script can fail the group automation job when its exit code is propagated. Conclude with the limits: no reliable whole-system maximum, shared identities and short workloads. If live HTTP execution is already running, show that summary and explain the saved evidence when the live result differs.

The assessment calls for live execution. The offline checker below demonstrates actual execution and interpretation; coordinate with the team whether an additional short live HTTP run should start during the group automation introduction. Saved results alone are not a replacement for the required live test demonstration.

## Live result check from the project root

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath '.\jmeter-results\create-load-20261018-145949\html\statistics.json' -TransactionName 'POST - Create Order'
$LASTEXITCODE
```

Expected result for this saved POST scope: FAIL, exit code 1. Use -TransactionName 'GET - Read Created Order' for the same report to show PASS, exit code 0. Notification List also passed its saved report. Do not blend the POST and GET into one Total acceptance result.

## Optional short live HTTP demonstration for the presentation

This is a demonstration command, not another capacity experiment. It is provided for rehearsal or the presentation and has not been run as part of finalizing the package. It uses a fresh environment token and leaves the three-minute evidence unchanged. Run it in the project root and allow time for report generation.

```powershell
$demoFolder = Join-Path $PWD ('jmeter-results\demo-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $demoFolder -Force | Out-Null
& 'C:\tools\apache-jmeter-5.6.3\bin\jmeter.bat' -n -t '.\List Orders Direct Azure.jmx' '-Jusers=5' '-Jrampup=5' '-Jduration=45' '-JthinkTime=1000' '-Jjmeter.reportgenerator.overall_granularity=5000' '-Jaggregate_rpt_pct1=90' '-Jaggregate_rpt_pct2=95' '-Jaggregate_rpt_pct3=99' -l "$demoFolder\requests.jtl" -j "$demoFolder\jmeter.log" -e -o "$demoFolder\html"
if ($LASTEXITCODE -ne 0) { throw 'JMeter execution or report generation failed.' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath "$demoFolder\html\statistics.json"
$LASTEXITCODE
```

This short demo includes startup and cannot replace the measured capacity reports.

## Viva answers to practise

- **How many users can Sellora handle?** A direct Order List run passed at 50 concurrent clients under this specific workload. Reliable maximum capacity and whole-system capacity were not established.
- **Why repeat a level?** The first 50-client run failed and the repeat passed. Repetition prevents treating a transient event as a fixed limit.
- **Why p95?** The average can hide slower responses. p95 describes a response-time boundary covering about 95 percent of the samples.
- **Why did throughput rise?** More clients issued requests concurrently. Because each waits for a response and then pauses, this is a closed workload; response delays limit the achieved request rate.
- **Does 50 clients mean 50 separate users?** No. These clients shared one Sales Rep token and its data scope.
- **Was CPU the cause?** Not established. The chart showed CPU Time in seconds, not CPU percentage. Error counts matched, but exception traces are needed for cause.
- **What remains for the group?** Invoke the runner in the unified pipeline, propagate its exit status, preserve evidence and provide the group slides and complete source package.
- **Did a notification GET prove delivery?** No. Read load checks the API. The separate correlation script checked stored OrderPlaced recipient outcomes and completion timestamps. Screenshots corroborated inbox arrival for displayed messages.
- **Why does creation have worse p95?** It performs validation, service calls, persistence and inventory operations. The measured difference is real, but attributing it to one component requires traces.
- **How much data did the write run create?** Ten scheduled orders in the concurrent batch, plus the earlier UI example and single JMeter baseline. No further write batches are planned; cleanup remains unverified.
