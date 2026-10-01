"""Actual APK manifest evidence, no source-condition inference."""
import hashlib,json,os,pathlib,subprocess,sys,xml.etree.ElementTree as ET
mode=sys.argv[1];root=pathlib.Path.cwd();out=root/'artifacts/logs';out.mkdir(parents=True,exist_ok=True)
def sha(p):
 with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
if mode=='normal':
 candidates=list((root/'9-1 OS (Android)/src/Haven.Android/bin/Release').rglob('*-Signed.apk'))
 if not candidates:candidates=list((root/'9-1 OS (Android)/src/Haven.Android/bin/Release').rglob('*.apk'))
 if not candidates or len({sha(p) for p in candidates})!=1:raise SystemExit('missing or ambiguous normal-build APK')
 apk=candidates[0]
elif mode=='validation':apk=root/'artifacts/apk/android29-frozen91-Signed.apk'
else:raise SystemExit('unknown manifest mode')
analyzer=pathlib.Path(os.environ['ANDROID_HOME'])/'cmdline-tools/latest/bin/apkanalyzer'
if not analyzer.is_file():raise SystemExit('actual hosted apkanalyzer missing')
result=subprocess.run([str(analyzer),'manifest','print',str(apk)],text=True,capture_output=True)
(out/(mode+'-apk-manifest.xml')).write_text(result.stdout);(out/(mode+'-manifest-stderr.txt')).write_text(result.stderr);result.check_returncode()
manifest=ET.fromstring(result.stdout);ns='{http://schemas.android.com/apk/res/android}'
if manifest.attrib.get('package')!='com.cakemods.haven':raise SystemExit('manifest package mismatch')
component='com.cakemods.haven.AstraCredentialProbeActivity';activities=manifest.findall('./application/activity');probe=[a for a in activities if a.attrib.get(ns+'name')==component]
if mode=='normal' and probe:raise SystemExit('normal APK contains validation probe')
if mode=='validation' and (len(probe)!=1 or probe[0].attrib.get(ns+'exported')!='true'):raise SystemExit('validation APK missing unique exported probe')
(out/(mode+'-manifest-receipt.json')).write_text(json.dumps({'mode':mode,'apk':str(apk.relative_to(root)),'apkSha256':sha(apk),'bytes':apk.stat().st_size,'probeCount':len(probe),'probeExported':probe[0].attrib.get(ns+'exported') if probe else None,'manifestSha256':sha(out/(mode+'-apk-manifest.xml')),'commit':os.environ['GITHUB_SHA'],'runId':os.environ['GITHUB_RUN_ID'],'qualification':'Actual APK manifest only; no runtime read proof'},indent=2)+'\n')
