#!/usr/bin/env python3
"""Emit exact retained public HTML inputs for the maintained Sites importer.
No project identities, authority, asset rewrites or deployment are created.
"""
import argparse, hashlib, json
from pathlib import Path
p=argparse.ArgumentParser(); p.add_argument('--input', type=Path, required=True); p.add_argument('--output', type=Path, required=True); a=p.parse_args()
d=json.loads(a.input.read_text()); assert d['schemaVersion']==1 and d['readOnly'] is True
assert not a.output.exists(), 'output must be new'
seen=set()
for page in d['pages']:
 assert page['id'] > 0 and page['route'].startswith('/') and page['route'] not in seen
 seen.add(page['route'])
 assert hashlib.sha256(page['html'].encode()).hexdigest()==page['htmlSha256'], 'HTML integrity mismatch'
a.output.mkdir(parents=True)
for page in d['pages']:
 (a.output/(str(page['id'])+'.html')).write_bytes(page['html'].encode())
(a.output/'import-plan.json').write_text(json.dumps({'consumer':d['consumer'],'authority':d['authority'],'pages':[{k:v for k,v in x.items() if k!='html'} for x in d['pages']]},indent=2)+'\n')
print(json.dumps({'pages':len(d['pages']),'routeCount':len(seen),'deploymentReady':False}))
