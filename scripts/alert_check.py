"""Alert API regression checks. Always starts its own API with a disposable SQLite DB."""
import concurrent.futures
import json
import os
import socket
import subprocess
import tempfile
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import httpx

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / 'Back-end/src/Services/FraudDetectionService/FraudDetectionService.Api'


def main():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    url = f'http://127.0.0.1:{port}'
    logs = ROOT / 'test-results/alerts'
    logs.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='fraud-alert-api-') as temp, (logs / 'api.log').open('w', encoding='utf-8') as log:
        env = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'ConnectionStrings__FraudShield': f'Data Source={Path(temp) / "alerts.db"}', 'Ai__BaseUrl': 'http://127.0.0.1:1/', 'Ai__TimeoutSeconds': '1'}
        process = subprocess.Popen(['dotnet', str(API / 'bin/Debug/net10.0/FraudDetectionService.Api.dll'), '--urls', url], cwd=API, env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            with httpx.Client(base_url=url, timeout=20) as client:
                for _ in range(160):
                    if process.poll() is not None:
                        raise RuntimeError('API exited; inspect test-results/alerts/api.log')
                    try:
                        if client.get('/health').status_code == 200:
                            break
                    except httpx.HTTPError:
                        pass
                    time.sleep(.25)
                else:
                    raise RuntimeError('API startup timed out')
                assert client.get('/api/alerts').status_code == 401
                tokens = {}
                for name, password in [('admin', 'Admin@123456'), ('analyst', 'Analyst@123456')]:
                    response = client.post('/api/auth/login', json={'username': name, 'password': password})
                    response.raise_for_status()
                    tokens[name] = {'Authorization': 'Bearer ' + response.json()['token']}
                admin, analyst = tokens['admin'], tokens['analyst']

                def get(path, actor=admin, **kwargs):
                    response = client.get('/api' + path, headers=actor, **kwargs)
                    response.raise_for_status()
                    return response.json()

                def create(amount=150000000, mode='RulesOnly'):
                    payload = dict(id=str(uuid.uuid4()), userId='ALERT-' + uuid.uuid4().hex[:8], amount=amount, currency='VND', merchant='Alert regression', category='grocery', country='VN', deviceId=str(uuid.uuid4()), ipAddress='192.0.2.40', mode=mode, timestamp='2026-01-02T12:00:00Z')
                    response = client.post('/api/transactions', json=payload, headers=admin)
                    response.raise_for_status()
                    assert client.post('/api/transactions', json=payload, headers=admin).json()['id'] == payload['id']
                    items = get('/alerts', params={'search': payload['id']})['items']
                    assert len(items) == (response.json()['decision'] != 'ALLOW')
                    return items[0] if items else None

                def send(a, command, as_actor=admin, **fields):
                    body = dict(requestId=str(uuid.uuid4()), expectedVersion=a['version'], **fields)
                    return client.post(f'/api/alerts/{a["id"]}/{command}', json=body, headers=as_actor), body

                def apply(a, command, actor=admin, **fields):
                    response, _ = send(a, command, actor, **fields)
                    response.raise_for_status()
                    return response.json()['alert']

                assert create(100000) is None
                a = create()
                assert a['status'] == 'New' and a['priority'] == 'P2'
                with concurrent.futures.ThreadPoolExecutor(2) as pool:
                    results = list(pool.map(lambda actor: (actor, *send(a, 'claim', actor)), [admin, analyst]))
                assert sorted(r[1].status_code for r in results) == [200, 409]
                winner, response, body = next(r for r in results if r[1].status_code == 200)
                loser = analyst if winner == admin else admin
                a = response.json()['alert']
                replay = client.post(f'/api/alerts/{a["id"]}/claim', json=body, headers=winner)
                assert replay.status_code == 200 and replay.json()['replayed']
                assert client.post(f'/api/alerts/{a["id"]}/claim', json=body, headers=loser).status_code == 409
                assert send(a, 'start', loser)[0].status_code == 403
                assert send(a, 'start', winner, actor='spoofed')[0].status_code == 400
                assert send(a, 'start', winner, ownerUserId=999)[0].status_code == 400
                a = apply(a, 'start', winner)
                stale = a.copy()
                a = apply(a, 'wait', winner, reason='Await additional records', waitingType='ExternalInformation', waitingUntil=(datetime.now(timezone.utc) + timedelta(hours=1)).isoformat())
                assert send(stale, 'close', winner, closureReasonCode='InsufficientEvidence', resolution='Unknown', notes='Evidence not available')[0].status_code == 409
                a = apply(a, 'close', winner, closureReasonCode='InsufficientEvidence', resolution='Unknown', notes='Evidence not available')
                assert a['status'] == 'Closed' and a['resolution'] == 'Unknown' and a['waitingUntil'] is None
                assert get('/transactions/' + a['transactionId'])['reviews'] == []
                stats = get('/alerts/stats')
                assert stats['openAlerts'] == 0 and stats['closedUnknown'] == 1
                legacy = client.post('/api/transactions/' + a['transactionId'] + '/reviews', json={'isFraud': False, 'notes': 'Cannot force a label'}, headers=admin)
                assert legacy.status_code == 409 and legacy.json()['code'] == 'LEGACY_REVIEW_DISABLED'
                assert get('/alerts/users', analyst) == [dict(id=2, username='analyst', role='Analyst')]
                assert client.get('/api/audit', headers=analyst).status_code == 403
                assert client.get('/api/alerts?owner=1', headers=analyst).status_code == 403
                assert client.get('/api/alerts?status=invalid', headers=admin).status_code == 400
                assert client.get('/api/alerts?size=0', headers=admin).status_code == 400
                timeline = get(f'/alerts/{a["id"]}/timeline')
                assert [e['type'] for e in timeline['items']] == ['Closed', 'WaitingEntered', 'Started', 'Claimed', 'Created']
                assert timeline['total'] == 5
                a = apply(a, 'reopen', admin, ownerUserId=1, reason='Additional records arrived')
                assert a['status'] == 'Assigned' and a['closureNotes'] is None and a['resolution'] == 'Unknown'
                a = apply(a, 'start')
                assert send(a, 'close', resolution='Legitimate', closureReasonCode='LegitimateConfirmed', notes='Evidence reviewed')[0].status_code == 400
                assert send(a, 'close', resolution='Legitimate', closureReasonCode='LegitimateConfirmed', notes='Evidence reviewed', evidence=[None])[0].status_code == 400
                a = apply(a, 'close', resolution='Legitimate', closureReasonCode='LegitimateConfirmed', notes='Evidence reviewed', evidence=[dict(sourceType='InternalRecord', reference='DOC-123', description='Referenced internal investigation', capturedAt=datetime.now(timezone.utc).isoformat())])
                assert json.loads(a['evidenceJson'])[0]['creatorUserId'] == 1
                assert get('/transactions/' + a['transactionId'])['decision'] == 'REVIEW'
                critical = create(300000000)
                assert critical['priority'] == 'P1'
                critical = apply(critical, 'escalate', reason='Manual supervisor attention')
                assert critical['status'] == 'Escalated' and critical['ownerUserId'] is None
                critical = apply(critical, 'assign', ownerUserId=1, reason='Admin takes escalation')
                assert critical['handlingEpisode'] == 2 and critical['status'] == 'Assigned'
                fallback = create(100000, 'Hybrid')
                assert fallback['priority'] == 'P3'
                print('Alert HTTP checks passed: auth, atomic claim race, retry, owner/state/version, Waiting, Unknown, evidence, reopen, escalation, filters, legacy and stats.')
                if os.getenv('ALERT_UI_CHECK') == '1':
                    subprocess.run(['node', str(ROOT / 'scripts/alert_ui_check.cjs')], cwd=ROOT, env={**os.environ, 'ALERT_TEST_API_URL': url}, check=True)
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)


if __name__ == '__main__':
    main()
