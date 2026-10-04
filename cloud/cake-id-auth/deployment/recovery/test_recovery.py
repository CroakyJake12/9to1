import json,os,sqlite3,tempfile,uuid
from pathlib import Path
import recovery
migrations=Path(os.environ['CAKE_RECOVERY_MIGRATIONS'])
with tempfile.TemporaryDirectory() as tmp:
 root=Path(tmp);source=root/'source.db';db=sqlite3.connect(source);db.execute('PRAGMA foreign_keys=ON')
 for p in sorted(migrations.glob('*.sql')):db.executescript(p.read_text())
 user=str(uuid.uuid4());session=str(uuid.uuid4());client='fictional-public-client';resource='https://fictional.example.test/api';refresh=str(uuid.uuid4())
 def insert(table,**values):
  fields=','.join('"'+k+'"' for k in values);db.execute('INSERT INTO "'+table+'" ('+fields+') VALUES ('+','.join('?' for _ in values)+')',list(values.values()))
 insert('user',id=user,name='Fictional recovery fixture',email='recovery@example.test',emailVerified=1,createdAt='2026-10-04',updatedAt='2026-10-04',username='recovery-fixture',profileRevision=7)
 insert('session',id=session,expiresAt='2030-01-01',token=uuid.uuid4().hex,createdAt='2026-10-04',updatedAt='2026-10-04',userId=user)
 insert('account',id=str(uuid.uuid4()),accountId=user,providerId='credential',userId=user,password='fictional-not-loginable',createdAt='2026-10-04',updatedAt='2026-10-04')
 insert('jwks',id=str(uuid.uuid4()),publicKey='fictional-public-bytes',privateKey='fictional-private-bytes-not-a-real-key',createdAt='2026-10-04',alg='RS256')
 insert('oauthClient',id=str(uuid.uuid4()),clientId=client,userId=user,redirectUris='["https://fictional.example.test/callback"]',requirePKCE=1)
 insert('oauthResource',id=str(uuid.uuid4()),identifier=resource,name='Fictional resource')
 insert('oauthClientResource',id=str(uuid.uuid4()),clientId=client,resourceId=resource)
 insert('oauthRefreshToken',id=refresh,token=uuid.uuid4().hex,clientId=client,sessionId=session,userId=user,expiresAt='2030-01-01',createdAt='2026-10-04',scopes='openid profile',resources=json.dumps([resource]))
 insert('oauthAccessToken',id=str(uuid.uuid4()),token=uuid.uuid4().hex,clientId=client,sessionId=session,userId=user,refreshId=refresh,expiresAt='2030-01-01',createdAt='2026-10-04',scopes='openid profile')
 insert('oauthConsent',id=str(uuid.uuid4()),clientId=client,userId=user,scopes='openid profile',createdAt='2026-10-04',updatedAt='2026-10-04')
 db.commit();before=list(db.iterdump());db.close();source_hash=recovery.digest(source)
 backup=root/'backup.db';receipt=recovery.backup(source,backup,migrations);dest=root/'restored.db';recovery.restore(backup,dest,migrations,receipt)
 with recovery.connect(dest) as check:
  if list(check.iterdump())!=before:raise RuntimeError('restored values changed')
 if source_hash!=recovery.digest(source):raise RuntimeError('source modified')
 if backup.stat().st_mode&0o777!=0o600 or dest.stat().st_mode&0o777!=0o600:raise RuntimeError('private mode lost')
 controls=['canonical35/UUID/profileRevision/JWKS/OAuth exact restoration','source unchanged/private modes']
 def denied(name,src,rec,hook=None):
  output=root/(name+'.db')
  try:recovery.restore(src,output,migrations,rec,hook)
  except (ValueError,sqlite3.DatabaseError,RuntimeError):pass
  else:raise RuntimeError('negative accepted: '+name)
  if output.exists():raise RuntimeError('failed restore published: '+name)
  controls.append(name)
 bad=root/'corrupt.db';bad.write_bytes(b'not SQLite');r=dict(receipt,sha256=recovery.digest(bad));denied('corruption',bad,r)
 denied('hash-mismatch',backup,dict(receipt,sha256='0'*64))
 denied('interrupted-before-publication',backup,receipt,lambda:(_ for _ in ()).throw(RuntimeError('injected interruption')))
 existing_hash=recovery.digest(dest)
 try:recovery.restore(backup,dest,migrations,receipt)
 except ValueError:pass
 else:raise RuntimeError('existing destination overwritten')
 if recovery.digest(dest)!=existing_hash:raise RuntimeError('existing destination changed')
 controls.append('existing destination preserved')
 invalid=root/'invalid.db';invalid.write_bytes(backup.read_bytes())
 with sqlite3.connect(invalid) as d:d.execute('PRAGMA foreign_keys=OFF');d.execute('UPDATE session SET userId=?',('missing-parent',))
 denied('foreign-key-corruption',invalid,dict(receipt,sha256=recovery.digest(invalid)))
 with sqlite3.connect(invalid) as d:d.execute('CREATE TABLE invented (id TEXT)')
 denied('schema-corruption',invalid,dict(receipt,sha256=recovery.digest(invalid)))
 print(json.dumps({'status':'PASS','controls':controls,'canonicalSchemaObjects':35,'providerRestoration':'NOT_RUN','realSigningKeyAcceptance':'NOT_CLAIMED'}))
# Sealed-image controls: row counts alone must never authorize changed data.
with tempfile.TemporaryDirectory() as tmp:
 root=Path(tmp);original=root/'original.db';d=sqlite3.connect(original)
 for p in sorted(migrations.glob('*.sql')):d.executescript(p.read_text())
 d.commit();d.close();image=root/'backup.db';receipt=recovery.backup(original,image,migrations)
 for name,hook in [('same-count-value-tamper',False),('changed-source-before-seal',True)]:
  candidate=root/(name+'-source.db');candidate.write_bytes(image.read_bytes());output=root/(name+'-output.db')
  def change():
   with sqlite3.connect(candidate) as live:live.execute("UPDATE cake_reserved_usernames SET reason='fictional changed value'")
  if not hook:change()
  try:recovery.restore(candidate,output,migrations,receipt,before_seal=change if hook else None)
  except ValueError:pass
  else:raise RuntimeError('changed image accepted')
  if output.exists():raise RuntimeError('changed image published')
 print(json.dumps({'status':'PASS','sealedImageControls':2,'privateValuesPrinted':False}))
# A matching staged image can drift at the actual publication boundary,
# including a change preserving every row count before final readback.
with tempfile.TemporaryDirectory() as tmp:
 root=Path(tmp);source=root/'source.db'
 with sqlite3.connect(source) as db:
  for p in sorted(migrations.glob('*.sql')):db.executescript(p.read_text())
 image=root/'backup.db';receipt=recovery.backup(source,image,migrations)
 controls=[]
 for name in ['same-count-published-drift','corrupt-published-image']:
  dest=root/(name+'.db');original_unlink=recovery.os.unlink
  def change_after_publication(path,*args,**kwargs):
   result=original_unlink(path,*args,**kwargs)
   if Path(path).name.startswith('.restore-') and dest.exists():
    if name=='same-count-published-drift':
     with sqlite3.connect(dest) as changed:changed.execute("UPDATE cake_reserved_usernames SET reason='fictional published value drift'")
    else:dest.write_bytes(b'fictional invalid SQLite image')
   return result
  recovery.os.unlink=change_after_publication
  try:
   try:recovery.restore(image,dest,migrations,receipt)
   except recovery.RecoveryReadbackError as error:
    if not error.publication_completed or error.destination_state!='RECONCILIATION_REQUIRED':raise RuntimeError('publication outcome lost')
   else:raise RuntimeError('changed published image reported restored')
  finally:recovery.os.unlink=original_unlink
  if not dest.exists() or recovery.digest(dest)==receipt['sha256']:raise RuntimeError('published drift evidence not retained')
  if recovery.digest(image)!=receipt['sha256']:raise RuntimeError('original backup changed')
  if name=='same-count-published-drift' and recovery.counts(dest)!=receipt['counts']:raise RuntimeError('same-count control changed its criterion')
  controls.append(name)
 # Copying only the source .db would lose committed values still in its WAL.
 with sqlite3.connect(source) as live:
  if live.execute('PRAGMA journal_mode=WAL').fetchone()!=('wal',):raise RuntimeError('WAL fixture unavailable')
  live.execute('PRAGMA wal_autocheckpoint=0')
  live.execute("UPDATE cake_reserved_usernames SET reason='fictional committed WAL value'");live.commit()
  expected=list(live.iterdump());wal_backup=root/'wal-backup.db';wal_receipt=recovery.backup(source,wal_backup,migrations)
  restored=root/'wal-restored.db';result=recovery.restore(wal_backup,restored,migrations,wal_receipt)
  with recovery.connect(restored) as actual:
   if list(actual.iterdump())!=expected:raise RuntimeError('committed WAL state lost')
  if result['sha256']!=wal_receipt['sha256']:raise RuntimeError('successful readback identity lost')
  # A held-open writer can change SQLite-visible values only in a sidecar,
  # preserving the restored main-file hash and every row count.
  drift=root/'wal-drift.db';original_unlink=recovery.os.unlink;holders=[]
  def change_wal_after_publication(path,*args,**kwargs):
   result=original_unlink(path,*args,**kwargs)
   if Path(path).name.startswith('.restore-') and drift.exists():
    changed=sqlite3.connect(drift);holders.append(changed)
    changed.execute('PRAGMA wal_autocheckpoint=0')
    changed.execute("UPDATE cake_reserved_usernames SET reason='fictional WAL-only drift'");changed.commit()
   return result
  recovery.os.unlink=change_wal_after_publication
  try:
   try:recovery.restore(wal_backup,drift,migrations,wal_receipt)
   except recovery.RecoveryReadbackError as error:
    if not error.publication_completed:raise RuntimeError('WAL publication outcome lost')
   else:raise RuntimeError('WAL-only changed image reported restored')
   if recovery.digest(drift)!=wal_receipt['sha256'] or recovery.counts(drift)!=wal_receipt['counts']:raise RuntimeError('WAL-only negative criterion changed')
   if not Path(str(drift)+'-wal').exists():raise RuntimeError('WAL evidence missing')
  finally:
   recovery.os.unlink=original_unlink
   for changed in holders:changed.close()
  controls.append('same-count-WAL-only-published-drift')
 controls.append('live-WAL-committed-state-recovery')
 print(json.dumps({'status':'PASS','publishedReadbackControls':controls,'providerRestoration':'NOT_RUN','privateValuesPrinted':False}))
