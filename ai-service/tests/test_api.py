import sys
from pathlib import Path
import pytest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from fastapi.testclient import TestClient
import app as inference
from features import NAMES

def request():
    f={k:0 for k in NAMES};f.update(amount=100000,hourOfDay=12,merchantRisk=.1)
    return {'transactionId':'unit-test','featureSchemaVersion':'1','features':f}
def test_auth_rejects_unknown_key():
    with TestClient(inference.app) as client:assert client.post('/v1/predict',json=request(),headers={'X-Service-Key':'fs_not_registered'}).status_code==401
def test_schema_is_enforced():
    with TestClient(inference.app) as client:
        req=request();req['featureSchemaVersion']='unknown'
        assert client.post('/v1/predict',json=req,headers={'X-Service-Key':'local-ai-service-key'}).status_code==422
def test_nonfinite_and_extra_features_rejected():
    with TestClient(inference.app) as client:
        req=request();req['features']['unexpected']=1
        assert client.post('/v1/predict',json=req,headers={'X-Service-Key':'local-ai-service-key'}).status_code==422
def test_loaded_model_predicts_valid_score_without_training():
    if not (inference.ROOT/'artifacts/model.joblib').exists():pytest.skip('Run offline training first')
    with TestClient(inference.app) as client:
        r=client.post('/v1/predict',json=request(),headers={'X-Service-Key':'local-ai-service-key'})
        assert r.status_code==200
        assert 0<=r.json()['score']<=1
        assert r.json()['featureSchemaVersion']=='1'
