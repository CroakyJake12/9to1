#!/usr/bin/env python3
"""Preserve source WIP as separate commits without changing any worktree or index."""
import datetime,hashlib,json,os,re,subprocess,time
from pathlib import Path
B=Path(__file__).resolve().parent.parent
OUT=B/'outputs/9to1-source-backup.json'
STAMP=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
PREFIX='refs/heads/codex/source-backup-'+STAMP+'/'
STORE=B/'work/local-final-handoff'/('source-backup-'+STAMP)
STORE.mkdir(mode=0o700)
REPO=B/'work/9to1-source.git'
def git(root,*args,env=None,input=None):
    return subprocess.check_output(['git','-C',str(root),*args],env=env,input=input)
def fingerprint(root,paths):
    result={}
    for name in paths:
        p=root/name
        if p.is_symlink():result[name]='link:'+os.readlink(p)
        elif p.is_file():result[name]=hashlib.sha256(p.read_bytes()).hexdigest()
        elif not p.exists():result[name]='DELETED'
        else:raise RuntimeError('Unexpected source directory change: '+name)
    return result
def main():
    roster=json.loads((B/'outputs/9to1-local-development-continuity.json').read_text())['roster']
    receipt={'schema':'9to1.source-backup/v1','created_at':time.time(),'status':'LOCAL_SNAPSHOTS_IN_PROGRESS','source_snapshots':[],
        'preservation':'Original worker files, dirty indexes, sessions and runtime data unchanged. Public backup contains product source and code patches only; private runtime/configuration remains on this PC.'}
    OUT.write_text(json.dumps(receipt,indent=2)+'\n')
    secret=re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|sk-[A-Za-z0-9_-]{24,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
    workers=[x for x in roster if x['role']=='worker']
    for item in workers:
        root=Path(item['worktree']);head=git(root,'rev-parse','HEAD').decode().strip()
        changed=git(root,'diff','--name-only','-z','HEAD').split(b'\0')+git(root,'ls-files','--others','--exclude-standard','-z').split(b'\0')
        allpaths=sorted(set(p.decode() for p in changed if p))
        paths=[p for p in allpaths if p.startswith(('9to1 Workspace/','9to1 Models/','framework/')) or (p.startswith('.opencode/team-coordination/') and Path(p).suffix in ('.cs','.cpp','.hpp','.patch','.diff','.py'))]
        for name in paths:
            p=root/name
            if p.is_file() and not p.is_symlink():
                if p.stat().st_size>10*1024**2 or secret.search(p.read_bytes()):raise RuntimeError('Source requires private-data review: '+name)
        before=fingerprint(root,paths);env=dict(os.environ);env['GIT_INDEX_FILE']=str(STORE/(item['name']+'.index'))
        git(root,'read-tree',head,env=env)
        if paths:git(root,'add','-A','--',*paths,env=env)
        tree=git(root,'write-tree',env=env).decode().strip()
        if before!=fingerprint(root,paths):raise RuntimeError('Worker source changed during snapshot: '+item['name'])
        message=('[skip ci] Preserve '+item['name']+' source WIP; unverified, not integrated\n').encode()
        commit=git(root,'commit-tree',tree,'-p',head,input=message).decode().strip();ref=PREFIX+item['name'];git(REPO,'update-ref',ref,commit,'0'*40)
        receipt['source_snapshots'].append({'owner':item['name'],'commit':commit,'base':head,'ref':ref,'worktree':str(root),'changed_source_paths':paths,'source_fingerprints':before,'private_runtime_paths_preserved_locally':len(allpaths)-len(paths)})
        OUT.write_text(json.dumps(receipt,indent=2)+'\n')
    root=B/'work/local-integration-20261024';head=git(root,'rev-parse','HEAD').decode().strip();ref=PREFIX+'integrated-candidate'
    # Backup child retains exact product tree; skip marker avoids automatic feature CI.
    tree=git(root,'rev-parse',head+'^{tree}').decode().strip()
    backup=git(root,'commit-tree',tree,'-p',head,input=b'[skip ci] Preserve exact integrated development candidate\n').decode().strip();git(REPO,'update-ref',ref,backup,'0'*40)
    receipt['integrated_candidate']={'product_commit':head,'backup_commit':backup,'tree':tree,'ref':ref,'worktree':str(root)}
    refs=[x['ref'] for x in receipt['source_snapshots']]+[ref]
    receipt['status']='LOCAL_SNAPSHOTS_READY_REMOTE_PENDING';OUT.write_text(json.dumps(receipt,indent=2)+'\n')
    git(REPO,'push','--atomic','origin',*[x+':'+x for x in refs])
    observed=dict(line.split('\t')[::-1] for line in git(REPO,'ls-remote','--heads','origin',*refs).decode().splitlines())
    for x in receipt['source_snapshots']:
        if observed.get(x['ref'])!=x['commit']:raise RuntimeError('Remote snapshot mismatch: '+x['owner'])
    if observed.get(ref)!=backup:raise RuntimeError('Remote integrated backup mismatch')
    receipt.update(status='REMOTE_AND_LOCAL_SOURCE_SNAPSHOTS_VERIFIED',verified_at=time.time(),remote_refs_verified=len(refs),remote='https://github.com/CroakyJake12/9to1')
    OUT.write_text(json.dumps(receipt,indent=2)+'\n');print(json.dumps({'status':receipt['status'],'remote_refs_verified':len(refs),'source_paths':sum(len(x['changed_source_paths']) for x in receipt['source_snapshots']),'manifest':str(OUT)}))
if __name__=='__main__':main()
