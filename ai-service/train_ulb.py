"""Train the real ULB benchmark with its own input schema, never the VND schema."""
import argparse
import hashlib
import json
import os
from datetime import datetime, timezone
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
import sklearn
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.metrics import average_precision_score, confusion_matrix, f1_score, precision_score, recall_score, roc_auc_score

ROOT = Path(__file__).resolve().parent
ULB_NAMES = ['Time'] + [f'V{i}' for i in range(1, 29)] + ['Amount']
SOURCE = 'https://storage.googleapis.com/download.tensorflow.org/data/creditcard.csv'


def evaluate(y, scores, threshold):
    predicted = scores >= threshold
    return dict(precision=float(precision_score(y, predicted, zero_division=0)),
                recall=float(recall_score(y, predicted, zero_division=0)),
                f1=float(f1_score(y, predicted, zero_division=0)),
                prAuc=float(average_precision_score(y, scores)),
                rocAuc=float(roc_auc_score(y, scores)),
                confusionMatrix=confusion_matrix(y, predicted, labels=[0, 1]).tolist(),
                threshold=float(threshold))


def chronological_split(frame):
    # Keep every equal-time group in the same partition.
    frame = frame.sort_values('Time', kind='stable').reset_index(drop=True)
    times = frame.Time.to_numpy()
    a, b = int(len(frame) * .6), int(len(frame) * .8)
    while a < len(frame) and times[a] == times[a - 1]:
        a += 1
    b = max(b, a + 1)
    while b < len(frame) and times[b] == times[b - 1]:
        b += 1
    if a >= b or b >= len(frame):
        raise ValueError('Not enough distinct times for three chronological partitions')
    for part in (frame.iloc[:a], frame.iloc[a:b], frame.iloc[b:]):
        if part.Class.nunique() != 2:
            raise ValueError('Every partition must contain both classes')
    return frame, a, b


def train(csv_path, output):
    frame = pd.read_csv(csv_path)
    if set(frame.columns) != set(ULB_NAMES + ['Class']):
        raise ValueError('ULB CSV must contain Time, V1..V28, Amount, Class')
    if not np.isfinite(frame.to_numpy(dtype=float)).all() or not frame.Class.isin([0, 1]).all():
        raise ValueError('Finite numeric values and binary labels are required')
    if (frame.Time < 0).any() or (frame.Amount < 0).any():
        raise ValueError('Time and Amount must be nonnegative')
    original = len(frame)
    frame = frame.drop_duplicates()
    frame, a, b = chronological_split(frame)
    X, y = frame[ULB_NAMES].to_numpy(), frame.Class.to_numpy(dtype=int)
    model = HistGradientBoostingClassifier(max_iter=160, max_leaf_nodes=15, learning_rate=.08,
                l2_regularization=2., early_stopping=False, class_weight='balanced', random_state=42)
    model.fit(X[:a], y[:a])
    val = model.predict_proba(X[a:b])[:, 1]
    thresholds = np.unique(np.r_[np.arange(.01, 1., .01), .995, .999])
    threshold = float(max(thresholds, key=lambda t: f1_score(y[a:b], val >= t, zero_division=0)))
    scores = model.predict_proba(X[b:])[:, 1]
    report = dict(modelVersion=datetime.now(timezone.utc).strftime('ulb-hgb-%Y%m%dT%H%M%SZ'),
        featureSchemaVersion='ulb-pca-v1', featureNames=ULB_NAMES, datasetSource='ULB real anonymized card transactions',
        sourceUrl=SOURCE, datasetSha256=hashlib.file_digest(Path(csv_path).open('rb'), 'sha256').hexdigest(),
        trainedAt=datetime.now(timezone.utc).isoformat(), seed=42, originalRows=original,
        duplicatesRemoved=original-len(frame), fraudRate=float(y.mean()),
        split=dict(train=a, validation=b-a, test=len(frame)-b, method='chronological 60/20/20; equal times kept together; exact duplicates removed'),
        validationMetrics=evaluate(y[a:b], val, threshold), testMetrics=evaluate(y[b:], scores, threshold),
        parameters=model.get_params(), runtime=dict(sklearn=sklearn.__version__, numpy=np.__version__),
        limitations=['Historical European card benchmark, not validation for Vietnamese transactions.',
            'PCA inputs V1..V28 cannot be reconstructed from the VND transaction form.',
            'Scores are not calibrated probabilities. prAuc is average precision.',
            'Threshold chosen on validation; test labels never used for fitting or threshold selection.'])
    output = Path(output); output.mkdir(parents=True, exist_ok=True)
    joblib.dump(dict(model=model, metadata=report), output/'model.tmp')
    os.replace(output/'model.tmp', output/'model.joblib')
    (output/'metrics.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    # Illustrative test samples, not a new estimate of model performance.
    rng = np.random.default_rng(42)
    chosen = np.r_[rng.choice(np.flatnonzero(y[b:] == 0), 10, replace=False),
                   rng.choice(np.flatnonzero(y[b:] == 1), 10, replace=False)] + b
    rng.shuffle(chosen)
    samples = [dict(id=int(i), features={k: float(frame.iloc[i][k]) for k in ULB_NAMES}, label=int(y[i])) for i in chosen]
    (output/'samples.json').write_text(json.dumps(samples), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--csv', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'artifacts'/'ulb')
    args = parser.parse_args()
    train(args.csv, args.output_dir)
