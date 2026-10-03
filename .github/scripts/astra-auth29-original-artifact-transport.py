"""Transport existing immutable public artifact; never run auth/browser/native code.
Authorization is confined to exact GitHub API routes. Signed download redirects
receive no Authorization. All output parts follow complete original verification.
"""
import argparse,datetime,hashlib,json,os,pathlib,stat,subprocess,sys,urllib.error,urllib.parse,urllib.request,zipfile
sys.dont_write_bytecode=True
REPO='CroakyJake12/9to1'
ARTIFACT=11263615256
ORIGINAL_RUN=37092034354
ORIGINAL_HEAD='89c63b7a5218629cef0bb129bd90542beaaf3a3e'
OUTER_BYTES=119810250
OUTER_SHA='e8463f01f7d1b571fed46b98161ad34ed610c0a5b0b72ea3b07e05f29636c56d'
EXPECTED_SHA='1cafa87c9e07fd6f9ba15ec7a496ef53f61c8fb707b0abad03c8c9730c0d9858'
CHUNK=1048576
CAP=120000000
PART_SIZE=26*1024*1024
CUT='.github/validation/astra-auth29-original-artifact-transport-cut.json'
EXPECTED='.github/validation/auth29-original-transport/expected-archive-admission.json'
H=lambda b:hashlib.sha256(b).hexdigest()

def require(condition,reason):
    if not condition:raise ValueError(reason)

def secure_https(url):
    p=urllib.parse.urlsplit(url)
    require(p.scheme=='https' and bool(p.hostname) and p.username is None and p.password is None and not p.fragment,'HTTPS response URL policy refused')
    return url

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None

class HttpsOnlyRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,request,fp,code,msg,headers,newurl):
        secure_https(newurl)
        require(not request.has_header('Authorization'),'Credentialed redirect refused')
        result=super().redirect_request(request,fp,code,msg,headers,newurl)
        if result is not None:require(not result.has_header('Authorization'),'Authorization escaped API boundary')
        return result

def bounded_json(response,cap):
    require(response.status==200,'API status refused')
    data=response.read(cap+1);require(len(data)<=cap,'API size refused');return json.loads(data)

def api_request(token,suffix):
    # No caller-provided hostname or path; authorization is never redirected.
    require(suffix in (f'/actions/artifacts/{ARTIFACT}',f'/actions/runs/{ORIGINAL_RUN}',f'/actions/artifacts/{ARTIFACT}/zip'),'API route refused')
    return urllib.request.Request(f'https://api.github.com/repos/{REPO}{suffix}',headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'astra-auth29-original-artifact-transport'})

def obtain_api_provenance(token):
    opener=urllib.request.build_opener(NoRedirect())
    with opener.open(api_request(token,f'/actions/artifacts/{ARTIFACT}'),timeout=45) as response:api=bounded_json(response,1048576)
    with opener.open(api_request(token,f'/actions/runs/{ORIGINAL_RUN}'),timeout=45) as response:run=bounded_json(response,1048576)
    require(api['id']==ARTIFACT and api['size_in_bytes']==OUTER_BYTES and api['digest']=='sha256:'+OUTER_SHA and api['expired'] is False,'Original artifact API identity refused')
    require(api['created_at']=='2026-10-03T03:06:23Z' and api['expires_at']=='2026-10-17T03:06:20Z','Original API retention provenance changed')
    require(api['name']=='astra-cake-auth-official-headless-archive-'+ORIGINAL_HEAD,'Original artifact name refused')
    require(api['workflow_run']['id']==ORIGINAL_RUN and api['workflow_run']['head_sha']==ORIGINAL_HEAD,'Original artifact run binding refused')
    require(run['id']==ORIGINAL_RUN and run['head_sha']==ORIGINAL_HEAD and run['repository']['full_name']==REPO and run['status']=='completed','Original completed run refused')
    expiry=datetime.datetime.fromisoformat(api['expires_at'].replace('Z','+00:00'));require(expiry>datetime.datetime.now(datetime.timezone.utc),'Original artifact expired')
    # Exact observed timestamps retained, including API's three-second rounding.
    return {k:api[k] for k in ('id','name','size_in_bytes','digest','expired','created_at','updated_at','expires_at','workflow_run')}, {'id':run['id'],'head_sha':run['head_sha'],'status':run['status'],'conclusion':run['conclusion'],'repository':run['repository']['full_name']}

def download_original(token,destination):
    opener=urllib.request.build_opener(NoRedirect())
    try:
        with opener.open(api_request(token,f'/actions/artifacts/{ARTIFACT}/zip'),timeout=45):raise ValueError('Expected standard artifact redirect absent')
    except urllib.error.HTTPError as response:
        try:
            require(response.code==302,'Artifact API redirect refused');location=response.headers.get('Location');require(isinstance(location,str),'Artifact redirect missing');secure_https(location)
        finally:response.close()
    # New opener/request: no API headers, token or authorization forwarded.
    public=urllib.request.build_opener(HttpsOnlyRedirect())
    request=urllib.request.Request(location,headers={'User-Agent':'astra-auth29-original-artifact-transport'})
    require(not request.has_header('Authorization'),'Public download authorization refused')
    sha=hashlib.sha256();total=0
    with public.open(request,timeout=60) as response, destination.open('xb') as output:
        secure_https(response.geturl());require(response.status==200,'Original download status refused')
        if response.headers.get('Content-Length') is not None:require(int(response.headers['Content-Length'])==OUTER_BYTES,'Original content length refused')
        while True:
            block=response.read(CHUNK)
            if not block:break
            total+=len(block);require(total<=CAP and total<=OUTER_BYTES,'Original download byte cap refused');sha.update(block);output.write(block)
        output.flush();os.fsync(output.fileno())
    require(total==OUTER_BYTES and sha.hexdigest()==OUTER_SHA,'Original complete ZIP byte identity refused')
    # location/redirect URLs stay process-local; only immutable API provenance persists.
    return {'bytes':total,'sha256':sha.hexdigest(),'apiAuthorizationForwarded':False,'signedDownloadUrlRetained':False}

def stream_hash(stream,expected_size):
    total=0;a=hashlib.sha256();b=hashlib.sha512()
    while True:
        block=stream.read(CHUNK)
        if not block:break
        total+=len(block);require(total<=expected_size,'Archive member declared size exceeded');a.update(block);b.update(block)
    require(total==expected_size,'Archive member truncated')
    return {'bytes':total,'sha256':a.hexdigest(),'sha512':b.hexdigest()}

def verify_archive(path,expected):
    outerrows=[];members=[];files={};directories={}
    with zipfile.ZipFile(path) as outer:
        infos=outer.infolist();require(len(infos)==2 and {x.filename for x in infos}=={'archive-receipt.json','chrome-headless-shell-linux64.zip'},'Outer complete member set refused')
        for info in infos:
            require(not info.is_dir() and stat.S_IFMT(info.external_attr>>16) in (0,stat.S_IFREG) and not info.flag_bits&1,'Outer node type refused')
            require(info.file_size<=CAP,'Outer member cap refused')
            with outer.open(info) as stream:observed=stream_hash(stream,info.file_size)
            outerrows.append(dict(observed,path=info.filename,crc32=format(info.CRC,'08x'),mode=stat.S_IMODE(info.external_attr>>16),compressedBytes=info.compress_size))
        retained=json.loads(outer.read('archive-receipt.json'));require(retained==expected['archive'],'Original archive receipt differs from independent combined artifact')
        row=next(x for x in outerrows if x['path']=='chrome-headless-shell-linux64.zip');require(all(row[k]==expected['archive'][k] for k in ('bytes','sha256','sha512')),'Original inner archive identity refused')
        secure_https(retained['url']);secure_https(retained['actualFinalUrl'])
        with outer.open('chrome-headless-shell-linux64.zip') as original:
            require(original.seekable(),'Original inner archive seekability refused')
            with zipfile.ZipFile(original) as archive:
                infos=archive.infolist();require(len(infos)==len({x.filename for x in infos})==287,'Inner member set refused')
                expected_rows={x['path']:x for x in expected['archiveMembers']}
                require(set(expected_rows)=={x.filename for x in infos},'Original complete member names refused')
                for info in infos:
                    rel=pathlib.PurePosixPath(info.filename.rstrip('/'));mode=stat.S_IMODE(info.external_attr>>16);kind=stat.S_IFMT(info.external_attr>>16)
                    require(not rel.is_absolute() and '..' not in rel.parts and '.' not in rel.parts and '\\' not in info.filename and rel.parts[0]=='chrome-headless-shell-linux64' and info.filename==rel.as_posix()+('/' if info.is_dir() else ''),'Archive canonical path refused')
                    require(kind==(stat.S_IFDIR if info.is_dir() else stat.S_IFREG) and not mode&0o7000 and not info.flag_bits&1 and info.create_system==3,'Archive link/unknown node/mode refused')
                    require(info.file_size==expected_rows[info.filename]['bytes'],'Original member size refused')
                    with archive.open(info) as stream:observed=stream_hash(stream,info.file_size)
                    row={'path':info.filename,'type':'directory' if info.is_dir() else 'file','mode':mode,'bytes':observed['bytes'],'compressedBytes':info.compress_size,'crc32':format(info.CRC,'08x'),'sha256':None if info.is_dir() else observed['sha256']}
                    require(row==expected_rows[info.filename],'Original member CRC/hash/type/mode refused');members.append(row)
                    if info.is_dir():
                        require(observed['bytes']==0,'Directory payload refused')
                        if rel.as_posix() in directories:require(directories[rel.as_posix()]['mode']==mode,'Implicit/explicit directory mode refused')
                        directories[rel.as_posix()]={'path':rel.as_posix(),'type':'directory','mode':mode}
                    else:files[rel.as_posix()]={'path':rel.as_posix(),'type':'file','mode':mode,'bytes':observed['bytes'],'sha256':observed['sha256']}
                    for parent in rel.parents:
                        if parent.as_posix()!='.':directories.setdefault(parent.as_posix(),{'path':parent.as_posix(),'type':'directory','mode':0o755})
    require(not set(files)&set(directories) and sum(x['bytes'] for x in files.values())<=2147483648,'File/directory collision or full payload cap refused')
    mapping=sorted([*directories.values(),*files.values()],key=lambda x:x['path'])
    require(members==expected['archiveMembers'] and mapping==expected['completeInstalledMap'] and len(mapping)==290,'Original complete member/directory map refused')
    executable=files['chrome-headless-shell-linux64/chrome-headless-shell'];require(executable['sha256']==expected['executableSha256'] and executable['mode']==expected['executableIdentity']['mode'],'Original executable hash/mode refused')
    return {'outerMembers':outerrows,'archive':retained,'archiveMemberCount':len(members),'installedMapCount':len(mapping),'archiveMemberMapSha256':H((json.dumps(members,sort_keys=True,separators=(',',':'))+'\n').encode()),'installedMapSha256':H((json.dumps(mapping,sort_keys=True,separators=(',',':'))+'\n').encode()),'executable':executable,'allCrcPassed':True,'qualification':'Complete original ZIP/member hash/type/mode and installed map equality. Original runtime dev/inode evidence remains in the separately pinned admission record; no extraction or executable invocation.'}

def verify_cut(repo,commit,cut_sha):
    git=lambda *args:subprocess.check_output(['git','-C',str(repo),*args],env={**os.environ,'GIT_OPTIONAL_LOCKS':'0'},stderr=subprocess.PIPE).decode().strip()
    require(git('rev-parse','HEAD')==commit,'Caller exact commit refused')
    path=repo/CUT;require(stat.S_ISREG(path.lstat().st_mode) and not path.is_symlink(),'Caller cut node refused');data=path.read_bytes();require(H(data)==cut_sha,'Caller complete cut SHA refused');cut=json.loads(data)
    require(cut['currentNormalCommit']=='0f4bffb4847ab9d4dcf60597ab008fcbe85538d5' and cut['baselineCommit']==cut['currentNormalCommit'] and cut['kind']=='validation','Transport-only canonical source base refused')
    expected_paths=set()
    for row in cut['files']:
        rel=pathlib.PurePosixPath(row['path']);require(row['path']==rel.as_posix() and not rel.is_absolute() and '..' not in rel.parts,'Cut path refused');expected_paths.add(row['path']);p=repo/row['path']
        require(stat.S_ISREG(p.lstat().st_mode) and not p.is_symlink(),'Cut file node refused')
        data=p.read_bytes();require(len(data)==row['bytes'] and H(data)==row['sha256'],'Full caller source bytes refused')
    actual_tracked=set(git('ls-files','-z').split('\0'));actual_tracked.discard('')
    expectedLinks={x['path']:x['commit'] for x in cut['gitlinks']}
    require(actual_tracked==expected_paths|set(expectedLinks)|{CUT},'Full tracked caller set refused')
    actualLinks={}
    for record in git('ls-tree','-r','-z','HEAD').split('\0'):
        if record:
            meta,path=record.split('\t',1);mode,kind,oid=meta.split()
            if mode=='160000':actualLinks[path]=oid
    require(actualLinks==expectedLinks,'Canonical gitlinks changed')
    require(git('diff','--name-only')=='' and git('diff','--cached','--name-only')=='','Original Git index/worktree changed')
    return {'commit':commit,'cutSha256':cut_sha,'trackedRegularFiles':len(expected_paths),'gitlinks':cut['gitlinks'],'noGitWrites':True}

def split_verified(path,output):
    directory=output/'parts';directory.mkdir(mode=0o700);parts=[];offset=0;replay=hashlib.sha256()
    with path.open('rb') as source:
        number=0
        while True:
            block=source.read(PART_SIZE)
            if not block:break
            require(len(block)<=PART_SIZE,'Part size cap refused');number+=1;name=f'part-{number:02d}.bin';dest=directory/name
            with dest.open('xb') as target:target.write(block);target.flush();os.fsync(target.fileno())
            # Full byte replay of each source-bound emitted part before upload.
            observed=dest.read_bytes();require(observed==block,'Emitted part differs from original');replay.update(observed)
            parts.append({'part':number,'path':name,'offset':offset,'bytes':len(block),'sha256':H(observed)});offset+=len(block)
    require(offset==OUTER_BYTES and replay.hexdigest()==OUTER_SHA and len(parts)==5,'Full multipart roundtrip refused')
    return parts

def main():
    p=argparse.ArgumentParser();p.add_argument('--repo',required=True);p.add_argument('--expected-commit',required=True);p.add_argument('--cut-sha256',required=True);p.add_argument('--output',required=True);args=p.parse_args()
    token=os.environ.pop('ASTRA_TRANSPORT_GITHUB_TOKEN',None);require(isinstance(token,str) and bool(token),'Standard GitHub Actions token missing')
    require(os.environ.get('GITHUB_REPOSITORY')==REPO and os.environ.get('GITHUB_SHA')==args.expected_commit and os.environ.get('GITHUB_EVENT_NAME') in ('workflow_dispatch','push'),'Transport caller environment refused')
    require(os.environ.get('GITHUB_REF')=='refs/heads/validation/astra-auth29-original-artifact-transport-current25-01','Transport-only branch refused')
    repo=pathlib.Path(args.repo);require(repo.is_absolute() and repo.resolve()==repo,'Caller root must be original absolute path')
    output=pathlib.Path(args.output);temp=pathlib.Path(os.environ['RUNNER_TEMP']);require(temp.is_absolute() and temp.resolve()==temp and stat.S_ISDIR(temp.lstat().st_mode),'Hosted temporary parent refused')
    expectedOutput=temp/('astra-auth29-original-transport-'+os.environ['GITHUB_RUN_ID']+'-'+os.environ['GITHUB_RUN_ATTEMPT']);require(output==expectedOutput and not os.path.lexists(output),'Fresh transport-only output path refused');output.mkdir(mode=0o700)
    receipt={'status':'INCOMPLETE_TRANSPORT_ONLY_NO_AUTH_ACCEPTANCE','originalArtifactId':ARTIFACT,'originalRunId':ORIGINAL_RUN,'originalHead':ORIGINAL_HEAD,'transportRunId':os.environ['GITHUB_RUN_ID'],'transportRunAttempt':os.environ['GITHUB_RUN_ATTEMPT'],'configuredOutputRetentionDays':14,'maximumReadChunk':CHUNK,'maximumOriginalBytes':CAP,'maximumPartBytes':PART_SIZE,'nativeOrAuthExecuted':False,'credentialsRetained':False,'signedDownloadUrlRetained':False}
    try:
        if os.environ['GITHUB_EVENT_NAME']=='push':
            require(args.cut_sha256=='COMMITTED_GIT_OBJECT','Push cut-source mode refused')
            committedCut=subprocess.check_output(['git','-C',str(repo),'show',args.expected_commit+':'+CUT],env={**os.environ,'GIT_OPTIONAL_LOCKS':'0'},stderr=subprocess.PIPE)
            args.cut_sha256=H(committedCut)
        else:require(len(args.cut_sha256)==64 and all(c in '0123456789abcdef' for c in args.cut_sha256),'Manual exact cut SHA missing')
        before=verify_cut(repo,args.expected_commit,args.cut_sha256);expectedBytes=(repo/EXPECTED).read_bytes();require(H(expectedBytes)==EXPECTED_SHA,'Independent original archive admission SHA refused');expected=json.loads(expectedBytes)
        require(expected['status']=='OFFICIAL_HEADLESS_ARCHIVE_COMPLETE_BEFORE_FIRST_EXECUTION' and expected['playwrightVersion']=='1.63.0' and expected['browserVersion']=='153.0.8010.12' and expected['browserRevision']=='1243','Source-bound maintained browser descriptor refused')
        api,run=obtain_api_provenance(token);receipt['originalArtifactApi']=api;receipt['originalRun']=run
        original=output/'original-api-artifact.zip';receipt['originalDownload']=download_original(token,original);del token
        verification=verify_archive(original,expected);after=verify_cut(repo,args.expected_commit,args.cut_sha256);require(before==after,'Caller source cut changed')
        parts=split_verified(original,output)
        require(verify_cut(repo,args.expected_commit,args.cut_sha256)==before,'Final caller source cut changed')
        receipt.update({'status':'COMPLETE_ORIGINAL_ARTIFACT_TRANSPORT_ONLY_VERIFIED','callerSourceBeforeAfter':before,'independentAdmissionSha256':EXPECTED_SHA,'completeArchiveVerification':verification,'parts':parts,'originalRetainedHostedForJobLifetime':True,'originalRemoteArtifactUnmodified':True,'allFivePartRoundtripSha256':OUTER_SHA,'originalApiRetentionQualification':'Original API created/expiry timestamps and configured original 14-day workflow retention retained; three-second API rounding is not asserted as 14 days remaining. New output uploads separately configure 14-day retention.','qualification':'Transport packaging only. Does not rerun or accept auth/OIDC/browser/700 scenarios. The original full6/OIDC failure and successful traffic/browser evidence remain unchanged.'})
        index={'status':'COMPLETE_ORIGINAL_API_ARTIFACT_MULTIPART_TRANSPORT_ONLY','originalArtifactApi':api,'originalRun':run,'originalBytes':OUTER_BYTES,'originalSha256':OUTER_SHA,'maximumPartBytes':PART_SIZE,'parts':parts,'verificationReceiptSha256':H((json.dumps(receipt,indent=2)+'\n').encode()),'independentAdmissionSha256':EXPECTED_SHA,'configuredUploadRetentionDays':14,'qualification':receipt['qualification']}
        (output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n');(output/'index.json').write_text(json.dumps(index,indent=2)+'\n')
        print('TRANSPORT_ONLY_COMPLETE '+str(ARTIFACT)+' '+OUTER_SHA+' PARTS '+str(len(parts)),flush=True)
    except BaseException as error:
        receipt.update({'status':'INCOMPLETE_TRANSPORT_ONLY_REFUSED','failureType':type(error).__name__,'qualification':'No original archive or auth acceptance. Exception text, signed download URL and credentials are not retained. No completed index is emitted.'})
        (output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n');print('TRANSPORT_ONLY_REFUSED '+type(error).__name__,flush=True);return 1
    return 0
if __name__=='__main__':raise SystemExit(main())
