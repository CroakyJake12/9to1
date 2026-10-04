#!/usr/bin/env python3
"""Emit exact retained public HTML inputs for the maintained Sites importer.
No project identities, authority, asset rewrites or deployment are created.
"""
import argparse, hashlib, json
from pathlib import Path
p=argparse.ArgumentParser(); p.add_argument('--input', type=Path, required=True); p.add_argument('--output', type=Path, required=True); a=p.parse_args()
d=json.loads(a.input.read_text())
if d['schemaVersion'] != 1 or d['readOnly'] is not True:
 raise ValueError('unsupported or mutable source capture')
if a.output.exists() or a.output.is_symlink():
 raise ValueError('output must be new')
seen=set(); ids=set()
for page in d['pages']:
 if type(page['id']) is not int or page['id'] <= 0 or page['id'] in ids:
  raise ValueError('page IDs must be unique positive integers')
 if not isinstance(page['route'], str) or not page['route'].startswith('/') or page['route'] in seen:
  raise ValueError('routes must be unique absolute paths')
 ids.add(page['id'])
 seen.add(page['route'])
 if hashlib.sha256(page['html'].encode()).hexdigest() != page['htmlSha256']:
  raise ValueError('HTML integrity mismatch')
# Resolve every plan field before creating output, including malformed metadata.
plan=json.dumps({'consumer':d['consumer'],'authority':d['authority'],'pages':[{k:v for k,v in x.items() if k!='html'} for x in d['pages']]},indent=2)+'\n'
a.output.mkdir(parents=True)
for page in d['pages']:
 (a.output/(str(page['id'])+'.html')).write_bytes(page['html'].encode())
(a.output/'import-plan.json').write_text(plan)
print(json.dumps({'pages':len(d['pages']),'routeCount':len(seen),'deploymentReady':False}))
