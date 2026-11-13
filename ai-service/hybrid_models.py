"""Explicit, administrator-controlled snapshots for native Hybrid inference."""
import os
import threading
from pathlib import Path

import joblib
from fastapi import HTTPException

from features import NAMES, SCHEMA

SELECTION = Path(__file__).resolve().parent/'artifacts'/'hybrid-selection.joblib'
lock = threading.RLock()
_cache = None
_stamp = None


def validate(bundle):
    metadata = bundle['metadata']
    if (metadata.get('featureSchemaVersion') != SCHEMA or metadata.get('featureNames') != NAMES
            or not metadata.get('modelVersion') or not hasattr(bundle.get('model'), 'predict_proba')):
        raise HTTPException(409, 'Mô hình không tương thích với đặc trưng giao dịch VND.')
    return bundle


def selection():
    global _cache, _stamp
    with lock:
        if not SELECTION.exists():
            return dict(bundle=None, active=dict(datasetId=None, name='Mô hình VND mặc định', modelVersion=None))
        stamp = (str(SELECTION), SELECTION.stat().st_mtime_ns)
        try:
            if _stamp != stamp:
                candidate = joblib.load(SELECTION)
                if candidate['bundle'] is not None:
                    validate(candidate['bundle'])
                _cache, _stamp = candidate, stamp
            return _cache
        except Exception as exc:
            raise HTTPException(503, 'Không thể nạp mô hình Hybrid đã chọn. Admin cần chọn lại mô hình.') from exc


def apply(bundle, active):
    global _cache, _stamp
    if bundle is not None:
        validate(bundle)
    with lock:
        SELECTION.parent.mkdir(parents=True, exist_ok=True)
        temporary = SELECTION.with_suffix('.tmp')
        joblib.dump(dict(bundle=bundle, active=active), temporary)
        os.replace(temporary, SELECTION)
        _cache = _stamp = None
        return selection()['active']


def current(default):
    chosen = selection()['bundle']
    return chosen if chosen is not None else default
