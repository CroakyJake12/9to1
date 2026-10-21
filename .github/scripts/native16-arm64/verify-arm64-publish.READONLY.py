#!/usr/bin/env python3
"""Read-only input inspection; never executes ARM or modifies package/source. JSON goes to stdout."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,re,struct,subprocess,sys
p=argparse.ArgumentParser();p.add_argument('publish',type=Path);args=p.parse_args();root=args.publish.resolve();fail=[];warn=[];files=[];elfs=[]
if not root.is_dir():raise SystemExit('Publish directory missing')
sha=lambda b:hashlib.sha256(b).hexdigest()
required=['Haven','Haven.dll','Haven.deps.json','Haven.runtimeconfig.json','CakeOS.Cui.Runtime.dll','libhostfxr.so','libhostpolicy.so','libcoreclr.so','System.Private.CoreLib.dll']
for n in required:
 if not (root/n).is_file():fail.append('Missing self-contained payload '+n)
for n in ['libSkiaSharp.so','libHarfBuzzSharp.so','libe_sqlite3.so']:
 candidates=[root/n,root/'runtimes/linux-arm64/native'/n]
 if not any(c.is_file() for c in candidates):fail.append('Missing effective ARM native asset '+n)
rc={};deps={}
try:
 rc=json.loads((root/'Haven.runtimeconfig.json').read_text()).get('runtimeOptions',{})
 if rc.get('framework') or rc.get('frameworks'):fail.append('Framework-dependent runtimeconfig; ARM64 .NET must be bundled')
 if not any(x.get('name')=='Microsoft.NETCore.App' and x.get('version','').startswith('10.') for x in rc.get('includedFrameworks',[])):fail.append('No bundled Microsoft.NETCore.App10 declaration')
except (OSError,ValueError) as e:fail.append('Runtimeconfig unreadable: '+str(e))
try:
 deps=json.loads((root/'Haven.deps.json').read_text())
 if 'linux-arm64' not in deps.get('runtimeTarget',{}).get('name',''):fail.append('deps runtimeTarget is not linux-arm64')
except (OSError,ValueError) as e:fail.append('Deps unreadable: '+str(e))
glibc_max=(0,);gxx_max=(0,)
for f in sorted(root.rglob('*')):
 rel=str(f.relative_to(root))
 if f.is_symlink():
  if Path(os.readlink(f)).is_absolute():fail.append('Absolute publish symlink is not relocatable: '+rel)
  try:f.resolve().relative_to(root)
  except ValueError:fail.append('Symlink escapes payload: '+rel)
  continue
 if not f.is_file():continue
 b=f.read_bytes();files.append({'path':rel,'bytes':len(b),'sha256':sha(b),'executable':bool(f.stat().st_mode&0o111)})
 if f.name.lower() in {'haven.db','home-state.json','preferences.json','auth.json','auth.db','.env','.env.local'} or f.suffix.lower() in {'.pfx','.p12','.pem','.key'}:fail.append('Personal/config/credential-shaped file requires exclusion or explicit review: '+rel)
 if b[:4]!=b'\x7fELF':continue
 if len(b)<20:fail.append('Truncated ELF '+rel);continue
 bits=b[4];endian='<' if b[5]==1 else '>';machine=struct.unpack_from(endian+'H',b,18)[0]
 # Other-RID assets can remain as inert package content; every root/selected-RID ELF must be aarch64.
 parts=Path(rel).parts;inactive='runtimes' in parts and len(parts)>parts.index('runtimes')+1 and parts[parts.index('runtimes')+1] not in {'linux-arm64','linux','unix'}
 if 'noavx' in parts and 'linux-arm64' not in parts:inactive=True
 active=not inactive
 if active and (bits!=2 or machine!=183):fail.append('Effective ELF is not64-bit AArch64: '+rel+' machine='+str(machine))
 needed=[];versions=[];gxx=[]
 if machine==183:
  d=subprocess.run(['readelf','-d',str(f)],capture_output=True,text=True)
  if d.returncode:fail.append('readelf dynamic failed: '+rel)
  needed=re.findall(r'\(NEEDED\).*?\[(.*?)\]',d.stdout)
  v=subprocess.run(['readelf','--version-info',str(f)],capture_output=True,text=True)
  versions=sorted(set(re.findall(r'\bGLIBC_([0-9.]+)',v.stdout)),key=lambda x:tuple(map(int,x.split('.'))))
  gxx=sorted(set(re.findall(r'\bGLIBCXX_([0-9.]+)',v.stdout)),key=lambda x:tuple(map(int,x.split('.'))))
  if active and versions:glibc_max=max(glibc_max,tuple(map(int,versions[-1].split('.'))))
  if active and gxx:gxx_max=max(gxx_max,tuple(map(int,gxx[-1].split('.'))))
 elfs.append({'path':rel,'elf_bits':64 if bits==2 else 32,'machine':machine,'effective_arm64_payload':active,'needed':needed,'max_glibc':versions[-1] if versions else None,'max_glibcxx':gxx[-1] if gxx else None})
if (root/'Haven').is_file() and not os.access(root/'Haven',os.X_OK):fail.append('Haven apphost is not executable')
if glibc_max>(2,34):warn.append('Actual effective ELF requires glibc '+'.'.join(map(str,glibc_max))+'; package Depends must be raised beyond2.34')
warn.extend(['Architecture/dependency inspection only: no ARM process executed','Linux native HTTP browser not supplied; WebRtcVad speech input Windows-only; local model/runtime availability separately unverified'])
result={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'publish':str(root),'status':'PASS_ARCHITECTURE_SELF_CONTAINED_SHAPE_ONLY' if not fail else 'FAIL','target':'linux-arm64','files':len(files),'bytes':sum(x['bytes'] for x in files),'tree_manifest_sha256':sha(json.dumps(files,sort_keys=True,separators=(',',':')).encode()),'runtimeTarget':deps.get('runtimeTarget',{}),'includedFrameworks':rc.get('includedFrameworks',[]),'effective_max_glibc':'.'.join(map(str,glibc_max)),'effective_max_glibcxx':'.'.join(map(str,gxx_max)),'failures':fail,'warnings':warn,'elf_inventory':elfs,'file_manifest':files,'ARM_native_execution':'NOT RUN','installation':'NOT RUN','lesson_workflow':'NOT RUN'}
print(json.dumps(result,indent=2));sys.exit(0 if not fail else 2)
