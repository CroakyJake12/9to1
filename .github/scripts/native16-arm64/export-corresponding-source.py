#!/usr/bin/env python3
"""Export committed source and exact two public gitlink sources, never worktree outputs."""
from pathlib import Path
import datetime,gzip,hashlib,io,json,os,subprocess,tarfile
root=Path.cwd();out=root/'evidence';out.mkdir(exist_ok=True);head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();target=out/'9to1-native16-corresponding-source.tar.gz';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
submodules=json.loads((Path(__file__).resolve().parent/'fork-extra-source-manifest.json').read_text())['submodules']
with tarfile.open(target,'w:gz') as dest:
 for work,prefix,pin in [(root,'9to1-preview-source/',head),*[(root/name,'9to1-preview-source/'+name+'/',pin) for name,pin in submodules.items()]]:
  if subprocess.check_output(['git','-C',str(work),'rev-parse','HEAD'],text=True).strip()!=pin:raise SystemExit('Source identity changed')
  raw=subprocess.check_output(['git','-C',str(work),'archive','--format=tar','--prefix='+prefix,pin])
  with tarfile.open(fileobj=io.BytesIO(raw),mode='r:') as src:
   for member in src:
    if Path(member.name).is_absolute() or '..' in Path(member.name).parts:raise SystemExit('Unsafe source path')
    if member.issym() or member.islnk():raise SystemExit('Unexpected source symlink: '+member.name)
    # Export exact committed blobs only. Generated output dirs are never added by seed.
    dest.addfile(member,src.extractfile(member) if member.isfile() else None)
receipt={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_commit':head,'archive':target.name,'bytes':target.stat().st_size,'sha256':sha(target),'gitlinks':submodules,'content':'Exact committed source plus exact gitlink sources; no .git/bin/obj/cache/profile added by seed','licence_status':'Preserved actual source and dependency notices; not a new blanket licence grant'}
(out/'corresponding-source-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
