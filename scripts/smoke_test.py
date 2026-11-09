"""Run against local development API and AI service. Uses unique synthetic records only."""
import json,os,sys,uuid
from pathlib import Path
from datetime import datetime,timezone,timedelta
import httpx
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'ai-service'))
from features import vector

def main():
    base=os.getenv('TEST_API_URL','http://127.0.0.1:5080')
    client=httpx.Client(base_url=base,timeout=20)
    assert client.get('/health').status_code==200
    assert client.get('/api/dashboard').status_code==401
    assert client.post('/api/auth/login',json={'username':'admin','password':'wrong'}).status_code==401
    login=client.post('/api/auth/login',json={'username':'admin','password':os.getenv('TEST_ADMIN_PASSWORD','Admin@123456')});login.raise_for_status()
    client.headers['Authorization']='Bearer '+login.json()['token']
    run=uuid.uuid4().hex[:6]
    def payload(amount=100000,mode='RulesOnly'):
        return dict(id=str(uuid.uuid4()),userId='SMOKE-'+run,amount=amount,currency='VND',merchant='Test Store',category='grocery',country='VN',deviceId='smoke-'+run,ipAddress='192.0.2.98',mode=mode,timestamp=(datetime.now(timezone.utc)-timedelta(days=1)).replace(hour=12,minute=0,second=0,microsecond=0).isoformat())
    req=payload();response=client.post('/api/transactions',json=req);response.raise_for_status();normal=response.json()
    assert normal['decision']=='ALLOW' and normal['mlScore'] is None and normal['aiStatus']=='Disabled'
    assert client.post('/api/transactions',json=req).json()['id']==normal['id']
    assert client.post('/api/transactions',json={**req,'amount':200000}).status_code==409
    critical=client.post('/api/transactions',json=payload(300000000));critical.raise_for_status();assert critical.json()['decision']=='BLOCK'
    hybrid=client.post('/api/transactions',json=payload(mode='Hybrid'));hybrid.raise_for_status();assert hybrid.json()['mlScore'] is not None and hybrid.json()['aiStatus']=='Available'
    # Workflow closure preserves uncertainty instead of producing a legacy legitimate label.
    legacy=client.post('/api/transactions/'+critical.json()['id']+'/reviews',json={'isFraud':False,'notes':'Legacy review is disabled.'})
    assert legacy.status_code==409 and legacy.json()['code']=='LEGACY_REVIEW_DISABLED'
    alert=client.get('/api/alerts',params={'search':critical.json()['id']}).json()['items'][0]
    def command(name,**fields):
        nonlocal alert
        r=client.post('/api/alerts/'+alert['id']+'/'+name,json={'requestId':str(uuid.uuid4()),'expectedVersion':alert['version'],**fields});r.raise_for_status();alert=r.json()['alert']
    command('claim');command('start');command('close',closureReasonCode='InsufficientEvidence',resolution='Unknown',notes='Synthetic smoke test has insufficient evidence.')
    assert alert['status']=='Closed' and alert['resolution']=='Unknown'
    assert client.get('/api/transactions/'+critical.json()['id']).json()['reviews']==[]
    policy=client.get('/api/rules').json();updated=client.put('/api/rules',json=policy);updated.raise_for_status();assert updated.json()['version']==policy['version']+1
    assert client.put('/api/rules',json=policy).status_code==409
    analyst=client.post('/api/auth/login',json={'username':'analyst','password':'Analyst@123456'}).json()
    assert client.put('/api/rules',json=updated.json(),headers={'Authorization':'Bearer '+analyst['token']}).status_code==403
    invalid=payload();invalid['currency']='USD';assert client.post('/api/transactions',json=invalid).status_code==400
    assert client.get('/api/transactions?size=0').status_code==400
    assert client.get('/api/transactions/export').status_code==200
    comparison=client.post('/api/compare/'+normal['id']);comparison.raise_for_status();assert comparison.json()['aiStatus']=='Available'
    assert client.get('/api/ai/metrics').json()['datasetSource']=='synthetic'
    # Cross-language integration: same raw fixture -> identical C# and Python features.
    fixture=json.loads((ROOT/'contracts/feature-v1-fixture.json').read_text())
    def mapped(raw):return {**raw,'id':str(uuid.uuid4()),'userId':run+'-'+raw['userId'],'deviceId':run+'-'+raw['deviceId'],'currency':'VND','merchant':'Fixture','mode':'RulesOnly','ipAddress':'192.0.2.99'}
    for raw in fixture['history']:client.post('/api/transactions',json=mapped(raw)).raise_for_status()
    result=client.post('/api/transactions',json=mapped(fixture['transaction']));result.raise_for_status()
    expected=vector(fixture['transaction'],fixture['history'])
    for key,value in expected.items():assert abs(result.json()['features'][key]-value)<1e-8,(key,result.json()['features'][key],value)
    print(json.dumps({'status':'passed','checks':['authentication','role authorization','validation','rule-only no AI','hybrid AI integration','critical override','idempotency','optimistic policy concurrency','Alert closure Unknown and legacy write disabled','CSV export','same-feature comparison','C# Python parity']},indent=2))
if __name__=='__main__':main()
