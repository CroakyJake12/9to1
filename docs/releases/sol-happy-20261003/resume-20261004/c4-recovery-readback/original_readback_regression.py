"""Reproduce preserved false success on immutable original C-owned source.

Run: python3 -B original_readback_regression.py REPO
Exit 1 preserves an observed original defect; fixture bytes stay private.
"""
import hashlib,json,sqlite3,subprocess,sys,tempfile,types
from pathlib import Path

repo=Path(sys.argv[1]).resolve()
basis='7254ca4ee535839a2c9ef3f52fed6f5f51c0034a'
pin=json.loads((Path(__file__).parent/'index.json').read_text())
raw=subprocess.check_output(['git','-C',str(repo),'show',basis+':cloud/cake-id-auth/deployment/recovery/recovery.py'])
if len(raw)!=pin['originalRecoveryBytes'] or hashlib.sha256(raw).hexdigest()!=pin['originalRecoverySHA256']:raise RuntimeError('original source custody mismatch')
recovery=types.ModuleType('original_recovery');exec(compile(raw,'pinned-original-recovery.py','exec'),recovery.__dict__)
migrations=repo/'cloud/cake-id-auth/migrations';rows=[]
for wal in [False,True]:
 with tempfile.TemporaryDirectory() as tmp:
  root=Path(tmp);src=root/'source.db';live=sqlite3.connect(src)
  for p in sorted(migrations.glob('*.sql')):live.executescript(p.read_text())
  if wal:
   live.execute('PRAGMA journal_mode=WAL');live.execute('PRAGMA wal_autocheckpoint=0')
   live.execute("UPDATE cake_reserved_usernames SET reason='fictional WAL base'");live.commit()
  image=root/'backup.db';receipt=recovery.backup(src,image,migrations);dest=root/'restored.db';unlink=recovery.os.unlink;holders=[]
  def change(path,*args,**kwargs):
   result=unlink(path,*args,**kwargs)
   if Path(path).name.startswith('.restore-') and dest.exists():
    changed=sqlite3.connect(dest)
    if wal:changed.execute('PRAGMA wal_autocheckpoint=0')
    changed.execute("UPDATE cake_reserved_usernames SET reason='fictional post-publication drift'");changed.commit()
    if wal:holders.append(changed)
    else:changed.close()
   return result
  recovery.os.unlink=change
  try:
   outcome=recovery.restore(image,dest,migrations,receipt)
   same_counts=outcome['counts']==receipt['counts']
   main_hash_same=recovery.digest(dest)==receipt['sha256']
   with recovery.connect(dest) as db:
    source_data_same=list(db.iterdump())==list(live.iterdump())
   false_success=outcome['status']=='LOCAL_RESTORED' and same_counts and not source_data_same
   rows.append({'name':'WAL-only-drift' if wal else 'main-image-drift','status':'FAIL_FALSE_SUCCESS' if false_success else 'UNEXPECTED','sameCounts':same_counts,'mainHashMatches':main_hash_same,'publishedValuesMatchOriginal':source_data_same})
  finally:
   recovery.os.unlink=unlink
   for changed in holders:changed.close()
   live.close()
print(json.dumps({'basis':basis,'originalRegression':rows,'privateValuesPrinted':False,'providerWrites':0}))
sys.exit(1 if all(row['status']=='FAIL_FALSE_SUCCESS' for row in rows) else 2)
