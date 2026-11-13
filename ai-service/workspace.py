"""Owned dataset drafts and isolated training jobs; never promotes a user model globally."""
import csv
import json
import logging
import os
import re
import subprocess
import sys
import threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

import joblib
import numpy as np
from fastapi import APIRouter, Header, HTTPException
from pydantic import BaseModel, ConfigDict, Field, field_validator

from train import load_csv, synthetic
from train_ulb import ULB_NAMES

ROOT = Path(__file__).resolve().parent
DATASETS = ROOT/'artifacts'/'datasets'
ULB = ROOT/'artifacts'/'ulb'
router = APIRouter(prefix='/v1/workspace')
lock = threading.RLock()
training_slot = threading.BoundedSemaphore(1)
executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix='dataset-training')
logger = logging.getLogger(__name__)
ulb_cache = None
ulb_stamp = None


def now():
    return datetime.now(timezone.utc).isoformat()


def actor(value):
    if not value or len(value) > 80:
        raise HTTPException(401, 'Missing trusted actor')
    return value


def folder(dataset_id):
    if not re.fullmatch(r'[0-9a-f]{32}', dataset_id):
        raise HTTPException(404, 'Không tìm thấy bộ dữ liệu.')
    return DATASETS/dataset_id


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix('.tmp')
    tmp.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
    os.replace(tmp, path)


def read_owned(dataset_id, owner):
    path = folder(dataset_id)/'dataset.json'
    if not path.exists():
        raise HTTPException(404, 'Không tìm thấy bộ dữ liệu.')
    data = json.loads(path.read_text(encoding='utf-8'))
    if data['owner'] != actor(owner):
        raise HTTPException(404, 'Không tìm thấy bộ dữ liệu.')
    return data


def describe(data):
    labelled = [r for r in data['rows'] if r['label'] is not None]
    result = {k: v for k, v in data.items() if k != 'rows'}
    result.update(rowCount=len(data['rows']), labelledCount=len(labelled),
                  fraudCount=sum(r['label'] == 1 for r in labelled),
                  containsSynthetic=any(r['origin'] == 'synthetic' for r in data['rows']))
    return result


def editable(data):
    if data['status'] == 'training':
        raise HTTPException(409, 'Bộ dữ liệu đang huấn luyện. Hãy chờ hoàn tất.')


class CreateDataset(BaseModel):
    model_config = ConfigDict(extra='forbid')
    name: str = Field(min_length=1, max_length=100)

    @field_validator('name')
    @classmethod
    def not_blank(cls, value):
        if not value.strip():
            raise ValueError('Tên không được để trống')
        return value.strip()


class GenerateRows(BaseModel):
    model_config = ConfigDict(extra='forbid')
    samples: int = Field(default=1000, ge=1000, le=10000)
    seed: int = Field(default=42, ge=0, le=2147483647)


class ManualRow(BaseModel):
    model_config = ConfigDict(extra='forbid', allow_inf_nan=False)
    userId: str = Field(min_length=1, max_length=80)
    amount: float = Field(gt=0, le=1e11)
    currency: str = 'VND'
    merchant: str = Field(min_length=1, max_length=120)
    category: str
    country: str
    deviceId: str = Field(min_length=1, max_length=120)
    ipAddress: str = Field(min_length=1, max_length=60)
    timestamp: str
    label: int | None = Field(default=None, ge=0, le=1)


def validate_row(row):
    # Reuse native CSV validation, including IP, VND and timezone rules.
    import ipaddress
    from features import parse_time
    if row['currency'] != 'VND' or row['category'] not in {'grocery','electronics','travel','jewelry','crypto','gambling','other'}:
        raise HTTPException(422, 'Chỉ hỗ trợ VND và nhóm giao dịch hợp lệ.')
    if not re.fullmatch('[A-Z]{2}', row['country']):
        raise HTTPException(422, 'Quốc gia cần hai chữ hoa, ví dụ VN.')
    try:
        row['ipAddress'] = str(ipaddress.ip_address(row['ipAddress']))
        row['timestamp'] = parse_time(row['timestamp']).isoformat()
    except (ValueError, TypeError):
        raise HTTPException(422, 'IP hoặc thời gian không hợp lệ; thời gian cần múi giờ.')
    for key in ('userId', 'merchant', 'deviceId'):
        row[key] = row[key].strip()
        if not row[key]:
            raise HTTPException(422, 'Các trường định danh không được để trống.')
    return row


@router.get('/datasets')
def list_datasets(x_actor: str = Header(default='')):
    owner = actor(x_actor)
    with lock:
        return sorted([describe(d) for path in DATASETS.glob('*/dataset.json')
                       if (d := json.loads(path.read_text(encoding='utf-8')))['owner'] == owner],
                      key=lambda d: d['createdAt'], reverse=True)


@router.post('/datasets')
def create_dataset(req: CreateDataset, x_actor: str = Header(default='')):
    with lock:
        if len(list_datasets(x_actor)) >= 10:
            raise HTTPException(409, 'Mỗi tài khoản được tạo tối đa 10 bộ dữ liệu demo.')
        data = dict(id=uuid4().hex, owner=actor(x_actor), name=req.name, createdAt=now(),
                    status='draft', revision=0, rows=[], metrics=None, error=None)
        save(folder(data['id'])/'dataset.json', data)
        return describe(data)


@router.get('/datasets/{dataset_id}')
def detail(dataset_id: str, x_actor: str = Header(default='')):
    with lock:
        data = read_owned(dataset_id, x_actor)
        return dict(**describe(data), preview=data['rows'][-20:])


@router.delete('/datasets/{dataset_id}')
def delete_dataset(dataset_id: str, x_actor: str = Header(default='')):
    import shutil
    with lock:
        data = read_owned(dataset_id, x_actor); editable(data)
        path = folder(dataset_id)
        if path.is_symlink() or path.resolve().parent != DATASETS.resolve():
            raise HTTPException(409, 'Đường dẫn bộ dữ liệu không hợp lệ.')
        shutil.rmtree(path)
        return {'deleted': True}


@router.post('/datasets/{dataset_id}/rows')
def add_row(dataset_id: str, req: ManualRow, x_actor: str = Header(default='')):
    row = validate_row(req.model_dump())
    with lock:
        data = read_owned(dataset_id, x_actor); editable(data)
        if len(data['rows']) >= 10000:
            raise HTTPException(409, 'Bộ dữ liệu đã đạt giới hạn 10.000 giao dịch.')
        data['rows'].append(dict(**row, origin='manual'))
        data.update(status='draft', metrics=None, error=None, revision=data['revision']+1)
        save(folder(dataset_id)/'dataset.json', data)
        return describe(data)


@router.post('/datasets/{dataset_id}/generate')
def generate(dataset_id: str, req: GenerateRows, x_actor: str = Header(default='')):
    with lock:
        data = read_owned(dataset_id, x_actor); editable(data)
        if len(data['rows']) + req.samples > 10000:
            raise HTTPException(409, 'Tổng số giao dịch không được vượt 10.000.')
        if data.get('generation') is not None:
            raise HTTPException(409, 'Mỗi bộ chỉ sinh dữ liệu một lần để tránh lặp mẫu; hãy tạo bộ mới với seed khác.')
        data['rows'].extend(dict(**r, origin='synthetic') for r in synthetic(req.samples, req.seed))
        data.update(status='draft', metrics=None, error=None, revision=data['revision']+1,
                    generation=dict(seed=req.seed, samples=req.samples))
        save(folder(dataset_id)/'dataset.json', data)
        return describe(data)


def train_job(dataset_id, snapshot, provenance):
    try:
        env = {**os.environ, 'OMP_NUM_THREADS': '2'}
        result = subprocess.run([sys.executable, str(ROOT/'train.py'), '--csv', str(snapshot),
            '--output-dir', str(folder(dataset_id)/'model')], cwd=ROOT, env=env,
            capture_output=True, text=True, timeout=180)
        if result.returncode:
            raise ValueError('Cần ít nhất 500 mẫu có nhãn và cả hai lớp trong từng tập theo thời gian. Kiểm tra lại dữ liệu.')
        report = json.loads((folder(dataset_id)/'model'/'metrics.json').read_text(encoding='utf-8'))
        report.update(datasetSource=provenance, datasetId=dataset_id)
        model_path = folder(dataset_id)/'model'/'model.joblib'
        bundle = joblib.load(model_path)
        bundle['metadata'] = report
        joblib.dump(bundle, model_path)
        save(folder(dataset_id)/'model'/'metrics.json', report)
        with lock:
            path = folder(dataset_id)/'dataset.json'
            data = json.loads(path.read_text(encoding='utf-8'))
            data.update(status='ready', metrics=report, error=None, trainedAt=now())
            save(path, data)
    except Exception as exc:
        logger.exception('Dataset training failed for %s', dataset_id)
        with lock:
            path = folder(dataset_id)/'dataset.json'
            data = json.loads(path.read_text(encoding='utf-8'))
            data.update(status='failed', error=str(exc) if isinstance(exc, ValueError) else 'Không thể train. Hãy thử lại hoặc kiểm tra dữ liệu.')
            save(path, data)
    finally:
        training_slot.release()


@router.post('/datasets/{dataset_id}/train')
def start_train(dataset_id: str, x_actor: str = Header(default='')):
    with lock:
        data = read_owned(dataset_id, x_actor); editable(data)
        rows = [{k: v for k, v in r.items() if k != 'origin'} for r in data['rows'] if r['label'] is not None]
        if len(rows) < 500 or len({r['label'] for r in rows}) != 2:
            raise HTTPException(422, 'Cần ít nhất 500 giao dịch có nhãn, gồm cả hợp lệ và gian lận. Mẫu chưa có nhãn không dùng train.')
        if not training_slot.acquire(blocking=False):
            raise HTTPException(409, 'Đang có tác vụ huấn luyện khác. Hãy thử lại sau.')
        try:
            path = folder(dataset_id)/'snapshot.csv'
            with path.open('w', encoding='utf-8', newline='') as f:
                writer = csv.DictWriter(f, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
            # Validate before enqueueing and changing job state.
            load_csv(path)
            data.update(status='training', error=None, metrics=None)
            save(folder(dataset_id)/'dataset.json', data)
            source = 'user dataset containing synthetic simulations' if describe(data)['containsSynthetic'] else 'user entered labels not independently verified'
            executor.submit(train_job, dataset_id, path, source)
        except Exception:
            training_slot.release()
            raise
        return describe(data)


@router.post('/datasets/{dataset_id}/predict')
def predict_personal(dataset_id: str, req: ManualRow, x_actor: str = Header(default='')):
    from features import vector, NAMES
    row = validate_row(req.model_dump())
    with lock:
        data = read_owned(dataset_id, x_actor)
        if data['status'] != 'ready' or not data['metrics']:
            raise HTTPException(409, 'Huấn luyện phiên bản dữ liệu hiện tại trước khi chấm thử.')
        f = vector(row, data['rows'])
        bundle = joblib.load(folder(dataset_id)/'model'/'model.joblib')
        score = float(bundle['model'].predict_proba(np.array([[f[k] for k in NAMES]]))[0, 1])
        threshold = data['metrics']['validationMetrics']['threshold']
        return dict(score=score, threshold=threshold, prediction=int(score >= threshold),
                    modelVersion=data['metrics']['modelVersion'], features=f)


@router.get('/datasets/{dataset_id}/csv')
def download_csv(dataset_id: str, x_actor: str = Header(default='')):
    import io
    from fastapi.responses import Response
    with lock:
        data = read_owned(dataset_id, x_actor)
        output = io.StringIO(newline='')
        fields = list(ManualRow.model_fields)
        writer = csv.DictWriter(output, fieldnames=fields)
        writer.writeheader()
        for row in data['rows']:
            safe = {k: row[k] for k in fields}
            for k, v in safe.items():
                if isinstance(v, str) and v.startswith(('=', '+', '-', '@', '\t', '\r')):
                    safe[k] = "'" + v
            writer.writerow(safe)
        return Response(output.getvalue().encode('utf-8-sig'), media_type='text/csv')


def real_model():
    global ulb_cache, ulb_stamp
    path = ULB/'model.joblib'
    if not path.exists():
        raise HTTPException(503, 'Chưa có mô hình ULB; chạy train_ulb.py trước.')
    with lock:
        stamp = path.stat().st_mtime_ns
        if ulb_cache is None or ulb_stamp != stamp:
            candidate = joblib.load(path)
            if candidate['metadata']['featureNames'] != ULB_NAMES:
                raise HTTPException(503, 'Schema mô hình ULB không phù hợp.')
            ulb_cache, ulb_stamp = candidate, stamp
        return ulb_cache


@router.get('/ulb')
def ulb_report():
    return real_model()['metadata']


@router.get('/ulb/samples')
def ulb_samples():
    real_model()
    return json.loads((ULB/'samples.json').read_text(encoding='utf-8'))


class UlbPredict(BaseModel):
    model_config = ConfigDict(extra='forbid', allow_inf_nan=False)
    features: dict[str, float]


@router.post('/ulb/predict')
def ulb_predict(req: UlbPredict):
    f = req.features
    if set(f) != set(ULB_NAMES) or not all(np.isfinite(v) for v in f.values()) or f.get('Amount', -1) < 0 or f.get('Time', -1) < 0:
        raise HTTPException(422, 'Cần đúng 30 giá trị số hữu hạn: Time, V1..V28, Amount; Time và Amount không âm.')
    bundle = real_model()
    score = float(bundle['model'].predict_proba(np.array([[f[k] for k in ULB_NAMES]]))[0, 1])
    threshold = bundle['metadata']['validationMetrics']['threshold']
    return dict(score=score, threshold=threshold, prediction=int(score >= threshold),
                modelVersion=bundle['metadata']['modelVersion'], featureSchemaVersion='ulb-pca-v1')


def recover_jobs():
    with lock:
        for path in DATASETS.glob('*/dataset.json'):
            data = json.loads(path.read_text(encoding='utf-8'))
            if data['status'] == 'training':
                data.update(status='failed', error='Dịch vụ đã khởi động lại khi đang train. Có thể chạy lại.')
                save(path, data)
