"""Offline training only. Chronological 60/20/20, validation threshold, untouched test."""
import argparse, csv, hashlib, json, os, platform, ipaddress, re
from datetime import datetime, timedelta, timezone
from pathlib import Path
import joblib
import numpy as np
import sklearn
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.metrics import average_precision_score, confusion_matrix, f1_score, precision_score, recall_score, roc_auc_score
from features import SCHEMA, NAMES, engineer, parse_time, default_rule_score

ROOT=Path(__file__).resolve().parent
def synthetic(n,seed):
    rng=np.random.default_rng(seed);start=datetime(2026,1,1,tzinfo=timezone.utc);rows=[]
    for i in range(n):
        attack=rng.random()<.09;user=int(rng.integers(0,180));fresh=rng.random()<(.70 if attack else .08)
        device=f"ring-{int(rng.integers(0,5))}" if attack and rng.random()<.55 else f"device-{user}-{'new-'+str(i) if fresh else 'known'}"
        amount=min(1e10,round(float(rng.lognormal(16.8 if attack else 13.6,1.25)),0))
        category=str(rng.choice(['grocery','electronics','travel','crypto','jewelry'],p=[.15,.15,.1,.45,.15] if attack else [.65,.15,.12,.02,.06]))
        rows.append(dict(userId=f"USER-{user:04}",amount=amount,currency='VND',merchant='SHOP-'+category,category=category,country='US' if attack and rng.random()<.4 else 'VN',deviceId=device,ipAddress=f"192.0.2.{(user%180)+1}",timestamp=(start+timedelta(seconds=i*90)).isoformat(),label=int(rng.random()<(.78 if attack else .015))))
    return rows

def load_csv(path):
    with open(path,encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
    required={'userId','amount','currency','merchant','category','country','deviceId','ipAddress','timestamp','label'}
    if not rows or not required.issubset(rows[0]):raise ValueError('CSV columns required: '+','.join(sorted(required)))
    for r in rows:
        r['amount']=float(r['amount']);r['label']=int(r['label']);r['timestamp']=parse_time(r['timestamp']).isoformat()
        if r['currency']!='VND' or not np.isfinite(r['amount']) or not 0<r['amount']<=1e11 or r['label'] not in (0,1):raise ValueError('VND, positive finite amount and binary verified label required')
        if any(not r[k].strip() for k in required-{'label','amount'}):raise ValueError('Required field is empty')
        if r['category'] not in {'grocery','electronics','travel','jewelry','crypto','gambling','other'} or not re.fullmatch('[A-Z]{2}',r['country']):raise ValueError('Category or country does not match API schema')
        r['userId']=r['userId'].strip();r['deviceId']=r['deviceId'].strip();r['ipAddress']=str(ipaddress.ip_address(r['ipAddress']))
    return sorted(rows,key=lambda r:parse_time(r['timestamp']))

def metrics(y,p,threshold):
    pred=np.asarray(p)>=threshold
    return dict(precision=float(precision_score(y,pred,zero_division=0)),recall=float(recall_score(y,pred,zero_division=0)),f1=float(f1_score(y,pred,zero_division=0)),prAuc=float(average_precision_score(y,p)),rocAuc=float(roc_auc_score(y,p)),confusionMatrix=confusion_matrix(y,pred,labels=[0,1]).tolist(),threshold=float(threshold))

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--csv',type=Path);parser.add_argument('--synthetic',action='store_true');parser.add_argument('--samples',type=int,default=8000);parser.add_argument('--seed',type=int,default=42);args=parser.parse_args()
    if bool(args.csv)==bool(args.synthetic):parser.error('Choose exactly one of --csv FILE or --synthetic')
    if args.synthetic and args.samples<1000:parser.error('Use at least 1000 synthetic samples')
    rows=load_csv(args.csv) if args.csv else synthetic(args.samples,args.seed)
    if len(rows)<500:raise ValueError('At least 500 chronological samples are required')
    feats=engineer(rows);X=np.array([[f[k] for k in NAMES] for f in feats]);y=np.array([r['label'] for r in rows]);a=int(len(rows)*.6);b=int(len(rows)*.8)
    # Keep identical timestamps in one partition.
    while a<len(rows) and rows[a]['timestamp']==rows[a-1]['timestamp']:a+=1
    b=max(b,a+1)
    while b<len(rows) and rows[b]['timestamp']==rows[b-1]['timestamp']:b+=1
    for subset in [y[:a],y[a:b],y[b:]]:
        if len(set(subset))!=2:raise ValueError('Each chronological partition must contain both classes; supply more representative data')
    model=HistGradientBoostingClassifier(max_iter=160,max_leaf_nodes=15,learning_rate=.08,l2_regularization=2.,early_stopping=False,random_state=args.seed,class_weight='balanced')
    model.fit(X[:a],y[:a]);val=model.predict_proba(X[a:b])[:,1]
    thresholds=np.arange(.05,.96,.025);threshold=float(max(thresholds,key=lambda t:f1_score(y[a:b],val>=t,zero_division=0)))
    prob=model.predict_proba(X[b:])[:,1];rule=np.array([default_rule_score(f) for f in feats[b:]])
    hybrid=.5*rule+.5*prob;hybrid=np.where(rule>=1,np.maximum(hybrid,.95),hybrid)
    # For binary BLOCK comparison, strong rule override is retained.
    hybrid_block=(hybrid>=.8)|(rule>=.8)
    version=datetime.now(timezone.utc).strftime('hgb-%Y%m%dT%H%M%SZ');art=ROOT/'artifacts';art.mkdir(exist_ok=True)
    report=dict(modelVersion=version,featureSchemaVersion=SCHEMA,featureNames=NAMES,datasetSource='csv' if args.csv else 'synthetic',datasetSha256=hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest(),trainedAt=datetime.now(timezone.utc).isoformat(),seed=args.seed,split=dict(train=a,validation=b-a,test=len(rows)-b,method='chronological 60/20/20; equal timestamps kept together'),fraudRate=float(y.mean()),validationMetrics=metrics(y[a:b],val,threshold),testMetrics=metrics(y[b:],prob,threshold),rulesBaseline=metrics(y[b:],rule,.8),hybridBlockMetrics=metrics(y[b:],hybrid,.8),hybridBlockOverride=dict(precision=float(precision_score(y[b:],hybrid_block,zero_division=0)),recall=float(recall_score(y[b:],hybrid_block,zero_division=0)),f1=float(f1_score(y[b:],hybrid_block,zero_division=0)),confusionMatrix=confusion_matrix(y[b:],hybrid_block).tolist()),limitations=['Synthetic results do not demonstrate real-world fraud performance.' if args.synthetic else 'CSV labels, provenance and representativeness require independent verification.','Model score is not a calibrated probability.','Stored historical features include only earlier transactions, never labels.','ML threshold selected on validation; C# hybrid uses separately configured policy thresholds.'],runtime=dict(python=platform.python_version(),sklearn=sklearn.__version__,numpy=np.__version__),parameters=model.get_params())
    bundle=dict(model=model,metadata=report)
    tmp=art/'model.tmp';joblib.dump(bundle,tmp);os.replace(tmp,art/'model.joblib')
    (art/'metrics.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    data=ROOT/'data';data.mkdir(exist_ok=True)
    with (data/'training_transactions.csv').open('w',encoding='utf-8',newline='') as f:w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print(json.dumps({k:report[k] for k in ['modelVersion','datasetSource','split','testMetrics']},indent=2))
if __name__=='__main__':main()
