"""Start the real services against an isolated temporary database, test, then stop both."""
import os,sys,subprocess,tempfile,time
from pathlib import Path
import httpx
ROOT=Path(__file__).resolve().parents[1]
API=ROOT/'Back-end/src/Services/FraudDetectionService/FraudDetectionService.Api'
def ready(url,process):
    for _ in range(120):
        if process.poll() is not None:raise RuntimeError(f'Service exited ({process.returncode}); read the integration log')
        try:
            if httpx.get(url,timeout=1).status_code==200:return
        except httpx.HTTPError:pass
        time.sleep(.25)
    raise RuntimeError('Service did not become ready; model artifact may be missing')
def main():
    dll=API/'bin/Debug/net10.0/FraudDetectionService.Api.dll'
    if not dll.exists():raise RuntimeError('Run dotnet build FraudShield.Clean.slnx first')
    logs=ROOT/'test-results';logs.mkdir(exist_ok=True)
    processes=[]
    with tempfile.TemporaryDirectory(prefix='fraudshield-test-') as temp, (logs/'integration-api.log').open('w',encoding='utf-8') as api_log, (logs/'integration-ai.log').open('w',encoding='utf-8') as ai_log:
        try:
            api_env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development','ConnectionStrings__FraudShield':f'Data Source={Path(temp)/"test.db"}','Ai__BaseUrl':'http://127.0.0.1:18001/','Ai__ApiKey':'local-ai-service-key'}
            ai_env={**os.environ,'APP_ENV':'development','AI_SERVICE_KEY':'local-ai-service-key','OMP_NUM_THREADS':'2'}
            processes.append(subprocess.Popen(['dotnet',str(dll),'--urls','http://127.0.0.1:15080'],cwd=API,env=api_env,stdout=api_log,stderr=subprocess.STDOUT))
            processes.append(subprocess.Popen([sys.executable,'-m','uvicorn','app:app','--host','127.0.0.1','--port','18001'],cwd=ROOT/'ai-service',env=ai_env,stdout=ai_log,stderr=subprocess.STDOUT))
            ready('http://127.0.0.1:15080/health',processes[0]);ready('http://127.0.0.1:18001/health',processes[1])
            subprocess.run([sys.executable,str(ROOT/'scripts/smoke_test.py')],env={**os.environ,'TEST_API_URL':'http://127.0.0.1:15080','TEST_ADMIN_PASSWORD':'Admin@123456'},cwd=ROOT,check=True)
            # Exercise real HTTP failure behavior with the AI process down.
            processes[1].terminate();processes[1].wait(timeout=10)
            with httpx.Client(base_url='http://127.0.0.1:15080',timeout=15) as client:
                token=client.post('/api/auth/login',json={'username':'admin','password':'Admin@123456'}).json()['token']
                client.headers['Authorization']='Bearer '+token
                req=dict(userId='FALLBACK-CHECK',amount=100000,currency='VND',merchant='Test',category='grocery',country='VN',deviceId='fallback-device',ipAddress='198.51.100.10',mode='Hybrid')
                response=client.post('/api/transactions',json=req);response.raise_for_status();data=response.json()
                assert data['decision']=='REVIEW' and data['mlScore'] is None and data['aiStatus']=='UnavailableReviewFallback'
                req['mode']='RulesOnly';req['userId']='OFFLINE-RULES';response=client.post('/api/transactions',json=req);response.raise_for_status()
                assert response.json()['aiStatus']=='Disabled'
                print('Real HTTP AI outage fallback and offline RulesOnly: passed.')
        finally:
            for process in reversed(processes):
                if process.poll() is None:
                    process.terminate()
                    try:process.wait(timeout=10)
                    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
if __name__=='__main__':main()
