import hashlib,json,os,re,subprocess,sys,zipfile,shutil
from pathlib import Path

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def run(*a):return subprocess.check_output(a,text=True).strip()
def write(p,d):Path(p).parent.mkdir(parents=True,exist_ok=True);Path(p).write_text(json.dumps(d,indent=2)+'\n')
manifest=Path('.github/validation/astra-android29-frozen91.json')
m=json.loads(manifest.read_text())
commit=os.environ['GITHUB_SHA']; expected=os.environ['EXPECTED_COMMIT']; mh=os.environ['EXPECTED_MANIFEST_SHA256']
assert re.fullmatch('[0-9a-f]{40}',expected) and commit==expected==run('git','rev-parse','HEAD')
assert re.fullmatch('[0-9a-f]{64}',mh) and sha(manifest)==mh
assert os.environ['GITHUB_REF'].startswith('refs/heads/validation/astra-android29-frozen91-')
mode=sys.argv[1]
if mode=='source':
 for f in m['files']:
  assert sha(f['path'])==f['sha256'],f['path']
  assert run('git','ls-files','-s','--',f['path']).startswith('100644 '),f['path']
 for pin in m['gitlinks']:
  assert run('git','ls-tree','HEAD','--',pin['path'])==pin['tree']
 # Entire tracked source cut equals baseline except the exact overlay and reviewed validation tooling.
 changed=run('git','diff','--name-only',m['baselineCommit'],'HEAD').splitlines()
 assert set(changed)<=set(m['allowedChanges']),changed
 write('artifacts/logs/source-receipt.json',dict(commit=commit,manifestSha256=mh,runId=os.environ['GITHUB_RUN_ID'],changed=changed,baseline=m['baselineCommit']))
elif mode in ('apk','download'):
 root=Path('artifacts/apk')
 if mode=='apk':
  output=Path(os.environ['ASTRA_ANDROID_APK_OUTPUT_ROOT'])
  assert output==Path('9-1 OS (Android)/src/Haven.Android/bin/Release'),'Unexpected explicit probe output root'
  candidates=list(output.rglob('*-Signed.apk'))
  assert candidates,'No signed Release APK'
  hashes={sha(p) for p in candidates};assert len(hashes)==1,'Ambiguous distinct signed APKs'
  root.mkdir(parents=True,exist_ok=True);shutil.copyfile(candidates[0],root/'android29-frozen91-Signed.apk')
 apk=root/'android29-frozen91-Signed.apk'
 assert len(list(root.glob('*.apk')))==1
 with zipfile.ZipFile(apk) as z:
  names=z.namelist();assert len(names)==len(set(names));assert z.testzip() is None
  assert all(not n.startswith('/') and '..' not in Path(n).parts and '\\' not in n for n in names)
  abis=sorted({n.split('/')[1] for n in names if n.startswith('lib/') and n.endswith('.so')})
  assert abis==['arm64-v8a','x86_64'],abis
 tools=sorted((Path(os.environ['ANDROID_HOME'])/'build-tools').glob('*'),key=lambda p:tuple(int(v) if v.isdigit() else 0 for v in re.split('[.-]',p.name)))
 tool=next(p for p in reversed(tools) if (p/'aapt').exists() and (p/'apksigner').exists())
 badging=run(str(tool/'aapt'),'dump','badging',str(apk))
 package=re.search(r"package: name='([^']+)' versionCode='([^']+)' versionName='([^']+)'",badging)
 assert package and package.groups()==('com.cakemods.haven','20001','0.2.1-mobile-preview'),badging[:500]
 evidence=Path('artifacts/logs' if mode=='apk' else 'artifacts/smoke');evidence.mkdir(parents=True,exist_ok=True)
 (evidence/'apk-badging.txt').write_text(badging+'\n')
 verification=subprocess.run([str(tool/'apksigner'),'verify','--verbose','--print-certs',str(apk)],text=True,capture_output=True)
 (evidence/'apksigner-stdout.txt').write_text(verification.stdout)
 (evidence/'apksigner-stderr.txt').write_text(verification.stderr)
 verification.check_returncode()
 cert=verification.stdout
 # Retain actual output before parsing. Accept one exact digest from anchored certificate fields;
 # signer display prefixes vary by Android build-tools, and never establish a digest by themselves.
 digests={value.lower() for value in re.findall(r'(?m)^.*\bcertificate SHA-256 digest: +([0-9a-fA-F]{64})\s*$',cert)}
 assert len(digests)==1,'Missing or ambiguous exact certificate SHA-256 digest; inspect retained apksigner output'
 digest=next(iter(digests))
 receipt=dict(commit=commit,manifestSha256=mh,runId=os.environ['GITHUB_RUN_ID'],apkSha256=sha(apk),apkBytes=apk.stat().st_size,abis=abis,package=package.group(1),versionCode=20001,versionName=package.group(3),certificateSha256=digest,signing='ephemeral-debug-only',buildTools=tool.name)
 if mode=='apk':
  assert digest==sha(Path(os.environ['RUNNER_TEMP'])/'android29-cert.der')
  write(root/'build-receipt.json',receipt);(root/'certificate.txt').write_text(cert+'\n');(root/'badging.txt').write_text(badging+'\n')
 else:
  original=json.loads((root/'build-receipt.json').read_text())
  for k in ['commit','manifestSha256','runId','apkSha256','apkBytes','abis','package','versionCode','versionName','certificateSha256','signing']:assert receipt[k]==original[k],k
  write('artifacts/smoke/download-verification.json',receipt)
else:raise SystemExit('Unknown validation mode')
