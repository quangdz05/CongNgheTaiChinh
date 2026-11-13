import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import app
import workspace
from train_ulb import chronological_split


@pytest.fixture
def client(tmp_path, monkeypatch):
    monkeypatch.setattr(workspace, 'DATASETS', tmp_path/'datasets')
    monkeypatch.setenv('APP_ENV', 'development')
    monkeypatch.delenv('AI_SERVICE_KEY', raising=False)
    with TestClient(app.app) as client:
        yield client


def headers(actor='analyst'):
    return {'X-Service-Key': 'local-ai-service-key', 'X-Actor': actor}


def create(client):
    response = client.post('/v1/workspace/datasets', headers=headers(), json={'name': 'My data'})
    assert response.status_code == 200
    return response.json()['id']


def test_dataset_ownership_and_internal_auth(client):
    assert client.get('/v1/workspace/datasets').status_code == 401
    id = create(client)
    assert client.get('/v1/workspace/datasets', headers=headers('admin')).json() == []
    assert client.get(f'/v1/workspace/datasets/{id}', headers=headers('admin')).status_code == 404
    assert client.post(f'/v1/workspace/datasets/{id}/generate', headers=headers('admin'), json={'samples': 1000, 'seed': 42}).status_code == 404


def test_generate_provenance_and_duplicate_generation_rejected(client):
    id = create(client)
    path = f'/v1/workspace/datasets/{id}'
    assert client.post(path+'/generate', headers=headers(), json={'samples': 1000, 'seed': 42}).status_code == 200
    d = client.get(path, headers=headers()).json()
    assert d['rowCount'] == d['labelledCount'] == 1000
    assert d['containsSynthetic'] is True
    assert all(r['origin'] == 'synthetic' for r in d['preview'])
    assert client.post(path+'/generate', headers=headers(), json={'samples': 1000}).status_code == 409
    assert client.post(path+'/generate', headers=headers(), json={'samples': 10001}).status_code == 422


def test_manual_unknown_not_used_and_export_sanitizes_formula(client):
    id = create(client)
    path = f'/v1/workspace/datasets/{id}'
    row = dict(userId='=HYPERLINK("x")', amount=100, currency='VND', merchant='Shop', category='other',
        country='VN', deviceId='dev', ipAddress='192.0.2.1', timestamp='2026-01-01T00:00:00Z', label=None)
    assert client.post(path+'/rows', json=row, headers=headers()).status_code == 200
    d = client.get(path, headers=headers()).json()
    assert d['rowCount'] == 1 and d['labelledCount'] == 0 and not d['containsSynthetic']
    assert client.post(path+'/train', json={}, headers=headers()).status_code == 422
    assert "'=HYPERLINK" in client.get(path+'/csv', headers=headers()).text
    row['timestamp'] = '2026-01-01'
    assert client.post(path+'/rows', json=row, headers=headers()).status_code == 422


def test_training_locks_edits_and_recovers_interrupted_job(client):
    id = create(client)
    p = workspace.folder(id)/'dataset.json'
    data = json.loads(p.read_text()); data['status'] = 'training'; workspace.save(p, data)
    assert client.post(f'/v1/workspace/datasets/{id}/generate', json={'samples': 1000}, headers=headers()).status_code == 409
    workspace.recover_jobs()
    assert client.get(f'/v1/workspace/datasets/{id}', headers=headers()).json()['status'] == 'failed'


def test_ulb_rejects_native_schema_and_label_leakage(client):
    assert client.post('/v1/workspace/ulb/predict', headers=headers(), json={'features': {'amount': 1}}).status_code == 422
    features = {k: 0. for k in workspace.ULB_NAMES}
    features['Class'] = 1
    assert client.post('/v1/workspace/ulb/predict', headers=headers(), json={'features': features}).status_code == 422


def test_ulb_split_keeps_equal_timestamps_together():
    data = pd.DataFrame({'Time': np.repeat(np.arange(10), 3), 'Class': [0, 1, 0]*10})
    sorted_data, a, b = chronological_split(data)
    assert sorted_data.Time.iloc[a-1] < sorted_data.Time.iloc[a]
    assert sorted_data.Time.iloc[b-1] < sorted_data.Time.iloc[b]


def test_personal_dataset_training_and_prediction_are_isolated(client):
    import time
    id = create(client)
    path = f'/v1/workspace/datasets/{id}'
    assert client.post(path+'/generate', headers=headers(), json={'samples': 1000, 'seed': 42}).status_code == 200
    assert client.post(path+'/train', headers=headers(), json={}).status_code == 200
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        data = client.get(path, headers=headers()).json()
        if data['status'] != 'training':
            break
        time.sleep(.25)
    assert data['status'] == 'ready', data.get('error')
    assert 'synthetic' in data['metrics']['datasetSource']
    row = dict(userId='USER-0001', amount=100000, currency='VND', merchant='Shop', category='grocery',
               country='VN', deviceId='test-device', ipAddress='192.0.2.2', timestamp='2026-01-10T00:00:00Z')
    result = client.post(path+'/predict', headers=headers(), json=row)
    assert result.status_code == 200
    assert 0 <= result.json()['score'] <= 1
    assert client.get(path, headers=headers()).json()['rowCount'] == 1000
    assert client.post(path+'/predict', headers=headers('admin'), json=row).status_code == 404
