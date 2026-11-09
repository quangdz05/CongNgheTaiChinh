"""Review probes: separate API process, temporary database, deterministic fake AI."""
import concurrent.futures, json, os, socket, sqlite3, subprocess, tempfile, threading, time, uuid
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import httpx

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / 'Back-end/src/Services/FraudDetectionService/FraudDetectionService.Api'
entered = threading.Event()
delay = 0
class FakeAi(BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_POST(self):
        if self.headers.get('Transfer-Encoding','').lower() == 'chunked':
            data=b''
            while True:
                size=int(self.rfile.readline().split(b';')[0],16)
                if not size:
                    self.rfile.readline(); break
                data+=self.rfile.read(size); self.rfile.read(2)
        else:
            data=self.rfile.read(int(self.headers['Content-Length']))
        req = json.loads(data)
        entered.set()
        time.sleep(delay)
        body = json.dumps(dict(transactionId=req['transactionId'], score=.1, modelVersion='review-fake', featureSchemaVersion='1')).encode()
        self.send_response(200); self.send_header('Content-Type','application/json'); self.end_headers(); self.wfile.write(body)

def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0)); return s.getsockname()[1]

def main():
    global delay
    (ROOT/'test-results/review').mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer(('127.0.0.1', 0), FakeAi)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    api_port = port()
    results = {}
    with tempfile.TemporaryDirectory(prefix='fraudshield-review-') as temp, (ROOT/'test-results/review/api.log').open('w') as log:
        dbpath = str(Path(temp)/'review.db')
        env = {**os.environ, 'ASPNETCORE_ENVIRONMENT':'Development', 'ConnectionStrings__FraudShield':f'Data Source={dbpath}', 'Ai__BaseUrl':f'http://127.0.0.1:{server.server_port}/', 'Ai__ApiKey':'review-test-key'}
        process = subprocess.Popen(['dotnet', str(API/'bin/Debug/net10.0/FraudDetectionService.Api.dll'), '--urls', f'http://127.0.0.1:{api_port}'], cwd=API, env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            with httpx.Client(base_url=f'http://127.0.0.1:{api_port}', timeout=15) as c:
                for _ in range(100):
                    if process.poll() is not None: raise RuntimeError('API exited; inspect api.log')
                    try:
                        if c.get('/health').status_code == 200: break
                    except httpx.HTTPError: pass
                    time.sleep(.1)
                login = c.post('/api/auth/login', json={'username':'admin','password':'Admin@123456'}); login.raise_for_status()
                c.headers['Authorization'] = 'Bearer '+login.json()['token']
                def req(**kwargs):
                    return dict(id=str(uuid.uuid4()), userId=str(uuid.uuid4()), amount=100000, currency='VND', merchant='Review probe', category='grocery', country='VN', deviceId=str(uuid.uuid4()), ipAddress='2001:db8::'+':'.join([uuid.uuid4().hex[:4],uuid.uuid4().hex[:4]]), mode='RulesOnly', timestamp='2026-01-02T12:00:00Z') | kwargs
                def analyze(payload):
                    r=c.post('/api/transactions', json=payload); r.raise_for_status(); return r.json()
                old = analyze(req(amount=150000000))
                p=c.get('/api/rules').json(); p['highAmount']=200000000
                c.put('/api/rules', json=p).raise_for_status()
                comparison=c.post('/api/compare/'+old['id']).json()
                current=analyze(req(amount=150000000))
                results['mixed_policy_comparison']={'comparisonRuleScore':comparison['ruleScore'], 'comparisonRuleDecision':comparison['ruleDecision'], 'currentPolicyAnalysisRuleScore':current['ruleScore'], 'currentPolicyAnalysisDecision':current['decision'], 'comparisonPolicyVersion':comparison['policyVersion']}
                # Ensure RulesOnly arrives while a different user's AI call holds the gate.
                entered.clear(); delay=1.2
                with concurrent.futures.ThreadPoolExecutor(2) as pool:
                    hybrid=pool.submit(analyze,req(mode='Hybrid'))
                    assert entered.wait(5)
                    started=time.perf_counter(); normal=analyze(req()); elapsed=time.perf_counter()-started
                    hybrid.result()
                results['global_gate']={'rulesOnlyWallMs':round(elapsed*1000,1),'rulesOnlyRecordedLatencyMs':normal['latencyMs'],'fakeAiDelayMs':1200}
                delay=0
                # The UI retains a generated ID when retrying the same submission.
                noid=req(); noid.pop('timestamp')
                first=analyze(noid); second=analyze(noid)
                results['stable_id_replay']={'differentIds':first['id']!=second['id'],'firstCount24h':first['features']['transactionCount24h'],'retryCount24h':second['features']['transactionCount24h']}
                # Add only to this probe's temporary database, never the app database.
                with sqlite3.connect(dbpath) as db:
                    cols=[r[1] for r in db.execute('PRAGMA table_info(Transactions)')]
                    evalcols=[r[1] for r in db.execute('PRAGMA table_info(Evaluations)')]
                    tx=dict(zip(cols,db.execute('SELECT * FROM Transactions WHERE Id=?',(normal['id'].upper(),)).fetchone()))
                    ev=dict(zip(evalcols,db.execute('SELECT * FROM Evaluations WHERE TransactionId=?',(normal['id'].upper(),)).fetchone()))
                    for i in range(10000):
                        new=str(uuid.uuid4()).upper(); tx['Id']=new; tx['Timestamp']+=1; ev['TransactionId']=new
                        db.execute('INSERT INTO Transactions VALUES ('+','.join('?' for _ in cols)+')',tuple(tx[k] for k in cols))
                        db.execute('INSERT INTO Evaluations VALUES ('+','.join('?' for _ in evalcols)+')',tuple(ev[k] for k in evalcols))
                db.close()
                s=c.get('/api/dashboard');s.raise_for_status();s=s.json()
                results['dashboard_limit']={k:s[k] for k in ['total','sampled','allow','review','block']}
                # Each forwarded client has its own login partition behind the trusted local proxy.
                statuses=[]
                for i in range(11):
                    r=c.post('/api/auth/login',json={'username':'nobody','password':'wrong'},headers={'X-Forwarded-For':f'198.51.100.{i+1}'})
                    statuses.append(r.status_code)
                results['forwarded_clients_login_statuses']=statuses
        finally:
            process.terminate()
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill();process.wait()
            server.shutdown(); server.server_close()
    (ROOT/'test-results/review/regression-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
    print(json.dumps(results,indent=2))
    assert results['mixed_policy_comparison']['comparisonRuleScore']==0
    assert results['mixed_policy_comparison']['comparisonRuleDecision']=='ALLOW'
    assert results['global_gate']['rulesOnlyWallMs']<1000
    assert not results['stable_id_replay']['differentIds']
    assert results['stable_id_replay']['firstCount24h']==results['stable_id_replay']['retryCount24h']
    dashboard=results['dashboard_limit']
    assert dashboard['total']==dashboard['allow']+dashboard['review']+dashboard['block']
    assert results['forwarded_clients_login_statuses']==[401]*11
    print('All HTTP review regressions passed.')

if __name__=='__main__': main()
