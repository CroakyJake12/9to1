#!/usr/bin/env python3
"""Read existing official Actions extraction; no browser/SDK/process launch."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import stat
import zipfile

SOURCE_RUN=37212388505
SOURCE_ARTIFACT=11306648191
SOURCE_OUTER_BYTES=44692508
SOURCE_OUTER_METADATA_SHA='1ef9cb1906e7521cb40cf46182430aba2fedf115e1cdea622ae0ec07026566b5'
DRIVER_COMMIT='7f69563623c7a9c9f55de2c50a418c38ab0e889b'
RUNTIME_COMMIT='22d2e2dd35c934f190d28efdef0190cbd8c4c957'
MAX_INPUT=56*1024*1024
MAX_CAUSAL=8*1024*1024
MAX_OUTPUT=192*1024*1024
PART_BYTES=19*1024*1024

def sha(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda:stream.read(1024*1024),b''):value.update(data)
    return value.hexdigest()

def write_json(path,value):path.write_text(json.dumps(value,indent=2)+'\n')

def safe_name(name):
    return bool(name) and not name.startswith('/') and '\\' not in name and '\0' not in name and all(p not in ('','.','..') for p in name.split('/'))

def inventory(root):
    if root.is_symlink() or not root.is_dir():raise RuntimeError('Actual regular extraction directory required')
    rows=[];total=0
    for path in sorted(root.rglob('*')):
        if path.is_symlink():raise RuntimeError('Extracted links unsupported')
        if path.is_dir():continue
        if not path.is_file():raise RuntimeError('Extracted special input unsupported')
        relative=path.relative_to(root).as_posix()
        if not safe_name(relative) or any(x in ('profile','profiles','tls-fixture','node_modules') for x in relative.split('/')) or path.suffix=='.key':raise RuntimeError('Unsafe/private extracted path unsupported')
        total+=path.stat().st_size
        if total>MAX_INPUT or len(rows)>=256:raise RuntimeError('Finite existing diagnostic member bound exceeded')
        rows.append({'path':relative,'bytes':path.stat().st_size,'sha256':sha(path),'extractedMode':stat.S_IMODE(path.stat().st_mode)})
    if not rows:raise RuntimeError('No extracted diagnostic members')
    return rows

def actual_review(root):
    read=lambda name:json.loads((root/name).read_bytes())
    result=read('result.json');report=read('browser/results.json');family=read('owned-family/owned-family-receipt.json');commands=read('commands.json');mapping=read('receipt-map.json');plan=read('execution-plan.json')
    counts={'discovered':14,'executed':14,'passed':14,'failed':0,'notRun':0}
    failures=[]
    def need(condition,message):
        if not condition:failures.append(message)
    need(result.get('runtimeSourceCommit')==report.get('candidateCommit')==RUNTIME_COMMIT and result.get('driverCheckoutCommit')==DRIVER_COMMIT,'exact driver/runtime source tuple')
    need(result.get('innerTransportVerified') is True,'actual285 inner transport gate')
    need(result.get('status')=='PASS' and result.get('counts')==report.get('counts')==counts,'exact fourteen completed PASS criteria')
    need(len(report.get('outcomes',{}))==14 and all(v.get('outcome')=='PASS' for v in report.get('outcomes',{}).values()),'all fourteen raw outcomes')
    need(not any(report.get('diagnostics',{}).get('counts',{'missing':1}).values()) and not report.get('finalizationFailures') and report.get('exit')==0,'actual runtime/trace/identity finalization')
    need(family.get('state')=='CLOSED' and family.get('custodyClean') is True and family.get('runnerExitCode')==0 and family.get('kernelOwnChildrenEmpty') is True and family.get('allOwnedKernelIdentitiesExited') is True and family.get('allOwnedBirthsDisappeared') is True and family.get('finalPinsUnchanged') is True,'actual normal browser kernel closure')
    need(not any(family.get(x,True) for x in ('forcedCleanup','deadlineFailure','budgetFailure','identityFailures','originalChildStillLive')),'no forced/budget/deadline/identity failure')
    need(family.get('boundTopBrowserLauncherCount')==3 and family.get('sequentialBoundTopBrowserLaunchers') is True and all(x.get('signalCount')==0 for x in family.get('ownedFamily',[])),'three sequential held profile tops/no signals')
    need(family.get('kernelOwnChildReceiptAfterWaitAndReap',{}).get('status')=='ECHILD','actual final ECHILD')
    need(sha(root/'execution-plan.json')==family.get('planSha256'),'actual plan rawSHA linkage')
    need(bool(commands) and all(c.get('exit')==c.get('exitAfterDrain')==0 and c.get('normalEOF') is True and c.get('familyClosed') is True and c.get('finalECHILD') is True and not c.get('signals') and not c.get('error') and all(x.get('gone') is True for x in c.get('births',[])) for c in commands),'all normal entry command families')
    need(mapping.get('originalReceiptSha256')=='330d6031e32e9e60fa76afb81478d33d5bc540713fdff07ae6fe3ac61aee9343' and mapping.get('originalInnerZipSha256')=='b3257dfb72a1d69a933037fc10c4a4fe0a020a0f41ebe332ab55fc1aa3d3d65e' and mapping.get('originalSealSha256')=='1b1b92c927ab4949cc9f42805db2a243f494af0248f9a0daae42da9f9f6b6f5a','original normalpublic tuple')
    need(report.get('candidateManifestSha256')==mapping.get('localReceiptSha256'),'actual local manifest linkage')
    for name in ('full-inventory-before.log','full-inventory-final.log'):
        value=read(name);need(value.get('sourceCommit')==RUNTIME_COMMIT and value.get('receiptSha256')==mapping.get('localReceiptSha256') and value.get('fileCount')==285 and value.get('totalBytes')==58424254 and value.get('transportVerified') is True,name+' complete285 gate')
    return {'status':'PASS' if not failures else 'FAIL','failures':failures,'counts':report.get('counts'),'originalResultStatus':result.get('status'),'outcomes':report.get('outcomes'),'commands':len(commands),'browserVersion':report.get('browserVersion'),'familyHeldIdentities':len(family.get('ownedFamily',[])),'scope':'Recorded actual fourteen browser criteria; no rerun, provider/deploy/fullparity acceptance'}

def bundle_parts(root,rows,output):
    container=output/'extracted-diagnostics-members.zip'
    with zipfile.ZipFile(container,'x',compression=zipfile.ZIP_STORED) as archive:
        for row in rows:archive.write(root/row['path'],row['path'])
    if container.stat().st_size>MAX_INPUT+1024*1024:raise RuntimeError('Finite new-container bound exceeded')
    with zipfile.ZipFile(container) as archive:
        if archive.namelist()!=[r['path'] for r in rows]:raise RuntimeError('Exact new-container member inventory required')
        for row in rows:
            value=hashlib.sha256();count=0
            with archive.open(row['path']) as stream:
                for data in iter(lambda:stream.read(1024*1024),b''):count+=len(data);value.update(data)
            if count!=row['bytes'] or value.hexdigest()!=row['sha256']:raise RuntimeError('New-container fullCRC/memberSHA differs')
    parts=[];directory=output/'transport-parts';directory.mkdir();whole=hashlib.sha256()
    with container.open('rb') as stream:
        while True:
            data=stream.read(PART_BYTES)
            if not data:break
            index=len(parts)+1
            if index>3:raise RuntimeError('Finite maximum three parts exceeded')
            part=directory/('part%02d'%index);part.mkdir();path=part/'extracted-members.zip.part';path.write_bytes(data);whole.update(data)
            parts.append({'part':index,'bytes':len(data),'sha256':sha(path),'path':path.relative_to(directory).as_posix()})
    if whole.hexdigest()!=sha(container):raise RuntimeError('Ordered exact part stream differs')
    value={'scope':'New container of exact official-extracted diagnostic member bodies, NOT byte identity of original GitHub outerZIP','sourceRun':SOURCE_RUN,'sourceArtifact':SOURCE_ARTIFACT,'originalOuterMetadataSha256':SOURCE_OUTER_METADATA_SHA,'originalOuterWholeBodyIndependentlyReadHere':False,'containerBytes':container.stat().st_size,'containerSha256':sha(container),'members':rows,'parts':parts}
    for row in parts:
        folder=directory/('part%02d'%row['part']);write_json(folder/'transport-manifest.json',value)
        if sum(p.stat().st_size for p in folder.iterdir())>=20*1024*1024-65536:raise RuntimeError('Part upload overhead reserve exceeded')
    return value

def main():
    p=argparse.ArgumentParser();p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();root=a.input.resolve();output=a.output.resolve()
    if output.exists() or output.is_relative_to(root):raise RuntimeError('Fresh separate owned output required')
    source_before=sha(Path(__file__).resolve())
    output.mkdir(parents=True);causal=output/'causal';causal.mkdir();summary={'status':'FAIL','sourceRun':SOURCE_RUN,'sourceArtifact':SOURCE_ARTIFACT,'sourceOuterMetadataSha256':SOURCE_OUTER_METADATA_SHA,'sourceOuterMetadataBytes':SOURCE_OUTER_BYTES,'originalOuterWholeBodyIndependentlyReadHere':False,'scope':'Official existing artifact extraction+fullmember body custody export only; no browser/SDK/publisher/child launch','before':None,'after':None,'exporterSourceSha256Before':source_before}
    try:
        if shutil.disk_usage(output).free<MAX_OUTPUT+256*1024*1024:raise RuntimeError('Remote finite output+256MiBreserve unavailable')
        before=inventory(root);summary['before']=before;selected=[];total=0
        for row in before:
            source=root/row['path']
            if source.suffix not in ('.json','.log'):continue
            total+=row['bytes']
            if total>MAX_CAUSAL:raise RuntimeError('Finite complete causalJSON/log bound exceeded')
            target=causal/row['path'];target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
            if sha(target)!=row['sha256']:raise RuntimeError('Exported complete causal body differs')
            selected.append(row)
        summary['selectedCausal']=selected;summary['causalBytes']=total
        # Preserve reports and all original outcomes even if review detects a failure.
        try:summary['actualReview']=actual_review(root)
        except Exception as error:summary['actualReview']={'status':'FAIL','error':repr(error)}
        summary['transport']=bundle_parts(root,before,output)
        summary['status']='PASS' if summary['actualReview']['status']=='PASS' else 'FAIL'
    except Exception as error:summary.update(status='FAIL',error=repr(error))
    finally:
        try:
            summary['exporterSourceSha256After']=sha(Path(__file__).resolve())
            if summary['exporterSourceSha256After']!=source_before:raise RuntimeError('Exporter source changed')
            summary['after']=inventory(root)
            if summary['before']!=summary['after']:raise RuntimeError('Whole original extracted member custody changed')
            if sum(x.stat().st_size for x in output.rglob('*') if x.is_file())>MAX_OUTPUT:raise RuntimeError('Finite all-new output budget exceeded')
            if shutil.disk_usage(output).free<256*1024*1024:raise RuntimeError('Final remote floor exceeded')
        except Exception as error:summary.update(status='FAIL',finalCustodyError=repr(error))
        write_json(causal/'export-receipt.json',summary)
        if sum(x.stat().st_size for x in causal.rglob('*') if x.is_file())>=24*1024*1024:raise RuntimeError('Complete causal upload exceeds finite24MiB limit')
        if sum(x.stat().st_size for x in output.rglob('*') if x.is_file())>MAX_OUTPUT or shutil.disk_usage(output).free<256*1024*1024:raise RuntimeError('Final receipt charged resource limit exceeded')
    print(json.dumps({'status':summary['status'],'recordedCounts':summary.get('actualReview',{}).get('counts'),'wholeExtractedMembersUnchanged':summary['before']==summary['after'],'outerWholeArchiveRead':False,'sourceArtifact':SOURCE_ARTIFACT}));return 0 if summary['status']=='PASS' else 1
if __name__=='__main__':raise SystemExit(main())
