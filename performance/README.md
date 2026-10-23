# Order and Notification performance tests

This folder contains the Sprint 3 load-testing contribution moved into the Order repository. The original internal layout is preserved so the existing scripts continue to resolve plans and evidence correctly.

## Start here

- [Report](QA/Performance/Sellora_Sprint_3_Load_Testing_Report.docx)
- [Setup and recorded findings](QA/Performance/README.md)
- [Two minute demo and viva guide](QA/Performance/DEMO-GUIDE.md)
- [Two service endpoint plan](QA/Performance/TWO-SERVICE-TEST-PLAN.md)
- [Submission archive with full HTML reports](QA/Performance/Sellora_Load_Testing_Submission.zip)

From the repository root, open the test workspace:

```powershell
Set-Location .\performance
```

All existing examples referring to `List Orders.jmx`, `QA\Performance` or `jmeter-results` must be run from this directory, not the repository root. Scripts also locate their input files relative to their own location.

## Files

| Location | Purpose |
| --- | --- |
| Root JMX files | Order List, direct Azure comparison, create/retrieve and Notification read plans |
| QA/Performance | Scripts, report, setup notes, demo guide and evidence images |
| jmeter-results | Raw request results, per-report statistics, screenshots, order ledger and notification correlation |

`Verify Existing Order.jmx` refers to one historical staging order. It is evidence/debugging support; that order may no longer exist or be visible. The helper scripts prepare plans and reports; they do not replace the documented runtime prerequisites.

Full generated dashboards remain on disk and in the submission ZIP. Git excludes their JavaScript, CSS and HTML files to avoid thousands of generated assets. Their `statistics.json` files, raw JTL records, logs, CSVs and screenshots remain eligible for upload. Extract the complete ZIP to view archived dashboards, or regenerate a dashboard from the relevant JTL using JMeter `-g` and a new output folder. Capacity dashboards use `steady-requests.jtl`; other dashboards use `requests.jtl`.

## Check saved results without making service requests

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath '.\jmeter-results\notification-load-20261018-154729\html\statistics.json'
$LASTEXITCODE
```

Expected for this saved Notification report: PASS, exit code 0.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\QA\Performance\Check-Performance.ps1' -StatisticsPath '.\jmeter-results\create-load-20261018-145949\html\statistics.json' -TransactionName 'POST - Create Order'
$LASTEXITCODE
```

Expected for the saved POST scope: FAIL, exit code 1. A failed performance target is retained as a finding rather than removed from the evidence.

## Before any future live execution

Install Java and JMeter, confirm the deployed test environment and data are still valid, and set a fresh Sales Rep token in `SELLORA_ACCESS_TOKEN` or Company Admin token in `SELLORA_ADMIN_ACCESS_TOKEN`. Never commit tokens. Default paths assume JMeter at `C:\tools\apache-jmeter-5.6.3\bin`; the capacity runner accepts `-JMeterPath` for another installation. Do not replay the write test without budgeting stock and identifying existing orders: it creates real staging records and can generate notification emails.

Tests were recorded on 17 and 18 October 2026. Reliable maximum capacity, database cleanup and integration into the group's full pipeline were not established by these files. The archive is the load-testing contribution, not the complete group project submission.
