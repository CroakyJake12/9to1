#!/usr/bin/env python3
"""Delegated one-slot CI SDK executor; no model/worker/programme controller.

Root's immutable reviewed instruction authorizes adoption only after this runner
observes its actual public-repository, host, source, SDK and resource identities.
All mutable execution context/evidence lives under RUNNER_TEMP. No native auth or
private coordination ledger is transferred. The native programme remains Root's.
"""
from __future__ import annotations
import argparse, contextlib, fcntl, hashlib, json, os
from pathlib import Path, PurePosixPath
import re, signal, stat, subprocess, sys, tarfile, time
import urllib.error, urllib.request

OWNER = 'native:local:01a16df8-1971-7d63-be3a-b8548a888274'
REPOSITORY = 'CroakyJake12/9to1'
BASE = '9505fcb85c640e8f444fb9bef842574d6df56a5c'
ANCHOR = '4d6a16e0ce60c8d24eb151bf0f1fdfdab4c6e522'
PRODUCT = '81c75b7e9125a6931d905bbc7410593f14112ff4'
PROJECT = 'framework/CUI/Runtime.Tests/CakeOS.Cui.Runtime.Tests.csproj'
FILTER = 'FullyQualifiedName~CakeOS.Cui.Runtime.Tests.CuiIconMenuTests|FullyQualifiedName~CakeOS.Cui.Runtime.Tests.CuiKeyboardTooltipHostTests'
WORKFLOW = '.github/workflows/haven-chat-model-ci.yml'
HARNESS = 'eng/development/ci_cui_owned.py'
INSTRUCTION = 'eng/development/ci-cui-root-delegation.json'
ALLOWED_CI_FILES = {WORKFLOW, HARNESS, INSTRUCTION}
DONORS = {'framework/CUI/vendor/Avalonia/external/XamlX':'009d4815470cf4bf71d1adbb633a5d81dcb2bb52',
          'framework/CUI/vendor/Avalonia/external/Avalonia.DBus':'864a05282841bf04006890f04d11d60d1a046aa9'}
GIB = 1024 ** 3
LIMIT = 4 * GIB
RESERVE = GIB
GC_ENV = {'DOTNET_GCHeapHardLimit':'0x80000000','DOTNET_GCConserveMemory':'5'}
CHECKOUT = Path(os.environ.get('GITHUB_WORKSPACE', Path.cwd())).resolve()
sys.path.insert(0, str(CHECKOUT/'eng/development'))
import workflow as w


def need(condition, message):
    if not condition: raise w.Refusal(message)


def pin(path): return {'path':str(Path(path).resolve()), **w.file_identity(path)}


def mem_disk(path):
    values = {}
    for line in Path('/proc/meminfo').read_text().splitlines():
        key, _, value = line.partition(':')
        if key in ('MemAvailable','SwapFree'): values[key] = int(value.strip().split()[0])*1024
    disk=os.statvfs(path); values['DiskAvailable']=disk.f_bavail*disk.f_frsize
    values['observed_at']=time.time(); return values


def require_resources(path):
    r=mem_disk(path)
    need(r.get('MemAvailable',0)>=LIMIT+RESERVE,'Actual CI host has less than4GiB SDK allocation plus1GiB reserve')
    need(r['DiskAvailable']>=10*GIB,'Actual CI host lacks8GiB normal dependency/copy budget plus2GiB disk reserve')
    return r


@contextlib.contextmanager
def lock(path):
    fd=os.open(path,os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW,0o600)
    try:
        need(stat.S_ISREG(os.fstat(fd).st_mode),'SDK lock is not regular')
        try: fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError: raise w.Refusal('Another actual CI SDK owner holds the slot') from None
        yield
    finally: os.close(fd)


def public_repository():
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self,*args,**kwargs): return None
    token=os.environ.get('GITHUB_TOKEN')
    need(isinstance(token,str) and token,'Read-only scoped GitHub token unavailable')
    req=urllib.request.Request('https://api.github.com/repos/'+REPOSITORY,
        headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','User-Agent':'9to1-owned-cui-ci'})
    try:
        with urllib.request.build_opener(NoRedirect()).open(req,timeout=20) as response: value=json.loads(response.read(1024*1024))
    except (OSError,ValueError,urllib.error.HTTPError): raise w.Refusal('Actual repository visibility observation unavailable') from None
    need(value.get('full_name')==REPOSITORY and value.get('private') is False and value.get('visibility')=='public',
        'This delegation is restricted to the actually public repository')
    return {'full_name':REPOSITORY,'private':False,'visibility':'public','observed_at':time.time()}


def clean_environment(sdk, source, temporary):
    permitted=('PATH','HOME','LANG','LC_ALL','TZ','TMPDIR','LD_LIBRARY_PATH','SSL_CERT_FILE','SSL_CERT_DIR','HTTPS_PROXY','HTTP_PROXY','NO_PROXY')
    env={key:os.environ[key] for key in permitted if key in os.environ}
    env.update(DOTNET_ROOT=str(sdk.parent),DOTNET_CLI_HOME=str(temporary/'dotnet-cli'),
        NUGET_PACKAGES=str(temporary/'nuget'),DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',
        DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='0',DOTNET_MULTILEVEL_LOOKUP='0',MSBUILDDISABLENODEREUSE='1',PWD=str(source))
    env.update(GC_ENV)
    return env


def tree(repo, revision):
    result=[]
    for raw in w.git(repo,'ls-tree','-rz',revision).decode().split('\0'):
        if not raw: continue
        metadata,name=raw.split('\t',1);mode,kind,oid=metadata.split();w.safe_relative(name)
        need(mode in ('100644','100755','160000'),'Required source contains unsupported symlink/special entry')
        result.append((name,mode,kind,oid))
    return result


def blob_hash(path):
    h=hashlib.sha1();h.update(b'blob '+str(path.stat().st_size).encode()+b'\0')
    with path.open('rb') as stream:
        while data:=stream.read(1024*1024): h.update(data)
    return h.hexdigest()


def export_source(source, manifest_path, evidence, temporary):
    source.mkdir(mode=0o700); files=[]; donors=[]; unexpanded=[]
    def materialize(repo, revision, prefix, label):
        rows=tree(repo,revision); archive=temporary/(label+'.tar')
        with archive.open('xb') as stream:
            subprocess.run(['git','-C',str(repo),'archive','--format=tar',revision],stdout=stream,check=True,timeout=120)
        destination=source/prefix;destination.mkdir(parents=True,exist_ok=True,mode=0o700)
        with tarfile.open(archive,'r:') as stream:
            members=stream.getmembers()
            for item in members:
                path=PurePosixPath(item.name)
                need(not path.is_absolute() and '..' not in path.parts and '\\' not in item.name
                    and (item.isfile() or item.isdir()),'Unsafe/nonregular immutable source archive')
            stream.extractall(destination,members=members,filter='data')
        for name,mode,kind,oid in rows:
            if kind!='blob': continue
            path=destination/name;path.parent.mkdir(parents=True,exist_ok=True,mode=0o700)
            if not path.exists() or blob_hash(path)!=oid:
                with path.open('wb') as stream:
                    subprocess.run(['git','-C',str(repo),'cat-file','blob',oid],stdout=stream,check=True,timeout=30)
            need(blob_hash(path)==oid,'Actual exported bytes differ from committed Git object')
            path.chmod(0o700 if mode=='100755' else 0o600)
            files.append({'path':prefix+name,**{k:v for k,v in w.file_identity(path).items() if k!='kind'}})
        expected={name for name,_,kind,_ in rows if kind=='blob'}
        need({p.relative_to(destination).as_posix() for p in destination.rglob('*') if p.is_file()}==expected,
             'Archive regular-file set differs from complete committed source')
        return rows
    roots=materialize(CHECKOUT,PRODUCT,'','source-product')
    for relative,commit in DONORS.items():
        entry=next(row for row in roots if row[0]==relative)
        need(entry[1]=='160000' and entry[3]==commit,'Product native gitlink differs from required pin')
        origin=CHECKOUT/relative
        need(w.git(origin,'rev-parse','HEAD').decode().strip()==commit,'Initialized native donor source differs')
        rows=materialize(origin,commit,relative+'/',relative.rsplit('/',1)[-1])
        donors.append({'path':relative,'commit':commit,'tree':w.git(origin,'rev-parse',commit+'^{tree}').decode().strip(),
            'regular_files':sum(row[2]=='blob' for row in rows),'blob_bytes_verified':True})
    for name,mode,kind,oid in roots:
        if kind=='commit' and name not in DONORS: unexpanded.append({'path':name,'commit':oid,'scope':'Outside owning CUI graph; not expanded or accepted'})
    for path in source.rglob('*'):
        need(not path.is_symlink(),'Symlink source refused')
        if path.is_dir():path.chmod(0o700)
    document={'schema':'9to1.exact-source-export/v1','root':str(source),'origin_git':str(CHECKOUT),
        'source_revision':PRODUCT,'candidate_revision':PRODUCT,'files':sorted(files,key=lambda x:x['path'])}
    w.atomic(manifest_path,document)
    summary={'source_revision':PRODUCT,'source_tree':w.git(CHECKOUT,'rev-parse',PRODUCT+'^{tree}').decode().strip(),
        'manifest':pin(manifest_path),'files':len(files),'bytes':sum(x['bytes'] for x in files),
        'required_donors':donors,'unexpanded_gitlinks':unexpanded,'private_modes':True,'hardlinks':0,'SDK_commands':0}
    w.atomic(evidence/'source-export.json',summary);return summary


def validate_source(source, manifest, revision):
    record={'path':str(source),'git':{'path':str(CHECKOUT),'revision':revision},'manifest':pin(manifest)}
    entries=w.export_files(record,revision);w.execution_files(source,entries)
    need(source.stat().st_mode&0o077==0,'Source export root not private')
    for name in entries:
        metadata=(source/name).stat()
        need(metadata.st_uid==os.getuid() and metadata.st_nlink==1 and metadata.st_mode&0o022==0,'Another actual source writer/hardlink exists')
    for directory, dirs, names in os.walk(source,followlinks=False):
        directory=Path(directory)
        for name in list(dirs):
            need(not (directory/name).is_symlink(),'Symlink source directory refused')
            if name in ('bin','obj','build-intermediate'):dirs.remove(name)
        for name in names:need((directory/name).relative_to(source).as_posix() in entries,'Undeclared non-output source input')
    return {'files_rehashed':len(entries),'scope':'All declared regular source bytes; dependency/copy closurePARTIAL_NOT_REUSABLE'}


def rss(identity):
    total=0
    for member in w.remaining_group(identity):
        try:
            before=w.process_identity(member['pid']);pages=int(Path('/proc',str(member['pid']),'statm').read_text().split()[1])
            after=w.process_identity(member['pid'])
            if before and after and before['start_ticks']==after['start_ticks']==member['start_ticks']:total+=pages
        except (OSError,ValueError,IndexError):pass
    return total*os.sysconf('SC_PAGE_SIZE')


def monitored(command, source, log, timeout, env, before_spawn, deadline):
    started=time.monotonic();peak=0;reason=None;identity=None
    with log.open('xb') as stream:
        os.fchmod(stream.fileno(),0o600)
        admission=before_spawn()
        process=subprocess.Popen(command,cwd=source,env=env,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        try:
            identity=w.process_identity(process.pid)
            need(identity and identity['pgid']==identity['session']==process.pid,'Actual owned SDK group identity missing')
            if process.poll() is None:need(Path('/proc',str(process.pid),'cwd').resolve()==source.resolve(),'Actual owned SDK cwd differs')
            w.atomic(log.with_suffix('.process.json'),{'process':identity,'command':command,'cwd':str(source),
                'sdk_environment':GC_ENV,'started_at':time.time(),'scope':'Actual owned SDK process only'})
            while process.poll() is None:
                peak=max(peak,rss(identity));current=mem_disk(source)
                if peak>LIMIT:reason='AGGREGATE_RSS_LIMIT'
                elif time.monotonic()-started>timeout or time.monotonic()>deadline:reason='TIMEOUT'
                elif current.get('MemAvailable',0)<RESERVE:reason='HOST_RESERVE'
                elif current['DiskAvailable']<GIB:reason='DISK_RESERVE'
                elif log.stat().st_size>32*1024**2:reason='BOUNDED_LOG_LIMIT'
                if reason:w.stop_owned(process,identity);break
                time.sleep(.1)
            process.wait(timeout=3)
        except BaseException:
            if identity and process.poll() is None:w.stop_owned(process,identity)
            elif process.poll() is None:process.terminate();process.wait(timeout=3)
            raise
    remaining=w.remaining_group(identity)
    for _ in range(10):
        if not remaining:break
        time.sleep(.02);remaining=w.remaining_group(identity)
    return {'command':command,'exit_code':process.returncode,'process':identity,'duration_seconds':time.monotonic()-started,
        'peak_aggregate_rss_bytes':peak,'limit_reason':reason,'remaining_group':remaining,'launch_admission':admission,
        'sdk_environment':GC_ENV,'log':pin(log),'status':'BLOCKED' if reason or remaining else 'PROCESS_SUCCEEDED' if process.returncode==0 else 'FAIL'}


def actual_test_classes(path):
    import xml.etree.ElementTree as ET
    root=ET.parse(path).getroot(); definitions={}
    for unit in root.findall('.//{*}UnitTest'):
        method=unit.find('{*}TestMethod')
        if method is not None:definitions[unit.get('id')]=method.get('className','')
    counts={key:0 for key in ('CuiIconMenuTests','CuiKeyboardTooltipHostTests')}
    for result in root.findall('.//{*}UnitTestResult'):
        if result.get('outcome')!='Passed':continue
        name=definitions.get(result.get('testId'),'')+' '+result.get('testName','')
        for key in counts:
            if key in name:counts[key]+=1
    need(counts['CuiIconMenuTests']>0 and counts['CuiKeyboardTooltipHostTests']>=4,'Actual TRX did not execute both required owning classes/new4tooltip cases')
    return counts


def main():
    evidence=Path(os.environ['RUNNER_TEMP'])/'9to1-cui-evidence';evidence.mkdir(mode=0o700,exist_ok=True)
    temporary=Path(os.environ['RUNNER_TEMP'])/'9to1-cui-context';temporary.mkdir(mode=0o700)
    receipt={'schema':'9to1.delegated-ci-sdk-result/v1','result':'NOT_RUN','source_revision':PRODUCT,
        'classification':'INFRASTRUCTURE','dependency_closure':'PARTIAL_NOT_REUSABLE','acceptance':'PAUSED','stages':[],'started_at':time.time()}
    w.atomic(evidence/'receipt.json',receipt);deadline=time.monotonic()+1200
    try:
        need(os.environ.get('GITHUB_ACTIONS')=='true' and os.environ.get('GITHUB_EVENT_NAME')=='workflow_dispatch'
             and os.environ.get('GITHUB_REPOSITORY')==REPOSITORY,'Only the actual registered manual GitHub job is delegated')
        ci_commit=w.git(CHECKOUT,'rev-parse','HEAD').decode().strip()
        need(ci_commit==os.environ.get('EXPECTED_CI_COMMIT')==os.environ.get('GITHUB_SHA'),'Actual CI commit differs from Root dispatch')
        instruction_path=CHECKOUT/INSTRUCTION;instruction=w.read(instruction_path)
        need(pin(instruction_path)['sha256']==os.environ.get('EXPECTED_DELEGATION_SHA256'),'Immutable Root delegation differs from exact dispatch pin')
        need(instruction.get('schema')=='9to1.root-live/v1' and instruction.get('issuer',{}).get('owner')==OWNER
             and instruction.get('candidate')==PRODUCT and instruction.get('delegation',{}).get('sdk_host_observed_adoption') is True,
             'Actual native Root SDK delegation is missing')
        native=instruction['native_sdk_reservation']
        need(native.get('id')==os.environ.get('EXPECTED_RESERVATION_ID') and re.fullmatch('[A-Za-z0-9._:-]{8,160}',native['id'])
             and type(native.get('adopted_generation')) is int and native['adopted_generation']>0
             and native.get('slots')==1 and native.get('status')=='RESERVED_FOR_CI'
             and native.get('source_revision')==PRODUCT and native.get('local_process_groups_terminal') is True
             and os.environ.get('GITHUB_REF')==native.get('github_ref'),'Actual native one-SDK reservation differs')
        need(instruction.get('harness_sha256')==pin(Path(__file__))['sha256']
             and instruction.get('workflow_sha256')==pin(CHECKOUT/WORKFLOW)['sha256'],'Reviewed immutable CI code differs')
        need(instruction.get('bounds')=={'max_aggregate_rss_bytes':LIMIT,'memory_headroom_bytes':RESERVE,
             'normal_copy_budget_bytes':8*GIB,'disk_reserve_bytes':2*GIB,'overall_seconds':1200,'sdk_environment':GC_ENV},'Root reviewed bounds differ')
        changed=set(w.git(CHECKOUT,'diff','--name-only',PRODUCT,ci_commit).decode().splitlines())
        need(changed.issubset(ALLOWED_CI_FILES) and {WORKFLOW,HARNESS,INSTRUCTION}.issubset(changed),'CI commit changes product source outside reviewed harness/workflow/instruction')
        for revision in (PRODUCT,BASE,ANCHOR):
            need(w.git(CHECKOUT,'cat-file','-t',revision).decode().strip()=='commit','Required source commit unavailable')
            w.git(CHECKOUT,'merge-base','--is-ancestor',revision,ci_commit)
        repo=public_repository();observed=require_resources(temporary)
        need(sys.platform=='linux' and os.uname().machine=='x86_64','Actual SDK host is not supportedLinuxx64')
        sdk=Path(os.environ['DOTNET_INSTALL_DIR']).resolve()/'dotnet'
        required_sdk={'dotnet','sdk/10.0.301/dotnet.dll','sdk/10.0.301/MSBuild.dll',
            'sdk/10.0.301/Roslyn/bincore/csc.dll','sdk/10.0.301/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets'}
        need(set(instruction.get('sdk_file_sha256',{}))==required_sdk,'Root delegation must retain all exact SDK core identities')
        actual_sdk={}
        for relative,expected in instruction['sdk_file_sha256'].items():
            w.safe_relative(relative);actual_sdk[relative]=pin(sdk.parent/relative)
            need(actual_sdk[relative]['sha256']==expected,'Actual official isolated SDK core file differs')
        source=temporary/'source';manifest=temporary/'source-manifest.json'
        summary=export_source(source,manifest,evidence,temporary)
        env=clean_environment(sdk,source,temporary)
        host={'repository':repo,'ci_commit':ci_commit,'product_source_revision':PRODUCT,'product_tree':summary['source_tree'],
            'github_run_id':os.environ['GITHUB_RUN_ID'],'github_run_attempt':os.environ.get('GITHUB_RUN_ATTEMPT'),
            'github_ref':os.environ['GITHUB_REF'],'runner_os':os.environ.get('RUNNER_OS'),'runner_arch':os.environ.get('RUNNER_ARCH'),
            'uname':list(os.uname()),'uid':os.getuid(),'resource_observation':observed,'sdk_executable':pin(sdk),
            'native_reservation':native,'instruction':pin(instruction_path),'harness':pin(Path(__file__)),
            'actual_sdk_core':actual_sdk,'source_directory_identity':{'device':source.stat().st_dev,'inode':source.stat().st_ino}}
        w.atomic(evidence/'actual-host.json',host)
        project_commands={'version':[str(sdk),'--version'],
            'restore':[str(sdk),'restore',PROJECT,'--configfile','NuGet.Config','--disable-build-servers','--disable-parallel','-p:Configuration=Release','-p:UseSharedCompilation=false','-p:BuildInParallel=false'],
            'build':[str(sdk),'build',PROJECT,'-c','Release','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:UseSharedCompilation=false'],
            'test':[str(sdk),'test',PROJECT,'-c','Release','--no-build','--no-restore','--disable-build-servers','--filter',FILTER,'--logger','trx;LogFileName=cui-owning.trx','--results-directory',str(evidence/'test-results')]}
        project_commands['regression']=[str(sdk),'test',PROJECT,'-c','Release','--no-build','--no-restore',
            '--disable-build-servers','--logger','trx;LogFileName=cui-runtime-all.trx','--results-directory',str(evidence/'test-results')]
        state_path=temporary/'sdk-execution-projection.json';execution='github-cui-'+os.environ['GITHUB_RUN_ID']
        initial=w.initial_state(CHECKOUT,str(instruction_path))
        initial['authority']='Existing native Root remains programme authority. This is its delegated compiler-only host projection; no workers/models/milestones are created.'
        w.initialize(state_path,initial)
        manifest_pin=pin(manifest);runner_pin=pin(Path(__file__))
        candidate={'revision':PRODUCT,'base_revision':PRODUCT,'parent_revision':BASE,'worktree':str(source),'tree':summary['source_tree'],'manifest':manifest_pin}
        custody={'revision':PRODUCT,'owner':OWNER,'source':{'kind':'source-export','path':str(source),'manifest':manifest_pin,
            'git':{'path':str(CHECKOUT),'common_directory':w.git(CHECKOUT,'rev-parse','--path-format=absolute','--git-common-dir').decode().strip(),
                'revision':PRODUCT,'required_revisions':[PRODUCT,BASE,ANCHOR,ci_commit]}},'build_dependencies':{
                'actual-guarded-sdk-runner':{'path':runner_pin['path'],'identity':{k:v for k,v in runner_pin.items() if k!='path'},'recovery':'restore','next_action':'Restore this exact Root-reviewed immutable CI commit; no source/runner substitution.'},
                'actual-sdk-executable':{'path':str(sdk),'identity':w.file_identity(sdk),'recovery':'restore','next_action':'Restore exact verifiedSDK10.0.301 core bytes.'}}}
        with lock(temporary/'sdk.lock'),lock(temporary/'source-writer.lock'):
            validate_source(source,manifest,PRODUCT);fresh=require_resources(source)
            proof_path=evidence/'ROOT-LIVE-delegated-SDK.json'
            proof={'schema':'9to1.root-live/v1','candidate':PRODUCT,'source_manifest':manifest_pin,'expected_next_generation':1,
                'issuer':{'owner':OWNER,'role':'Root-delegated observed CI SDK executor'},'actual_host':host,'actual_resources':fresh,
                'immutable_Root_instruction':pin(instruction_path),'native_sdk_reservation':native,'observed_at':time.time(),
                'sdk_admission':{'runner':runner_pin,'local_execution':{'slots':1,'commands':project_commands,'bounds':instruction['bounds']}},
                'scope':'Actual source/host/SDK observations adopted under existing native Root delegation. No Cloud or human ACK invented; no product/acceptance claim.'}
            w.atomic(proof_path,proof);proof_pin=pin(proof_path)
            with w.transaction(state_path,0) as state:
                w.apply_checkpoint(state,{'development_candidates':{execution:candidate},'execution_custody':{execution:custody},
                    'recovery_assets':{'latest-root-checkpoint':proof_pin},'last_root_checkpoint_observed':proof_pin})
            receipt.update(actual_host=host,source_manifest=manifest_pin,SDK_adoption=proof_pin,SDK_environment=GC_ENV)
            for stage,timeout in [('version',30),('restore',300),('build',900),('test',180),('regression',300)]:
                need({'device':source.stat().st_dev,'inode':source.stat().st_ino}==host['source_directory_identity'],
                    'Actual frozen SDK source directory identity changed')
                validate_source(source,manifest,PRODUCT);require_resources(source)
                def admission():
                    require_resources(source)
                    return w.execution_admission(state_path,execution,source,PRODUCT,manifest,manifest_pin['sha256'],Path(__file__).resolve())
                item=monitored(project_commands[stage],source,evidence/(stage+'.log'),timeout,env,admission,deadline)
                item['stage']=stage;receipt['stages'].append(item);w.atomic(evidence/'receipt.json',receipt)
                item['source_after']=validate_source(source,manifest,PRODUCT)
                if stage=='version':need((evidence/'version.log').read_text().strip()=='10.0.301','Actual owning source selected a different SDK')
                if stage in ('test','regression') and item['status']=='PROCESS_SUCCEEDED':
                    trx=evidence/'test-results'/('cui-owning.trx' if stage=='test' else 'cui-runtime-all.trx')
                    item['tests']=w.trx(trx);item['test_artifact']=pin(trx)
                    if stage=='test':item['test_classes']=actual_test_classes(trx)
                    else:item['scope']='Full same owning Runtime.Tests project, without a test filter; no acceptance claim'
                    item['status']=item['tests']['result']
                w.atomic(evidence/'receipt.json',receipt)
                need(item['status'] in ('PROCESS_SUCCEEDED','PASS'),'Actual owning '+stage+' failed; raw diagnostic retained')
            receipt.update(result='PASS_PARTIAL_NOT_REUSABLE',classification='OWNING_CUI_DEVELOPMENT_CHECK')
    except Exception as error:
        receipt.update(result='BLOCKED',reason=str(error),classification='INFRASTRUCTURE_OR_ACTUAL_CHECK_FAILURE')
    finally:
        receipt['ended_at']=time.time();receipt['retained_logs']=[pin(p) for p in evidence.glob('*.log')]
        w.atomic(evidence/'receipt.json',receipt)
    print(json.dumps({'result':receipt['result'],'source_revision':PRODUCT,'receipt':str(evidence/'receipt.json'),'acceptance':False}))
    return 0 if receipt['result']=='PASS_PARTIAL_NOT_REUSABLE' else 2


if __name__=='__main__':
    def interrupted(signum,frame):raise w.Refusal('Owned CI SDK executor interrupted; only still-proven owned group is stopped')
    signal.signal(signal.SIGTERM,interrupted);signal.signal(signal.SIGINT,interrupted)
    raise SystemExit(main())
