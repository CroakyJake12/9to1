#!/usr/bin/env python3
"""Finish the already-authorised build delivery; never dispatch or retry a build."""
import json, os, subprocess, sys, time
from pathlib import Path
B=Path(__file__).resolve().parent.parent
RUN=40135259270
SOURCE='ecd1973d6f2694cc3e9a14f9931ef27d02d66b54'
CI='2f830ecab078be2cb63a5e9c276865abbf1ae918'
OUT=B/'outputs/9to1-final-delivery-status.json'
EVIDENCE=B/f'work/ci-cui/evidence-{RUN}'
def command(args):
    return subprocess.check_output([str(x) for x in args],text=True,timeout=180)
def save(value):
    value.update(observed_at=time.time(),run_id=RUN,source_revision=SOURCE,ci_commit=CI,native_smoke='NOT_RUN')
    pending=OUT.with_suffix('.tmp');pending.write_text(json.dumps(value,indent=2)+'\n');os.replace(pending,OUT)
def main():
    save({'status':'WAITING_FOR_ACTUAL_CI','ready_for_local_development':False})
    end=time.monotonic()+1800
    while True:
        server=json.loads(command(['gh','run','view',RUN,'--repo','CroakyJake12/9to1','--json','databaseId,headSha,status,conclusion,updatedAt,url']))
        if server['databaseId']!=RUN or server['headSha']!=CI:raise RuntimeError('Actual run identity differs')
        if server['status']=='completed':break
        if time.monotonic()>end:raise TimeoutError('CI outcome remains unknown; no repeat dispatch')
        time.sleep(30)
    terminal=B/f'work/ci-cui/terminal-{RUN}.json'
    terminal.write_text(json.dumps(server,indent=2)+'\n')
    if not EVIDENCE.exists():command(['gh','run','download',RUN,'--repo','CroakyJake12/9to1','--dir',EVIDENCE])
    receipts=list(EVIDENCE.rglob('receipt.json'));archives=list(EVIDENCE.rglob('Haven-linux-x64.tar.gz'))
    if len(receipts)!=1:raise RuntimeError('Exact owning receipt missing or ambiguous')
    receipt=json.loads(receipts[0].read_text())
    if server['conclusion']!='success':
        save({'status':'CI_FAILED_EVIDENCE_RETAINED','ready_for_local_development':False,'reason':receipt.get('reason'),'receipt':str(receipts[0]),'archives':[str(x) for x in archives]});return
    if len(archives)!=1:raise RuntimeError('Exact Desktop archive missing or ambiguous')
    installed=json.loads(command(['python3','-B',B/'work/install_verified_desktop.py','--source-revision',SOURCE,'--ci-commit',CI,'--run-id',RUN,'--receipt',receipts[0],'--server-observation',terminal,'--archive',archives[0]]))
    checked=command([B/'outputs/Launch 9to1.sh','--check'])
    save({'status':'VERIFIED_BUILD_INSTALLED_SMOKE_PENDING','ready_for_local_development':False,'installation':installed,'launcher_check':checked,'launcher':str(B/'outputs/Launch 9to1.sh')})
if __name__=='__main__':
    try:main()
    except Exception as error:
        save({'status':'DELIVERY_BLOCKED','ready_for_local_development':False,'reason':str(error)});raise
