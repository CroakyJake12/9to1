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
