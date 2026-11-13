"""Keep inference/workspace tests away from local data and model artifacts."""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import app
import hybrid_models
import workspace


@pytest.fixture(autouse=True)
def isolated_artifacts(tmp_path, monkeypatch):
    monkeypatch.setattr(app, 'ROOT', tmp_path)
    monkeypatch.setattr(app, 'bundle', None)
    monkeypatch.setattr(workspace, 'DATASETS', tmp_path/'datasets')
    monkeypatch.setattr(workspace, 'ULB', tmp_path/'ulb')
    monkeypatch.setattr(hybrid_models, 'SELECTION', tmp_path/'hybrid-selection.joblib')
    monkeypatch.setattr(hybrid_models, '_cache', None)
    monkeypatch.setattr(hybrid_models, '_stamp', None)
    monkeypatch.setenv('APP_ENV', 'development')
    monkeypatch.delenv('AI_SERVICE_KEY', raising=False)
