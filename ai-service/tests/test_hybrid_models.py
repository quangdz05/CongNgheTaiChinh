import json

import joblib
import numpy as np
import pytest
from fastapi.testclient import TestClient

import app
import hybrid_models
import workspace
from features import NAMES, SCHEMA


class FixedModel:
    def __init__(self, score):
        self.score = score

    def predict_proba(self, rows):
        return np.array([[1-self.score, self.score] for _ in rows])


def bundle(version, score):
    return dict(model=FixedModel(score), metadata=dict(modelVersion=version,
                featureSchemaVersion=SCHEMA, featureNames=NAMES))


def headers(role='Admin', owner='admin'):
    return {'X-Service-Key': 'local-ai-service-key', 'X-Actor': owner, 'X-Actor-Role': role}


@pytest.fixture
def client(monkeypatch):
    with TestClient(app.app) as client:
        monkeypatch.setattr(app, 'bundle', bundle('default-v1', .1))
        yield client


def ready_dataset(client):
    dataset_id = client.post('/v1/workspace/datasets', headers=headers(), json={'name': 'Trained VND'}).json()['id']
    path = workspace.folder(dataset_id)
    data = json.loads((path/'dataset.json').read_text(encoding='utf-8'))
    model = bundle('personal-v2', .9)
    data.update(status='ready', metrics=model['metadata'])
    workspace.save(path/'dataset.json', data)
    (path/'model').mkdir()
    joblib.dump(model, path/'model'/'model.joblib')
    return dataset_id


def predict(client):
    features = {k: 0 for k in NAMES}
    features['amount'] = 100000
    return client.post('/v1/predict', headers=headers(), json=dict(
        transactionId='hybrid-test', featureSchemaVersion=SCHEMA, features=features))


def test_activation_changes_actual_inference_metrics_and_survives_reload(client, monkeypatch):
    dataset_id = ready_dataset(client)
    assert predict(client).json()['score'] == .1
    result = client.post('/v1/workspace/hybrid-model', headers=headers(), json={'datasetId': dataset_id})
    assert result.status_code == 200
    version = result.json()['active']['modelVersion']
    assert version.startswith('personal-v2-ds-' + dataset_id)
    assert predict(client).json()['score'] == .9
    assert client.get('/health').json()['modelVersion'] == version
    assert client.get('/v1/metrics', headers=headers()).json()['modelVersion'] == version
    monkeypatch.setattr(hybrid_models, '_stamp', None)
    monkeypatch.setattr(hybrid_models, '_cache', None)
    monkeypatch.setattr(app, 'bundle', None)
    assert predict(client).json()['modelVersion'] == version
    # Retraining changes the source artifact, never the applied snapshot.
    joblib.dump(bundle('retrained-v3', .3), workspace.folder(dataset_id)/'model'/'model.joblib')
    assert predict(client).json()['score'] == .9
    # Dataset deletion likewise cannot invalidate a promoted snapshot.
    assert client.delete(f'/v1/workspace/datasets/{dataset_id}', headers=headers()).status_code == 200
    assert predict(client).json()['score'] == .9
    monkeypatch.setattr(app, 'bundle', bundle('default-v1', .1))
    assert client.post('/v1/workspace/hybrid-model', headers=headers(), json={'datasetId': None}).status_code == 200
    assert predict(client).json()['modelVersion'] == 'default-v1'


def test_only_admin_can_activate_own_ready_native_model(client):
    dataset_id = ready_dataset(client)
    endpoint = '/v1/workspace/hybrid-model'
    assert client.post(endpoint, headers=headers('Analyst'), json={'datasetId': dataset_id}).status_code == 403
    assert client.post(endpoint, headers=headers(owner='other-admin'), json={'datasetId': dataset_id}).status_code == 404
    assert client.post(endpoint, headers=headers(), json={'datasetId': '../ulb'}).status_code == 404
    assert client.get(endpoint, headers=headers('Analyst')).json()['canManage'] is False
    path = workspace.folder(dataset_id)/'dataset.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    data['status'] = 'draft'
    workspace.save(path, data)
    assert client.get(endpoint, headers=headers()).json()['options'] == []
    assert client.post(endpoint, headers=headers(), json={'datasetId': dataset_id}).status_code == 409
    data['status'] = 'ready'
    workspace.save(path, data)
    wrong = bundle('personal-v2', .9)
    wrong['metadata']['featureSchemaVersion'] = 'ulb-pca-v1'
    joblib.dump(wrong, path.parent/'model'/'model.joblib')
    assert client.post(endpoint, headers=headers(), json={'datasetId': dataset_id}).status_code == 409
    assert predict(client).json()['modelVersion'] == 'default-v1'


def test_corrupt_selection_fails_closed_and_admin_can_restore_default(client):
    hybrid_models.SELECTION.write_bytes(b'invalid model')
    assert predict(client).status_code == 503
    assert client.get('/health').status_code == 503
    assert client.get('/v1/workspace/hybrid-model', headers=headers()).json()['canManage'] is True
    assert client.post('/v1/workspace/hybrid-model', headers=headers(), json={'datasetId': None}).status_code == 200
    assert predict(client).json()['modelVersion'] == 'default-v1'
