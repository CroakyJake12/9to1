#!/usr/bin/env python3
from pathlib import Path
import hashlib,json,os,subprocess,sys,tarfile,urllib.request
assert os.uname().machine=='aarch64';out=Path('engine-artifact');ev=out/'evidence';ev.mkdir(parents=True,exist_ok=True);task=Path(os.environ['RUNNER_TEMP'])/'official-b10809-arm64-baseline';task.mkdir();archive=task/'official.tar.gz';url='https://github.com/ggml-org/llama.cpp/releases/download/b10809/llama-b10809-bin-ubuntu-arm64.tar.gz';expected='f2b7333971e1b7b42e9268bfdbfa30f5f56e2897156084d2251385df94aec358'
with urllib.request.urlopen(url,timeout=90) as response,archive.open('xb') as f:
 while b:=response.read(1024*1024):f.write(b)
assert archive.stat().st_size==13380118 and hashlib.sha256(archive.read_bytes()).hexdigest()==expected
with tarfile.open(archive) as tar:tar.extractall(task/'extracted',filter='data')
servers=list((task/'extracted').rglob('llama-server'));assert len(servers)==1;server=servers[0]
(ev/'download-provenance.json').write_text(json.dumps({'official_url':url,'archive_sha256':expected,'source_commit':'5266f24da75dc449bd56cbed7addb9c8e4a6a73e','release':'b10809','version':'0.4.0','native_machine':os.uname().machine,'baseline':'Official prebuilt onUbuntu24.04 ARM, not older-laptop compatibility','laptop_acceptance':'NOT RUN'},indent=2)+'\n')
(ev/'native-platform.txt').write_text(subprocess.check_output(['uname','-a'],text=True)+Path('/etc/os-release').read_text()+subprocess.check_output(['getconf','GNU_LIBC_VERSION'],text=True))
subprocess.run([sys.executable,str(Path(__file__).with_name('smoke-engine-http.py')),str(server),str(task/'model'),str(ev)],check=True)
