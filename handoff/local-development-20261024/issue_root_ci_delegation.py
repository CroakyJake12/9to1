#!/usr/bin/env python3
"""Local Root helper: export only an ACTUAL native SDK reservation to public CI.

No SDK command or ledger mutation. Root must first release local SDK custody and
CAS-record one RESERVED_FOR_CI slot in the existing native state. No fake ACK.
"""
import argparse,datetime,hashlib,json,os,re
from pathlib import Path
import sys
WORK=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(WORK/'local-team-runtime/frozen-launcher-d3d11eef'))
import workflow as w
OWNER='native:local:01a16df8-1971-7d63-be3a-b8548a888274'
GIB=1024**3

def need(value,message):
    if not value:raise w.Refusal(message)

def pin(path):return {'path':str(Path(path).resolve()),**w.file_identity(path)}

def source_revision(value):
    if type(value) is not str or not re.fullmatch('[0-9a-f]{40}',value):
        raise argparse.ArgumentTypeError('Product source must be exact lowercase40hex')
    return value


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-revision',type=source_revision,required=True)
    parser.add_argument('--reservation-name',required=True)
    parser.add_argument('--owning-check-scope',choices=('cui','cui-and-notes','cui-notes-desktop'),default='cui')
    parser.add_argument('--harness',type=Path,required=True)
    parser.add_argument('--workflow',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();PRODUCT=args.source_revision
    need(w.git(WORK/'9to1-source.git','cat-file','-t',PRODUCT).decode().strip()=='commit','Actual product revision is not an available commit')
    state=w.read(WORK/'local-team-runtime/development-workflow-state.json')
    grant_pin=state['local_host_delegation']['grant'];w.recovery_reference(grant_pin,content=True);grant=w.read(grant_pin['path'])
    need(grant.get('authority',{}).get('integration_owner')==OWNER and grant.get('allocation',{}).get('sdk_build_concurrency')==1,
        'Actual existing native Root SDK delegation missing')
    rows=state.get('local_sdk_allocations',{});reservation=rows.get(args.reservation_name,{})
    need(reservation.get('owner')==OWNER and reservation.get('status')=='RESERVED_FOR_CI' and reservation.get('slots')==1
        and reservation.get('source_revision')==PRODUCT and reservation.get('local_process_groups_terminal') is True,
        'Record a genuine native SDK1 reservation after actual local group release first')
    need(type(reservation.get('adopted_generation')) is int and 0<reservation['adopted_generation']<=state['generation']
        and isinstance(reservation.get('reservation_id'),str) and isinstance(reservation.get('github_ref'),str)
        and reservation['github_ref'].startswith('refs/heads/codex/'),'Actual native reservation ID/generation/dev-ref is missing')
    need(reservation.get('owning_check_scope','cui')==args.owning_check_scope,
        'Owning check scope differs from actual native Root reservation')
    for name,row in rows.items():
        need(name==args.reservation_name or row.get('status') in ('RELEASED','COMPLETED','CANCELLED'),
             'Another actual SDK slot still retains custody')
    terminal_pin=reservation.get('terminal_receipt');w.recovery_reference(terminal_pin,content=True);terminal=w.read(terminal_pin['path'])
    need(terminal.get('schema')=='9to1.guarded-local-sdk/v1' and terminal.get('owner')==OWNER
        and terminal.get('ended_at') and terminal.get('result') in ('BLOCKED','PASS_PARTIAL_NOT_REUSABLE'),
        'Actual retained local terminal SDK receipt unavailable')
    for stage in terminal.get('stages',[]):
        identity=stage.get('process');need(not stage.get('remaining_group'),'Prior SDK receipt retains group members')
        if identity:
            actual=w.process_identity(identity['pid'])
            need(not actual or actual['start_ticks']!=identity['start_ticks'],'Prior actual SDK process is still live')
            need(not w.remaining_group(identity),'Prior SDK group still has live members')
    sdk=WORK/'dotnet-sdk-10.0.301';core=['dotnet','sdk/10.0.301/dotnet.dll','sdk/10.0.301/MSBuild.dll',
        'sdk/10.0.301/Roslyn/bincore/csc.dll','sdk/10.0.301/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets']
    sdk_pins={name:w.file_identity(sdk/name)['sha256'] for name in core}
    document={'schema':'9to1.root-live/v1','issued_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'issuer':{'owner':OWNER,'role':'Existing native Root coordinator'},'candidate':PRODUCT,
        'owning_check_scope':args.owning_check_scope,
        'programme':'CroakyJake12/9to1 existing local programme','authority_grant_id':grant['grant_id'],
        'authority_grant_sha256':grant_pin['sha256'],'native_state_generation_observed':state['generation'],
        'native_sdk_reservation':{'id':reservation['reservation_id'],'adopted_generation':reservation['adopted_generation'],
            'status':'RESERVED_FOR_CI','slots':1,'source_revision':PRODUCT,'github_ref':reservation['github_ref'],
            'local_process_groups_terminal':True,'local_terminal_receipt_sha256':terminal_pin['sha256']},
        'delegation':{'sdk_host_observed_adoption':True,'sdk_only_host_projection':True,
            'instruction':'Execute only the reviewed exact CUI and explicitly selected Notes owning commands after observing actual GitHub public repository/host/source/SDK/resources. Adopt the compiler-only execution context through existing workflow.py CAS and execution_admission. Native Root retains global SDK1 and programme ownership; no model/worker/Cloud ACK/acceptance authority is delegated.'},
        'harness_sha256':pin(args.harness)['sha256'],'workflow_sha256':pin(args.workflow)['sha256'],
        'sdk_file_sha256':sdk_pins,'bounds':{'max_aggregate_rss_bytes':4*GIB,'memory_headroom_bytes':GIB,
            'normal_copy_budget_bytes':8*GIB,'disk_reserve_bytes':2*GIB,'overall_seconds':1200,
            'sdk_environment':{'DOTNET_GCHeapHardLimit':'0x80000000','DOTNET_GCConserveMemory':'5'}},
        'accepted_claim':False,'scope':'One exact Root-reviewed committed CUI and explicitly selected Notes development compiler run; all normal analyzers/targets/dependencies retained. Fresh PARTIAL/NOTREUSABLE, no product/release acceptance.'}
    need(not args.output.exists(),'Preserve existing immutable delegation; choose a fresh reviewed file')
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(w.encoded(document))
    print(json.dumps({'delegation_sha256':pin(args.output)['sha256'],'reservation_id':reservation['reservation_id'],
        'source_revision':PRODUCT,'native_generation':state['generation'],'output':str(args.output.resolve()),'SDK_commands':0,'ledger_mutations':0}))

if __name__=='__main__':
    try:main()
    except (OSError,ValueError,KeyError,TypeError,w.Refusal) as error:
        print(json.dumps({'status':'BLOCKED','reason':str(error),'SDK_commands':0,'ledger_mutations':0}));raise SystemExit(2)
