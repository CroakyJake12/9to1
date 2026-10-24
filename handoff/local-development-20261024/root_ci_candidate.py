#!/usr/bin/env python3
"""Small Root SDK1 successor/dispatch helper for the existing CUI workflow.

Install beside issue_root_ci_delegation.py in work/ci-cui. It reuses the native
CAS ledger, exact terminal receipts and registered workflow. An existing dispatch
intent is reconciliation-only: UNKNOWN never causes another workflow POST.
"""
import argparse, contextlib, fcntl, json, os, re, stat, subprocess, sys, time, uuid
from pathlib import Path
WORK=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(WORK/'local-team-runtime/frozen-launcher-d3d11eef'))
import workflow as w
OWNER='native:local:01a16df8-1971-7d63-be3a-b8548a888274'
REPO='CroakyJake12/9to1';BRANCH='codex/cui-owning-ci-20261024';WORKFLOW='362661383'
DEADLINE=1792875600
TERMINAL=('RELEASED','COMPLETED','CANCELLED')

def need(value,message):
    if not value:raise w.Refusal(message)
def pin(path):return {'path':str(path.resolve()),**w.file_identity(path)}
def revision(value):
    if type(value) is not str or not re.fullmatch('[0-9a-f]{40}',value):raise argparse.ArgumentTypeError('Exact lowercase40hex required')
    return value
def gh_json(args):return json.loads(subprocess.check_output(['gh',*args],text=True,timeout=60))
def state_path():return WORK/'local-team-runtime/development-workflow-state.json'
def active_reservation(args):
    state=w.read(state_path());row=state.get('local_sdk_allocations',{}).get(args.reservation_name,{})
    need(row.get('owner')==OWNER and row.get('status')=='RESERVED_FOR_CI' and row.get('slots')==1
        and row.get('source_revision')==args.source_revision,'Exact existing native SDK1 reservation unavailable')
    need(all(name==args.reservation_name or other.get('status') in TERMINAL
        for name,other in state.get('local_sdk_allocations',{}).items()),'Another SDK slot retains custody')
    return state,row

def deadline_authority(args):
    if time.time()<DEADLINE:return None
    import hashlib
    completion=WORK/'local-team-runtime/OWNER-POST-DEADLINE-COMPLETE-VERIFICATION-20261024.json'
    if completion.is_file():
        need(hashlib.sha256(completion.read_bytes()).hexdigest()=='b9aa9cacc6fdeafe58f15b45685e84636951e9bc5fa883857ec05baebccde7d9','Owner completion approval changed')
        approval=w.read(completion)
        need(1 <= args.attempt <= 10,'Invalid verification attempt')
        suffix='' if args.attempt==1 else '-attempt-'+str(args.attempt)
        need(args.reservation_name=='github-desktop-'+args.source_revision[:8]+'-completion'+suffix,'Completion reservation must bind exact source and attempt')
        w.git(WORK/'local-integration-20261024','merge-base','--is-ancestor',approval['minimum_source_ancestor'],args.source_revision)
        if hasattr(args,'intent'):need(args.intent.resolve()==(WORK/'local-team-runtime'/('ci-completion-'+args.source_revision+suffix+'.json')).resolve(),'Completion dispatch requires exact-source single durable intent')
        if hasattr(args,'owning_check_scope'):need(args.owning_check_scope==approval['scope'],'Completion scope differs')
        return pin(completion)
    path=WORK/'local-team-runtime/OWNER-POST-DEADLINE-ONE-VERIFICATION-20261024.json'
    need(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest()=='30ecf99965db11703abc2b393549528332fe6696c314da5b1d5615d71331dd9d','Exact owner one-build approval unavailable')
    approval=w.read(path)
    need(args.source_revision==approval['source_revision'] and args.reservation_name==approval['reservation_name']
        and approval['maximum_workflow_posts']==1 and approval['feature_workers_remain_parked'] is True,
        'Late exception is limited to one exact approved source/reservation')
    if hasattr(args,'intent'):need(args.intent.resolve()==Path(approval['intent_path']).resolve(),'Late dispatch requires the single owner-approved intent')
    if hasattr(args,'owning_check_scope'):need(args.owning_check_scope==approval['scope'],'Late scope differs')
    return pin(path)

def reserve(args):
    approval_pin=deadline_authority(args)
    run=gh_json(['run','view',str(args.prior_run_id),'--repo',REPO,'--json','databaseId,headSha,status,conclusion,updatedAt,url'])
    need(run.get('databaseId')==args.prior_run_id and run.get('status')=='completed' and run.get('conclusion')
        and run.get('headSha')==args.prior_ci_commit,'Prior CI is not the exact observed terminal run')
    remote=w.read(args.terminal_receipt);remote_pin=pin(args.terminal_receipt)
    need(remote.get('ended_at') and remote.get('actual_host',{}).get('github_run_id')==str(args.prior_run_id)
        and remote.get('actual_host',{}).get('ci_commit')==args.prior_ci_commit
        and all(not stage.get('remaining_group') for stage in remote.get('stages',[])),
        'Prior actual compiler receipt is nonterminal, mismatched or retains SDK groups')
    root=WORK/'local-integration-20261024'
    need(w.git(root,'rev-parse','HEAD').decode().strip()==args.source_revision and not w.git(root,'status','--porcelain').strip(),
        'Root candidate differs or has uncommitted source')
    parent=remote.get('source_revision');revision(parent)
    w.git(root,'merge-base','--is-ancestor',parent,args.source_revision)
    review_pin=pin(args.source_review)
    observation_path=args.terminal_receipt.parent/('server-terminal-'+str(args.prior_run_id)+'.json')
    if observation_path.exists():need(w.read(observation_path)==run,'Preserve existing terminal server observation')
    else:w.atomic(observation_path,run)
    for attempt in range(3):
        current=w.read(state_path())
        try:
            with w.transaction(state_path(),current['generation']) as state:
                rows=state['local_sdk_allocations'];existing=rows.get(args.reservation_name)
                if existing is not None:
                    need(existing.get('status')=='RESERVED_FOR_CI' and existing.get('source_revision')==args.source_revision
                         and existing.get('owning_check_scope','cui')==args.owning_check_scope,'Successor name already used for different custody')
                    return {'status':'ALREADY_RESERVED','reservation':args.reservation_name,'record':existing}
                prior=rows.get(args.prior_reservation,{})
                need(prior.get('owner')==OWNER and prior.get('status') in ('RESERVED_FOR_CI','RELEASED')
                    and prior.get('source_revision')==parent,'Prior native SDK custody changed')
                if prior.get('status')=='RELEASED':
                    need(prior.get('remote_run_id')==args.prior_run_id and prior.get('remote_terminal_receipt')==remote_pin,
                        'Released prior SDK custody lacks this exact terminal receipt')
                need(remote.get('actual_host',{}).get('native_reservation',{}).get('id')==prior.get('reservation_id'),
                    'Terminal remote receipt belongs to another native reservation')
                need(all(name==args.prior_reservation or row.get('status') in TERMINAL for name,row in rows.items()),
                    'Another SDK slot retains custody')
                local_pin=prior['terminal_receipt'];w.recovery_reference(local_pin,content=True);local=w.read(local_pin['path'])
                need(local.get('ended_at'),'Prior local SDK remains nonterminal')
                for stage in local.get('stages',[]):
                    need(not stage.get('remaining_group'),'Prior local SDK members remain')
                    identity=stage.get('process')
                    if identity:
                        actual=w.process_identity(identity['pid'])
                        need(not actual or actual['start_ticks']!=identity['start_ticks'],'Prior local SDK process remains')
                        need(not w.remaining_group(identity),'Prior local SDK group remains')
                prior.update(status='RELEASED',released_at=time.time(),remote_run_id=args.prior_run_id,
                    remote_terminal_receipt=remote_pin,remote_terminal_observation=pin(observation_path),result=remote.get('result'))
                row={'owner':OWNER,'status':'RESERVED_FOR_CI','slots':1,'reservation_id':'9to1-cui-ci-'+uuid.uuid4().hex,
                    'source_revision':args.source_revision,'parent_source_revision':parent,'github_ref':'refs/heads/'+BRANCH,
                    'adopted_generation':state['generation']+1,'created_at':time.time(),'local_process_groups_terminal':True,
                    'terminal_receipt':local_pin,'previous_remote_terminal_receipt':remote_pin,'deadline_utc_epoch':DEADLINE,
                    'source_review':review_pin,'owning_check_scope':args.owning_check_scope,'owner_late_verification_approval':approval_pin}
                rows[args.reservation_name]=row
            return {'status':'RESERVED','reservation':args.reservation_name,'record':row}
        except w.Refusal as error:
            if str(error)!='Stale generation; reread/reconcile rather than overwriting' or attempt==2:raise
    raise w.Refusal('SDK reservation was not adopted')

@contextlib.contextmanager
def intent_lock(path):
    path.parent.mkdir(parents=True,exist_ok=True)
    fd=os.open(path,os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW,0o600)
    try:
        need(stat.S_ISREG(os.fstat(fd).st_mode),'Intent lock is not a regular file')
        fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB);yield
    finally:os.close(fd)

def matching_runs(commit):
    runs=gh_json(['run','list','--repo',REPO,'--workflow',WORKFLOW,'--branch',BRANCH,'--limit','100',
        '--json','databaseId,headSha,event,status,conclusion,url,createdAt'])
    return [r for r in runs if r.get('headSha')==commit and r.get('event')=='workflow_dispatch']

def reconcile(path,intent):
    matches=matching_runs(intent['body']['ci_commit'])
    need(len(matches)<=1,'Multiple exact-commit server runs; preserve UNKNOWN for manual identity reconciliation')
    if matches:
        intent.update(status='DISPATCH_CONFIRMED',actual_run=matches[0],last_observed_at=time.time());w.atomic(path,intent)
    return {'status':intent['status'],'intent':str(path.resolve()),'actual_run':intent.get('actual_run'),'workflow_POSTs':0}

def dispatch(args):
    with intent_lock(args.intent.with_suffix(args.intent.suffix+'.lock')):
        state=w.read(state_path());row=state.get('local_sdk_allocations',{}).get(args.reservation_name,{})
        need(row.get('owner')==OWNER and row.get('source_revision')==args.source_revision,
            'Recorded native reservation source/owner differs')
        instruction=w.read(args.delegation);delegation_pin=pin(args.delegation)
        need(instruction.get('candidate')==args.source_revision and instruction.get('native_sdk_reservation',{}).get('id')==row['reservation_id']
            and instruction.get('owning_check_scope','cui')==row.get('owning_check_scope','cui'),
            'Immutable instruction differs from actual source/reservation/check scope')
        body={'source_revision':args.source_revision,'ci_commit':args.ci_commit,'delegation_sha256':delegation_pin['sha256'],
            'reservation_id':row['reservation_id'],'workflow_id':WORKFLOW,'branch':BRANCH}
        if args.intent.exists():
            intent=w.read(args.intent);need(intent.get('body')==body,'Existing dispatch intent is for different immutable inputs')
            return reconcile(args.intent,intent) # Never repeat a POST, including after an unknown transport outcome.
        need(not args.reconcile_only,'Reconciliation requires the existing immutable dispatch intent')
        state,row=active_reservation(args)
        approval_pin=deadline_authority(args)
        need(not matching_runs(args.ci_commit),'Existing exact-commit run; reconcile its retained intent instead of dispatching')
        remote=gh_json(['api','repos/'+REPO+'/git/ref/heads/'+BRANCH])
        need(remote.get('object',{}).get('sha')==args.ci_commit,'Remote CI branch changed before dispatch')
        checkout=WORK/'cui-owning-ci-20261024'
        need(w.git(checkout,'rev-parse','HEAD').decode().strip()==args.ci_commit
            and not w.git(checkout,'status','--porcelain').strip(),'Local reviewed CI checkout differs or is dirty')
        retained=w.git(checkout,'show',args.ci_commit+':eng/development/ci-cui-root-delegation.json')
        import hashlib
        need(hashlib.sha256(retained).hexdigest()==delegation_pin['sha256'],'CI commit does not contain the exact pinned delegation')
        intent={'schema':'9to1.root-ci-dispatch-intent/v1','status':'UNKNOWN','created_at':time.time(),'body':body,
            'native_generation_observed':state['generation'],'owner_late_verification_approval':approval_pin,'policy':'One durable intent permits at most one actual workflow POST; unknown outcome is reconciliation-only.'}
        w.atomic(args.intent,intent) # Durable UNKNOWN exists before the external side effect.
        _,fresh=active_reservation(args);need(fresh['reservation_id']==row['reservation_id'],'SDK reservation changed before POST')
        command=['gh','workflow','run',WORKFLOW,'--repo',REPO,'--ref',BRANCH,
            '-f','pipeline=cui-guarded','-f','cui_product_revision='+args.source_revision,
            '-f','cui_expected_ci_commit='+args.ci_commit,'-f','cui_delegation_sha256='+delegation_pin['sha256'],
            '-f','cui_native_reservation='+row['reservation_id']]
        try:
            result=subprocess.run(command,capture_output=True,text=True,timeout=60)
            intent['dispatch_returncode']=result.returncode
        except (OSError,subprocess.TimeoutExpired) as error:
            intent['dispatch_transport_error']=type(error).__name__
        w.atomic(args.intent,intent)
        result=reconcile(args.intent,intent);result['workflow_POSTs']=1;return result

def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    common=argparse.ArgumentParser(add_help=False);common.add_argument('--source-revision',type=revision,required=True)
    common.add_argument('--reservation-name',required=True)
    common.add_argument('--attempt',type=int,default=1)
    r=sub.add_parser('reserve',parents=[common]);r.add_argument('--prior-reservation',required=True);r.add_argument('--prior-run-id',type=int,required=True)
    r.add_argument('--prior-ci-commit',type=revision,required=True);r.add_argument('--terminal-receipt',type=Path,required=True)
    r.add_argument('--source-review',type=Path,required=True);r.add_argument('--owning-check-scope',choices=('cui','cui-and-notes','cui-notes-desktop'),default='cui')
    d=sub.add_parser('dispatch',parents=[common]);d.add_argument('--ci-commit',type=revision,required=True);d.add_argument('--delegation',type=Path,required=True)
    d.add_argument('--intent',type=Path,required=True);d.add_argument('--reconcile-only',action='store_true')
    args=parser.parse_args();print(json.dumps(reserve(args) if args.command=='reserve' else dispatch(args)))
if __name__=='__main__':
    try:main()
    except (OSError,ValueError,KeyError,TypeError,subprocess.SubprocessError,w.Refusal) as error:
        print(json.dumps({'status':'BLOCKED','reason':str(error)}));raise SystemExit(2)
