import pathlib,sqlite3,tempfile,importlib.util,json
from contextlib import contextmanager
r=pathlib.Path(__file__).parent;migrations=pathlib.Path('/workspace/team-c/c2-hosted-package/cloud/cake-id-auth/migrations')
for name in ['original','fixed']:
 spec=importlib.util.spec_from_file_location(name,r/(name+'.py'));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 with tempfile.TemporaryDirectory() as tmp:
  root=pathlib.Path(tmp);source=root/'source.db';db=sqlite3.connect(source)
  for p in sorted(migrations.glob('*.sql')):db.executescript(p.read_text())
  expected=db.execute("SELECT reason FROM cake_reserved_usernames WHERE username='croakyjake'").fetchone()[0];db.commit();db.close()
  image=root/'image.db';receipt=m.backup(source,image,migrations);original_connect=m.connect;mutated=False
  @contextmanager
  def connect(p):
   global mutated
   if not mutated:
    mutated=True
    with sqlite3.connect(image) as writer:writer.execute("UPDATE cake_reserved_usernames SET reason='controlled changed value' WHERE username='croakyjake'")
   with original_connect(p) as db:yield db
  m.connect=connect;output=root/'restore.db';m.restore(image,output,migrations,receipt)
  with sqlite3.connect(output) as db:observed=db.execute("SELECT reason FROM cake_reserved_usernames WHERE username='croakyjake'").fetchone()[0]
  same=observed==expected
  print(json.dumps({'source':name,'sameCountMutationAtFirstSQLiteRead':mutated,'restoredReceiptOriginalValues':same,'published':output.exists()}))
  if same!=(name=='fixed'):raise RuntimeError('unexpected race-control outcome')
