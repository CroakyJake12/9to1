#!/usr/bin/env python3
from pathlib import Path
import datetime,hashlib,json,os,re,struct,subprocess,sys
binary=Path(sys.argv[1]);out=Path(sys.argv[2]);b=binary.read_bytes();assert os.uname().machine=='aarch64';assert b[:4]==b'\x7fELF' and b[4]==2 and b[5]==1 and struct.unpack_from('<H',b,18)[0]==183
needed_text=subprocess.check_output(['readelf','-d',str(binary)],text=True);needed=re.findall(r'\(NEEDED\).*?\[(.*?)\]',needed_text)
# Preserve native loader/libc rather than bundling or replacing laptop system libraries.
allowed={'libc.so.6','libm.so.6','libpthread.so.0','libdl.so.2','librt.so.1','ld-linux-aarch64.so.1','libatomic.so.1'}
unknown=set(needed)-allowed
if unknown:raise SystemExit('Unexpected non-system/static closure dependency: '+','.join(sorted(unknown)))
v=subprocess.check_output(['readelf','--version-info',str(binary)],text=True);versions=sorted(set(re.findall(r'\bGLIBC_([0-9.]+)',v)),key=lambda x:tuple(map(int,x.split('.'))));cxx=sorted(set(re.findall(r'\bGLIBCXX_([0-9.]+)',v)))
limit=(2,31) if os.environ['PREVIEW_DEBIAN_BASE']=='bullseye' else (2,36)
if versions and tuple(map(int,versions[-1].split('.')))>limit:raise SystemExit('Actual ABI exceeds chosen older Debian baseline')
ldd=subprocess.run(['ldd',str(binary)],text=True,capture_output=True)
if ldd.returncode or 'not found' in ldd.stdout+ldd.stderr:raise SystemExit('Native ARM runtime closure unresolved')
result={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'native_machine':os.uname().machine,'binary':str(binary),'sha256':hashlib.sha256(b).hexdigest(),'elf_machine':183,'needed_system_libraries':needed,'all_non_system_engine_libraries_statically_linked':True,'glibc_versions':versions,'max_glibc':versions[-1] if versions else None,'glibcxx_versions':cxx,'ldd':ldd.stdout,'base_glibc':subprocess.check_output(['getconf','GNU_LIBC_VERSION'],text=True).strip(),'no_owner_OS_changes':True,'status':'PASS_NATIVE_ARCHITECTURE_AND_CLOSURE_ONLY'};out.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
