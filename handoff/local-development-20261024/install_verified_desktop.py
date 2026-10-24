#!/usr/bin/env python3
"""Root-only downloaded CI artifact verifier/launcher registration. No app/SDK/network calls."""
import argparse,hashlib,json,os,re,shutil,sys,tarfile,time,uuid
from pathlib import Path,PurePosixPath
BASE=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(BASE/'work/local-team-runtime/frozen-launcher-d3d11eef'))
import workflow as owning_workflow

def need(ok,message):
    if not ok:raise ValueError(message)
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        while chunk:=f.read(1024*1024):h.update(chunk)
    return h.hexdigest()
def read(path):
    need(path.is_file() and not path.is_symlink() and path.stat().st_size<=8*1024**2,'Expected bounded regular JSON evidence')
    return json.loads(path.read_text())
def verify_tree(target,rows,manifest_sha):
    m=target/'Haven-linux-x64.manifest.json';need(not target.is_symlink() and not m.is_symlink() and sha(m)==manifest_sha,'Manifest changed')
    actual=set()
    for p in (target/'app').rglob('*'):
        need(not p.is_symlink(),'Package contains a symlink')
        if p.is_dir():continue
        name=p.relative_to(target).as_posix();actual.add(name);f=rows.get(name)
        need(p.is_file() and f is not None and p.stat().st_size==f['bytes'] and sha(p)==f['sha256'],'Published file differs: '+name)
    need(actual==set(rows),'Published file set differs')
def install(a):
    need(re.fullmatch('[0-9a-f]{40}',a.source_revision) and re.fullmatch('[0-9a-f]{40}',a.ci_commit) and a.run_id>0,'Exact source/CI revisions and positive run ID required')
    r=read(a.receipt);server=read(a.server_observation);h=r['actual_host'];ci=h['ci_commit'];need(ci==a.ci_commit,'CI commit differs from Root expected candidate')
    need(r.get('result')=='PASS_PARTIAL_NOT_REUSABLE' and r.get('source_revision')==a.source_revision and r.get('owning_check_scope')=='cui-notes-desktop' and r.get('ended_at'),'Actual complete Desktop CI receipt required')
    need(h.get('github_run_id')==str(a.run_id) and server.get('databaseId')==a.run_id and server.get('headSha')==ci and server.get('status')=='completed' and server.get('conclusion')=='success','Exact successful server run required')
    stages={x['stage']:x for x in r['stages']};need(len(stages)==len(r['stages']),'Duplicate CI stage evidence')
    for name in ('version','restore','build','test','regression','notes-restore','notes-build','notes-test','desktop-restore','desktop-build','strata-infrastructure-test','strata-desktop-test','desktop-publish'):
        stage=stages.get(name,{})
        expected='PASS' if name in ('test','regression','notes-test','strata-infrastructure-test','strata-desktop-test') else 'PROCESS_SUCCEEDED'
        need(stage.get('status')==expected and stage.get('exit_code')==0 and not stage.get('remaining_group'),'Owning stage not passed/closed: '+name)
        if name in ('strata-infrastructure-test','strata-desktop-test'):
            test_pin=stage.get('test_artifact',{});test_file=a.receipt.parent/Path(test_pin.get('path','missing')).name
            need(test_file.is_file() and not test_file.is_symlink() and test_file.stat().st_size==test_pin.get('bytes') and sha(test_file)==test_pin.get('sha256'),'Actual pinned Strata TRX missing or changed: '+name)
            parsed=owning_workflow.trx(test_file)
            need(parsed.get('result')=='PASS' and parsed==stage.get('tests'),'Actual Strata TRX/counter proof differs: '+name)
    package=r['desktop_package'];ap=package['archive'];mp=package['manifest']
    need(a.archive.is_file() and not a.archive.is_symlink() and a.archive.stat().st_size==ap['bytes'] and sha(a.archive)==ap['sha256'],'Archive hash/size differs from CI')
    store=BASE/'work/desktop-artifacts';store.mkdir(parents=True,exist_ok=True,mode=0o700);need(not store.is_symlink(),'Artifact store is a symlink')
    target=store/(a.source_revision[:12]+'-'+str(a.run_id));temp=store/('.extracting-'+uuid.uuid4().hex)
    with tarfile.open(a.archive,'r:gz') as archive:
        members=archive.getmembers();by_name={m.name:m for m in members}
        need(len(by_name)==len(members) and all(m.isfile() for m in members),'Archive duplicate/special/link member')
        mm=by_name.get('Haven-linux-x64.manifest.json');need(mm is not None and mm.size<=8*1024**2,'Manifest missing/unbounded')
        raw=archive.extractfile(mm).read();need(len(raw)==mp['bytes'] and hashlib.sha256(raw).hexdigest()==mp['sha256'],'Manifest differs from CI')
        manifest=json.loads(raw)
        need(manifest.get('schema')=='9to1.linux-desktop-package/v1' and manifest.get('source_revision')==a.source_revision and manifest.get('ci_commit')==ci and manifest.get('rid')=='linux-x64' and manifest.get('target_framework')=='net10.0' and manifest.get('self_contained') is False and manifest.get('entrypoint')=='app/Haven.dll' and manifest.get('publish_result')=='PROCESS_SUCCEEDED','Manifest candidate/entry differs')
        rows={};total=0
        for f in manifest['files']:
            name=f['path'];p=PurePosixPath(name)
            need(not p.is_absolute() and len(p.parts)>1 and p.parts[0]=='app' and '..' not in p.parts and str(p)==name and '\\' not in name and name not in rows,'Unsafe/duplicate path')
            need(type(f['bytes']) is int and f['bytes']>=0 and re.fullmatch('[0-9a-f]{64}',f['sha256']) and f['mode'] in (0o644,0o755),'Malformed file identity')
            rows[name]=f;total+=f['bytes'];need(total<=4*1024**3,'Package exceeds4GiB allowance')
        need(all('app/'+n in rows for n in ('Haven.dll','Haven.deps.json','Haven.runtimeconfig.json')),'Required Desktop outputs missing')
        need(set(by_name)=={'Haven-linux-x64.manifest.json',*rows},'Undeclared archive output')
        for name,f in rows.items():need(by_name[name].size==f['bytes'],'Archive member size differs')
        if not target.exists():
            temp.mkdir(mode=0o700);(temp/'Haven-linux-x64.manifest.json').write_bytes(raw)
            for name,f in rows.items():
                p=temp/name;p.parent.mkdir(parents=True,exist_ok=True,mode=0o700)
                with archive.extractfile(by_name[name]) as src,p.open('xb') as dest:shutil.copyfileobj(src,dest,1024*1024)
                p.chmod(f['mode'])
            verify_tree(temp,rows,mp['sha256']);need(not target.exists(),'Target appeared during extraction');os.rename(temp,target)
        verify_tree(target,rows,mp['sha256'])
    runtime=BASE/'work/dotnet-sdk-10.0.301/dotnet';need(runtime.is_file() and not runtime.is_symlink(),'Existing isolated .NET host missing')
    runtime_sha=sha(runtime);need(runtime_sha==h['actual_sdk_core']['dotnet']['sha256'],'Local .NET host differs from CI SDK')
    config=BASE/'outputs/9to1-local-launch.json';config.parent.mkdir(parents=True,exist_ok=True)
    need(not config.is_symlink(),'Launcher registration is a symlink')
    if config.exists():
        previous=config.read_bytes();history=store/'registration-history';history.mkdir(mode=0o700,exist_ok=True);saved=history/(hashlib.sha256(previous).hexdigest()+'.json')
        if not saved.exists():saved.write_bytes(previous);saved.chmod(0o600)
    value={'schema':'9to1.local-desktop-launch/v1','source_revision':a.source_revision,'artifact_directory':str(target),'manifest_sha256':mp['sha256'],'dotnet_sha256':runtime_sha,'github_run_id':a.run_id,'ci_commit':ci,'verified_at':time.time(),'native_smoke':'NOT_RUN'}
    pending=config.with_name(config.name+'.tmp-'+uuid.uuid4().hex)
    with pending.open('x') as stream:
        os.chmod(pending,0o600);json.dump(value,stream,indent=2);stream.write('\n');stream.flush();os.fsync(stream.fileno())
    os.replace(pending,config)
    return {'status':'REGISTERED_VERIFIED_BUILD','source_revision':a.source_revision,'artifact':str(target),'files':len(rows),'native_smoke':'NOT_RUN','app_invocations':0,'profile_changes':0}
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-revision',required=True);p.add_argument('--ci-commit',required=True);p.add_argument('--run-id',required=True,type=int)
    p.add_argument('--receipt',required=True,type=Path);p.add_argument('--server-observation',required=True,type=Path);p.add_argument('--archive',required=True,type=Path)
    print(json.dumps(install(p.parse_args()),indent=2))
if __name__=='__main__':main()
