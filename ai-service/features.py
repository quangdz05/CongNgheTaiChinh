"""Schema v1. Must stay aligned with Application/FeatureBuilder; UTC, VND, population std."""
from collections import defaultdict, deque
from datetime import datetime, timedelta, timezone
import math

SCHEMA = "1"
NAMES = ["amount", "transactionCount1h", "transactionCount24h", "amountZScore", "isNewDevice", "isNewLocation", "sharedDeviceUsers", "sharedIpUsers", "hourOfDay", "merchantRisk"]
RISKS = {"gambling": 1., "crypto": .9, "jewelry": .75, "electronics": .65, "travel": .55}

def parse_time(value):
    t = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    if t.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return t.astimezone(timezone.utc)

def vector(tx, history):
    now = parse_time(tx["timestamp"])
    prior = [h for h in history if now-timedelta(hours=24) <= parse_time(h["timestamp"]) < now]
    user = [h for h in prior if h["userId"] == tx["userId"]]
    return _make(tx, now, user,
                 len({h['userId'] for h in prior if h['deviceId'] == tx['deviceId']}),
                 len({h['userId'] for h in prior if h['ipAddress'] == tx['ipAddress']}))

def _make(tx, now, user, shared_device, shared_ip):
    amounts = [float(h["amount"]) for h in user]
    mean = sum(amounts)/len(amounts) if amounts else float(tx["amount"])
    std = math.sqrt(sum((a-mean)**2 for a in amounts)/len(amounts)) if len(amounts)>1 else 1.
    return dict(zip(NAMES, [float(tx["amount"]), float(sum(parse_time(h["timestamp"]) >= now-timedelta(hours=1) for h in user)), float(len(user)), max(-10.,min(10.,(float(tx["amount"])-mean)/max(std,1.))), float(not any(h["deviceId"]==tx["deviceId"] for h in user)), float(not any(h["country"]==tx["country"] for h in user)), float(shared_device), float(shared_ip), float(now.hour), RISKS.get(tx['category'],.1)]))

def engineer(rows):
    """Compute before update; equal timestamps are a batch and cannot see each other."""
    from itertools import groupby
    users, devices, ips = defaultdict(deque), defaultdict(deque), defaultdict(deque)
    output=[]
    for timestamp, group in groupby(rows, key=lambda r:r['timestamp']):
        batch=list(group);now=parse_time(timestamp);cutoff=now-timedelta(hours=24)
        for tx in batch:
            for mapping,key in [(users,tx['userId']),(devices,tx['deviceId']),(ips,tx['ipAddress'])]:
                q=mapping[key]
                while q and parse_time(q[0]['timestamp'])<cutoff:q.popleft()
            output.append(_make(tx,now,list(users[tx['userId']]),len({h['userId'] for h in devices[tx['deviceId']]}),len({h['userId'] for h in ips[tx['ipAddress']]})))
        for tx in batch:
            users[tx['userId']].append(tx);devices[tx['deviceId']].append(tx);ips[tx['ipAddress']].append(tx)
    return output

def default_rule_score(f):
    scores=[0.]
    if f['amount']>=250_000_000:scores.append(1.)
    elif f['amount']>=125_000_000:scores.append(.7)
    if f['transactionCount1h']>=10:scores.append(.85)
    if f['transactionCount24h']>=3 and f['amountZScore']>=2.5:scores.append(.9 if f['amountZScore']>=4 else .6)
    if f['sharedDeviceUsers']>=3:scores.append(.9)
    if f['sharedIpUsers']>=5:scores.append(.55)
    if f['transactionCount24h']>=3 and f['isNewDevice'] and f['isNewLocation']:scores.append(.8)
    if f['hourOfDay']<6 and f['amount']>=50_000_000:scores.append(.7)
    if f['merchantRisk']>=.8:scores.append(.65)
    return max(scores)
