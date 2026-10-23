"""Package existing load-testing evidence only. Makes no service requests."""
from pathlib import Path
import csv
import hashlib
import json
import re
import zipfile

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[1]
RESULTS = ROOT / 'jmeter-results'
rows = []
capacity = []
for folder in sorted(RESULTS.glob('capacity-*')):
    stat_path = folder / 'html' / 'statistics.json'
    if not stat_path.is_file():
        continue
    stat = json.loads(stat_path.read_text())['Total']
    measured = list(csv.DictReader((folder / 'steady-requests.jtl').open(encoding='utf-8-sig', newline='')))
    raw = list(csv.DictReader((folder / 'requests.jtl').open(encoding='utf-8-sig', newline='')))
    assert len(measured) == stat['sampleCount'], folder.name
    failures = sum(r['success'].lower() != 'true' for r in measured)
    assert failures == stat['errorCount'], folder.name
    clients = re.search(r'(\d+)users', folder.name).group(1)
    rows.append({
        'Run': folder.name,
        'Path': 'Direct Azure' if 'direct-azure' in folder.name else 'Gateway',
        'ConcurrentClients': clients,
        'MeasuredRequests': stat['sampleCount'],
        'P95ms': round(stat['pct2ResTime'], 2),
        'ErrorPercent': round(stat['errorPct'], 6),
        'RequestsPerSecond': round(stat['throughput'], 4),
        'MeasuredFailures': failures,
        'FullRunFailures': sum(r['success'].lower() != 'true' for r in raw),
        'ProvisionalGate': 'PASS' if stat['pct2ResTime'] <= 2000 and stat['errorPct'] < 1 else 'FAIL',
    })
    capacity.append(folder)

with (OUT / 'Capacity-Results.csv').open('w', encoding='utf-8-sig', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)

files = [ROOT / name for name in ['README.md', 'List Orders.jmx', 'List Orders Direct Azure.jmx',
    'Create and Read Order.jmx', 'Verify Existing Order.jmx', 'Create and Read Order Load.jmx',
    'Notification List Baseline.jmx', 'Notification List Load.jmx']]
for name in ['Sellora_Sprint_3_Load_Testing_Report.docx', 'README.md', 'DEMO-GUIDE.md',
             'Check-Performance.ps1', 'Run-CapacityStep.ps1', 'Capacity-Results.csv',
             'Inspect-OrderNotifications.ps1', 'TWO-SERVICE-TEST-PLAN.md']:
    files.append(OUT / name)
for path in ROOT.glob('orders-*aggregate.csv'):
    files.append(path)
files.extend(sorted((OUT / 'report-assets').glob('*.png')))

folders = capacity + [RESULTS / name for name in ['20261018-110949', '20261018-111639', 'orders-10users-20261018-114507']]
for pattern in ['create-baseline-*', 'verify-order-*', 'create-load-*',
                'notification-baseline-*', 'notification-correlation-*', 'notification-load-*']:
    folders.extend(sorted(RESULTS.glob(pattern)))
for folder in folders:
    for name in ['requests.jtl', 'steady-requests.jtl', 'jmeter.log', 'report.log', 'workload.json', 'plan-used.jmx']:
        path = folder / name
        if path.is_file():
            files.append(path)
    report = folder / 'html'
    files.extend(path for path in sorted(report.rglob('*')) if path.is_file())
    files.extend(path for path in sorted(folder.iterdir()) if path.is_file() and path.suffix.lower() in {'.csv','.json','.png','.jpeg','.txt'})
    # The regenerated five-second graph used in the report belongs to this run.
    if folder.name == '20261018-111639':
        for report in sorted(folder.glob('html-5sec-*')):
            if (report / 'index.html').is_file():
                files.extend(path for path in sorted(report.rglob('*')) if path.is_file())

files = sorted(set(files))
print(f'Packaging {len(files)} existing evidence files; no service requests will be sent.', flush=True)
jwt_pattern = re.compile(rb'eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]+')
for path in files:
    if path.suffix.lower() in {'.jmx', '.jtl', '.log', '.json', '.csv', '.md', '.ps1'}:
        if jwt_pattern.search(path.read_bytes()):
            raise RuntimeError('Possible literal access token found; packaging stopped without printing it.')

inventory = '\n'.join(f'{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(ROOT).as_posix()}' for path in files)
(OUT / 'SUBMISSION-MANIFEST.sha256').write_text(inventory + '\n', encoding='utf-8')
files.append(OUT / 'SUBMISSION-MANIFEST.sha256')
archive = OUT / 'Sellora_Load_Testing_Submission.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=7) as z:
    for path in files:
        z.write(path, path.relative_to(ROOT).as_posix())
    z.writestr('START-HERE.txt',
        'Sellora Sprint 3 load-testing contribution\n\n'
        'Open QA/Performance/Sellora_Sprint_3_Load_Testing_Report.docx first.\n'
        'Setup: QA/Performance/README.md\n'
        'Presentation: QA/Performance/DEMO-GUIDE.md\n'
        'Comparisons: QA/Performance/Capacity-Results.csv\n\n'
        'Extract this entire archive before opening HTML reports.\n'
        'This contains the load-testing contribution, not the complete group project source or group slides.\n'
        'Add your name/student ID to the ownership section or submission cover.\n'
        'No tokens are included. Configure a fresh environment token before any future live demo.\n'
        'Capacity exploration stopped after the repeated 50-client run. Maximum reliable capacity was not established.\n'
        'Additional completed scenarios: ten-order create/retrieve batch, Notification List load and order-event notification correlation.\n'
        'Order-creation p95 missed the provisional target; Notification List passed. Cleanup remains unverified.\n')
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
print(f'Capacity runs verified: {len(rows)}')
print(f'Archive entries: {len(files) + 1}')
print(f'Archive size: {archive.stat().st_size / 1024 / 1024:.1f} MB')
print(archive)
