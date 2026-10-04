"""Private local SQLite recovery validation; never calls Cloudflare or reads credentials."""
import hashlib,json,os,sqlite3,tempfile
from contextlib import contextmanager, closing
from pathlib import Path

class RecoveryReadbackError(ValueError):
 """Publication occurred; inspect its destination instead of assuming no effect."""
 publication_completed = True
 destination_state = 'RECONCILIATION_REQUIRED'

def digest(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def schema(db):
 return {r[0]:r[1:] for r in db.execute("SELECT name,type,tbl_name,sql FROM sqlite_master WHERE sql IS NOT NULL AND name != '_cf_KV'")}
def validate(db,migrations):
 expected=sqlite3.connect(':memory:')
 try:
  for p in sorted(Path(migrations).glob('*.sql')): expected.executescript(p.read_text())
  if len(schema(expected))!=35 or schema(db)!=schema(expected): raise ValueError('canonical schema mismatch')
  if db.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]: raise ValueError('database integrity failure')
  if db.execute('PRAGMA foreign_key_check').fetchall(): raise ValueError('foreign key failure')
 finally: expected.close()
@contextmanager
def connect(p,immutable=False):
 db=sqlite3.connect(Path(p).resolve().as_uri()+'?mode=ro'+('&immutable=1' if immutable else ''),uri=True)
 try:
  db.execute('PRAGMA foreign_keys=ON');yield db
 finally:db.close()

def backup(source,destination,migrations):
 destination=Path(destination)
 fd=os.open(destination,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600);os.close(fd)
 try:
  with connect(source) as src:
   validate(src,migrations)
   with closing(sqlite3.connect(destination)) as target: src.backup(target);validate(target,migrations)
  with destination.open('rb') as f:os.fsync(f.fileno())
  fd=os.open(destination.parent,os.O_RDONLY)
  try:os.fsync(fd)
  finally:os.close(fd)
  return {'sha256':digest(destination),'schemaObjects':35,'counts':counts(destination),'migrationHashes':{p.name:digest(p) for p in sorted(Path(migrations).glob('*.sql'))}}
 except BaseException:
  # Preserve any incomplete private evidence; never claim it is a valid backup.
  raise

def counts(p,immutable=False):
 with connect(p,immutable=immutable) as db:
  names=[r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '_cf_KV'")]
  return {n:db.execute('SELECT COUNT(*) FROM "'+n.replace('"','""')+'"').fetchone()[0] for n in names}

def restore(source,destination,migrations,receipt,before_publish=None,before_seal=None):
 destination=Path(destination)
 if destination.exists() or destination.is_symlink():raise ValueError('restore destination must be new')
 current={p.name:digest(p) for p in sorted(Path(migrations).glob('*.sql'))}
 if current!=receipt['migrationHashes']:raise ValueError('migration provenance mismatch')
 if before_seal:before_seal()
 fd,temp=tempfile.mkstemp(prefix='.restore-',dir=destination.parent)
 try:
  # Consume only the sealed, receipt-matching image, never the live path.
  h=hashlib.sha256()
  with os.fdopen(fd,'wb') as staged, open(source,'rb') as original:
   while chunk:=original.read(1024*1024):
    staged.write(chunk);h.update(chunk)
   staged.flush();os.fsync(staged.fileno())
  if h.hexdigest()!=receipt['sha256']:raise ValueError('backup hash mismatch')
  with connect(temp,immutable=True) as sealed:validate(sealed,migrations)
  if counts(temp,immutable=True)!=receipt['counts']:raise ValueError('row count mismatch')
  with open(temp,'rb') as f:os.fsync(f.fileno())
  if before_publish:before_publish()
  # Hard-link publication is exclusive: a concurrent destination cannot be overwritten.
  os.link(temp,destination);os.unlink(temp)
  fd=os.open(destination.parent,os.O_RDONLY)
  try:os.fsync(fd)
  finally:os.close(fd)
 except BaseException:
  # Retain private temporary bytes on failed/ambiguous restoration for review.
  raise
 # Success requires the published path to match the receipt too. Equal row
 # counts do not prove that another process preserved the original image.
 try:
  if any(Path(str(destination)+suffix).exists() or Path(str(destination)+suffix).is_symlink() for suffix in ['-wal','-shm','-journal']):raise ValueError('published image has SQLite sidecars')
  if digest(destination)!=receipt['sha256']:raise ValueError('published image hash mismatch')
  # The sealed main file has already matched its receipt and has no sidecars.
  # Immutable mode prevents this read itself creating WAL/SHM files. It is
  # never used for the live source, whose committed WAL data must be backed up.
  restored_counts=counts(destination,immutable=True)
  if restored_counts!=receipt['counts']:raise ValueError('published image row count mismatch')
  if any(Path(str(destination)+suffix).exists() or Path(str(destination)+suffix).is_symlink() for suffix in ['-wal','-shm','-journal']):raise ValueError('published image sidecars appeared during readback')
  if digest(destination)!=receipt['sha256']:raise ValueError('published image changed during readback')
 except (OSError,ValueError,sqlite3.DatabaseError) as error:
  raise RecoveryReadbackError('restore publication completed but final readback failed; reconcile the destination') from error
 return {'status':'LOCAL_RESTORED','counts':restored_counts,'schemaObjects':35,'sha256':receipt['sha256']}

if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser();p.add_argument('operation',choices=['backup','restore']);p.add_argument('--source',required=True);p.add_argument('--destination',required=True);p.add_argument('--migrations',required=True);p.add_argument('--receipt',required=True);a=p.parse_args()
 if a.operation=='backup':
  result=backup(a.source,a.destination,a.migrations)
  fd=os.open(a.receipt,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
  with os.fdopen(fd,'w') as f:json.dump(result,f);f.flush();os.fsync(f.fileno())
  fd=os.open(Path(a.receipt).parent,os.O_RDONLY)
  try:os.fsync(fd)
  finally:os.close(fd)
 else:result=restore(a.source,a.destination,a.migrations,json.loads(Path(a.receipt).read_text()))
 print(json.dumps({'status':'LOCAL_VALIDATED','schemaObjects':result['schemaObjects'],'counts':result['counts']}))
