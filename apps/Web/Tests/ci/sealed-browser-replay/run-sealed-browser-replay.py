#!/usr/bin/env python3
"""Replay existing actual browser oracles on an independently pinned CI bundle."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import zipfile

COMMIT='22d2e2dd35c934f190d28efdef0190cbd8c4c957'
RECEIPT_SHA='330d6031e32e9e60fa76afb81478d33d5bc540713fdff07ae6fe3ac61aee9343'
ZIP_SHA='b3257dfb72a1d69a933037fc10c4a4fe0a020a0f41ebe332ab55fc1aa3d3d65e'
SEAL_SHA='1b1b92c927ab4949cc9f42805db2a243f494af0248f9a0daae42da9f9f6b6f5a'
COMMON_SHA='a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
VERIFIER_SHA='e5481670b9647ccb7f8cfb6b1106d6a3ee1973b0e507e0ef77ec70e53bb31f8a'
FILE_COUNT=285
TOTAL_BYTES=58424254
ZIP_BYTES=35870413
PART_BYTES=19*1024*1024
MAX_PUBLIC_BYTES=128*1024*1024
HERE=Path(__file__).resolve().parent

def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda:stream.read(1024*1024),b''):value.update(data)
    return value.hexdigest()

def write_json(path,value):path.write_text(json.dumps(value,indent=2)+'\n')

def public_pins(paths):
    files=set()
    for path in paths:
        entries=[path] if path.is_file() else path.rglob('*')
        for entry in entries:
            if entry.is_symlink():raise RuntimeError('Public tool input symlink unsupported')
            if entry.is_file():files.add(entry)
    return [{'path':str(p),'bytes':p.stat().st_size,'sha256':digest(p)} for p in sorted(files)]

def verify_pins(rows):
    for row in rows:
        p=Path(row['path'])
        if p.is_symlink() or not p.is_file() or p.stat().st_size!=row['bytes'] or digest(p)!=row['sha256']:raise RuntimeError('Public tool/certificate input changed')

def safe_name(name):
    return isinstance(name,str) and bool(name) and not name.startswith('/') and '\\' not in name and '\0' not in name and all(x not in ('','.','..') for x in name.split('/'))

def transport_verified(transport):
    expected={'publish-manifest.json','seal.json','wwwroot.zip'}
    if {p.name for p in transport.iterdir()}!=expected or any(p.is_symlink() or not p.is_file() for p in transport.iterdir()):raise RuntimeError('Exact three regular public artifact files required')
    receipt_path=transport/'publish-manifest.json';seal_path=transport/'seal.json';archive_path=transport/'wwwroot.zip'
    if digest(receipt_path)!=RECEIPT_SHA or digest(seal_path)!=SEAL_SHA or digest(archive_path)!=ZIP_SHA or archive_path.stat().st_size!=ZIP_BYTES:raise RuntimeError('Actual original receipt/seal/inner archive differs from independently pinned tuple')
    receipt=json.loads(receipt_path.read_bytes());seal=json.loads(seal_path.read_bytes())
    if receipt.get('sourceCommit')!=COMMIT or receipt.get('sourceCommitAfter')!=COMMIT or receipt.get('exitCode')!=0:raise RuntimeError('Exact successful runtime source commit required')
    if receipt.get('fileCount')!=FILE_COUNT or receipt.get('totalBytes')!=TOTAL_BYTES or receipt.get('sourceInputs')!=4783 or receipt.get('sourceInputsAfter')!=4783:raise RuntimeError('Actual publisher source/inventory tuple differs')
    for name in ('changedSourceInputs','addedSourceInputs','removedSourceInputs','trackedChangesAfter'):
        if receipt.get(name)!=[]:raise RuntimeError('Publisher source custody differs')
    if seal!={'sourceCommit':COMMIT,'receiptSha256':RECEIPT_SHA,'zipSha256':ZIP_SHA,'zipBytes':ZIP_BYTES,'fileCount':FILE_COUNT,'totalBytes':TOTAL_BYTES,'runtimeVerified':False,'deploymentVerified':False,'fullParityVerified':False}:raise RuntimeError('Actual seal differs from recorded producer tuple')
    rows=receipt.get('publishFiles');seen=set()
    if not isinstance(rows,list) or len(rows)!=FILE_COUNT:raise RuntimeError('Full original inventory required')
    for row in rows:
        name=row['path']
        if not safe_name(name) or name in seen or type(row['bytes']) is not int or row['bytes']<0 or not isinstance(row['sha256'],str) or len(row['sha256'])!=64 or any(c not in '0123456789abcdef' for c in row['sha256']):raise RuntimeError('Invalid original inventory row')
        seen.add(name)
    if sum(x['bytes'] for x in rows)!=TOTAL_BYTES or TOTAL_BYTES>MAX_PUBLIC_BYTES:raise RuntimeError('Bounded original body size differs')
    with zipfile.ZipFile(archive_path) as archive:
        items=archive.infolist()
        if len(items)!=FILE_COUNT or len({i.filename for i in items})!=FILE_COUNT or {i.filename for i in items}!=seen:raise RuntimeError('Exact complete ZIP member set required')
        by_name={r['path']:r for r in rows}
        for item in items:
            mode=(item.external_attr>>16)&0xffff
            if item.is_dir() or not safe_name(item.filename) or (stat.S_IFMT(mode) not in (0,stat.S_IFREG)) or item.file_size!=by_name[item.filename]['bytes']:raise RuntimeError('ZIP regular member/size differs')
            value=hashlib.sha256();count=0
            with archive.open(item) as stream:
                for data in iter(lambda:stream.read(1024*1024),b''):
                    count+=len(data)
                    if count>by_name[item.filename]['bytes']:raise RuntimeError('ZIP member exceeds declared bound')
                    value.update(data)
            if count!=item.file_size or value.hexdigest()!=by_name[item.filename]['sha256']:raise RuntimeError('Complete actual ZIP body/CRC/hash differs')
    return receipt

def extract_verified(archive_path,receipt,bundle):
    if bundle.exists():raise RuntimeError('Fresh immutable receiver directory required')
    bundle.mkdir()
    with zipfile.ZipFile(archive_path) as archive:
        for row in receipt['publishFiles']:
            path=bundle/row['path'];path.parent.mkdir(parents=True,exist_ok=True)
            with archive.open(row['path']) as source,path.open('xb') as target:shutil.copyfileobj(source,target,1024*1024)
    for row in receipt['publishFiles']:
        path=bundle/row['path']
        if path.stat().st_size!=row['bytes'] or digest(path)!=row['sha256']:raise RuntimeError('Extracted unchanged public body differs')

def split_public(archive_path,receipt_path,seal_path,destination):
    destination.mkdir();parts=[]
    with archive_path.open('rb') as stream:
        index=0
        while True:
            data=stream.read(PART_BYTES)
            if not data:break
            index+=1;directory=destination/('part%02d'%index);directory.mkdir();path=directory/'wwwroot.zip.part'
            path.write_bytes(data);parts.append({'part':index,'bytes':len(data),'sha256':digest(path),'path':path.relative_to(destination).as_posix()})
    if len(parts)!=2 or sum(x['bytes'] for x in parts)!=ZIP_BYTES:raise RuntimeError('Exact two full-custody public parts required')
    value={'sourceCommit':COMMIT,'archiveSha256':ZIP_SHA,'archiveBytes':ZIP_BYTES,'originalReceiptSha256':RECEIPT_SHA,'originalSealSha256':SEAL_SHA,'parts':parts,'scope':'Exact original inner archive split for transport only; no reconstructed assets or browser acceptance'}
    for row in parts:
        folder=destination/('part%02d'%row['part']);write_json(folder/'parts-manifest.json',value)
        shutil.copyfile(receipt_path,folder/'original-publish-manifest.json');shutil.copyfile(seal_path,folder/'original-seal.json')
        if sum(x.stat().st_size for x in folder.iterdir())>=20*1024*1024-65536:raise RuntimeError('Each public transport part must retain bounded ZIP overhead below20MiB')
    return value

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--suite',choices=('accounts7','wave14'),required=True);parser.add_argument('--transport',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    root=Path.cwd().resolve();output=args.output.resolve();transport=args.transport.resolve();common_path=root/'apps/Web/Tests/ci/run-ordinary-native.py';verifier_path=root/'apps/Web/Tests/verify-publish-receipt.cjs'
    if output.exists() or output.is_relative_to(root) or digest(common_path)!=COMMON_SHA or digest(verifier_path)!=VERIFIER_SHA:raise RuntimeError('Fresh external output/exact maintained Commands required')
    sys.dont_write_bytecode=True
    protected=[common_path,verifier_path,*[p for p in HERE.iterdir() if p.is_file()]];source_before=[{'path':str(p),'sha256':digest(p)} for p in protected]
    spec=importlib.util.spec_from_file_location('team_b_commands',common_path);common=importlib.util.module_from_spec(spec);spec.loader.exec_module(common)
    output.mkdir(parents=True);diagnostics=output/'diagnostics';diagnostics.mkdir();env=os.environ.copy()
    for name,folder in {'TMPDIR':'setup-tmp','TMP':'setup-tmp','TEMP':'setup-tmp','XDG_CACHE_HOME':'setup-cache','PLAYWRIGHT_BROWSERS_PATH':'browser-tools','npm_config_cache':'npm-cache'}.items():
        path=output/folder;path.mkdir(exist_ok=True);env[name]=str(path)
    env['PYTHONDONTWRITEBYTECODE']='1';commands=common.Commands(diagnostics,env,root)
    result={'status':'NOT_RUN','suite':args.suite,'runtimeSourceCommit':COMMIT,'scope':'Real sealed22d anonymous local-device browser oracles on ordinary hostedCI only; issuer/provider/realBFCache/deploy/full31parity NOT_RUN','outerArchiveFullBodyVerifiedHere':False,'outerTransport':'Official Actions download-artifact11304679865; independently pinned exact innerZIP/receipt/seal/full285 bodies verified by this driver'}
    local_path=None;local_sha=None;plan=None;receipt=None;bundle=output/'wwwroot';runtime_output=output/'runtime-output';tool_pins=[];tool_roots=[];fixture=None;key=None;key_stat=None
    try:
        result['driverCheckoutCommit']=commands.run('driver-git-head',['git','rev-parse','HEAD'],15).strip()
        commands.run('driver-clean-before',['git','diff','--exit-code','HEAD','--'],15)
        receipt=transport_verified(transport);result['innerTransportVerified']=True
        extract_verified(transport/'wwwroot.zip',receipt,bundle)
        local=dict(receipt);local['publishRoot']=str(bundle)
        if {k:v for k,v in local.items() if k!='publishRoot'}!={k:v for k,v in receipt.items() if k!='publishRoot'}:raise RuntimeError('Only receipt transport root may change')
        local_path=output/'local-publish-manifest.json';write_json(local_path,local);local_sha=digest(local_path)
        write_json(diagnostics/'receipt-map.json',{'sourceCommit':COMMIT,'originalReceiptSha256':RECEIPT_SHA,'originalSealSha256':SEAL_SHA,'originalInnerZipSha256':ZIP_SHA,'localReceiptSha256':local_sha,'onlySemanticChangedField':'publishRoot','originalRoot':receipt['publishRoot'],'localRoot':str(bundle)})
        verifier=['node',str(root/'apps/Web/Tests/verify-publish-receipt.cjs'),str(local_path),str(bundle),COMMIT,local_sha]
        commands.run('full-inventory-before',verifier,30)
        if args.suite=='wave14':result['publicParts']=split_public(transport/'wwwroot.zip',transport/'publish-manifest.json',transport/'seal.json',output/'public-parts')
        tools=output/'tools';commands.run('playwright-install',['npm','install','--prefix',str(tools),'--no-audit','--no-fund','playwright@1.62.0'],180)
        module=tools/'node_modules/playwright';package=json.loads((module/'package.json').read_bytes())
        if package['version']!='1.62.0':raise RuntimeError('Exact ordinary Playwright1.62.0 required')
        commands.run('browser-install',['node',str(module/'cli.js'),'install','--with-deps','chromium'],300)
        browser=commands.run('browser-path',['node','-e','console.log(require('+json.dumps(str(module))+').chromium.executablePath())'],30).strip()
        browser_path=Path(browser)
        if not browser_path.is_file() or not browser_path.is_relative_to(output/'browser-tools'):raise RuntimeError('Actual installed browser path differs')
        env.update(PLAYWRIGHT_MODULE=str(module),CHROMIUM_EXECUTABLE=browser)
        tool_roots=[module,tools/'node_modules/playwright-core',browser_path.parent,Path(shutil.which('node')).resolve(),Path(sys.executable).resolve()]
        tool_pins=public_pins(tool_roots)
        write_json(diagnostics/'public-tool-pins.json',tool_pins)
        write_json(diagnostics/'browser-tool-identity.json',{'playwrightVersion':package['version'],'packageJsonSha256':digest(module/'package.json'),'browserPath':browser,'browserBytes':browser_path.stat().st_size,'browserSha256':digest(browser_path)})
        if args.suite=='accounts7':
            fixture=output/'tls-fixture';fixture.mkdir(mode=0o700);cert=fixture/'client.crt';key=fixture/'client.key'
            commands.run('openssl-version',['openssl','version'],30)
            commands.run('fixture-tls-create',['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=client.example.test','-addext','subjectAltName=DNS:client.example.test','-keyout',str(key),'-out',str(cert)],30)
            os.chmod(key,0o600);key_stat=(key.stat().st_dev,key.stat().st_ino,key.stat().st_size,key.stat().st_mtime_ns)
            san=commands.run('fixture-cert-san-and-dates',['openssl','x509','-in',str(cert),'-noout','-ext','subjectAltName','-dates'],30)
            if 'DNS:client.example.test' not in san:raise RuntimeError('Exact anonymous TLS fixture SAN absent')
            commands.run('fixture-cert-unexpired',['openssl','x509','-in',str(cert),'-noout','-checkend','0'],30)
            pub=fixture/'public.pem';der=fixture/'public.der'
            commands.run('fixture-public-key',['openssl','x509','-in',str(cert),'-pubkey','-noout','-out',str(pub)],30)
            commands.run('fixture-public-spki',['openssl','pkey','-pubin','-in',str(pub),'-outform','DER','-out',str(der)],30)
            spki=base64.b64encode(hashlib.sha256(der.read_bytes()).digest()).decode()
            tool_roots += [cert,pub,der];tool_pins=public_pins(tool_roots);write_json(diagnostics/'public-tool-pins.json',tool_pins)
            env.update(B5_CLIENT_TLS_ROOT=str(fixture),B5_CLIENT_TLS_CERT=str(cert),B5_CLIENT_TLS_KEY=str(key),B5_CLIENT_TLS_SPKI=spki,B5_ACCOUNT_INTEROP_GUI_GRANTED='granted')
            write_json(diagnostics/'anonymous-tls-public-proof.json',{'san':'client.example.test','certSha256':digest(cert),'spkiBase64Sha256':spki,'keyBodyUploaded':False,'issuerAuthority':False})
            runner=HERE/'run-unconfigured-interop7-portable-ci02.cjs';binding=output/'accounts7-binding.json'
            write_json(binding,{'manifestPath':str(local_path),'manifestSha256':local_sha,'sourceCommit':COMMIT,'fileCount':FILE_COUNT,'runnerSha256':digest(runner),'hostSha256':digest(HERE/'sealed-https-host-explicit-binding.cjs')})
            argv=['node',str(runner),str(binding),str(runtime_output),COMMIT];wrapper=HERE/'run-owned-account-replay01.py';expected=7;port=5096
        else:
            runner=HERE/'run-wave-browser-cold-quota.cjs';argv=['node',str(runner),str(bundle),str(runtime_output)];wrapper=HERE/'run-owned-wave-replay01.py';expected=14;port=18761;env['B3_WAVE_GUI_GRANTED']='granted'
        pins=[{'path':str(p),'bytes':p.stat().st_size,'sha256':digest(p)} for p in [runner,wrapper,HERE/'owned-kernel-child-proof01.py',local_path,transport/'publish-manifest.json',transport/'seal.json',transport/'wwwroot.zip',common_path,browser_path,module/'package.json',tools/'node_modules/playwright-core/package.json']]
        plan={'candidateCommit':COMMIT,'output':str(runtime_output),'control':str(output/'control'),'tmpdir':str(output/'bt'),'cache':str(output/'bc'),'resourceRoot':str(output),'browserExecutable':browser,'argv':argv,'maximumSeconds':240,'maximumOutputAndTemporaryBytes':192000000,'workspaceMinimumReserveBytes':256000000,'pins':pins}
        if args.suite=='wave14':plan['environment']={'B3_CANDIDATE_MANIFEST':str(local_path),'B3_CANDIDATE_MANIFEST_SHA256':local_sha,'B3_PORT':str(port)}
        else:plan['pins'] += [{'path':str(p),'bytes':p.stat().st_size,'sha256':digest(p)} for p in [binding,HERE/'sealed-https-host-explicit-binding.cjs']]
        plan_path=output/'execution-plan.json';write_json(plan_path,plan);write_json(diagnostics/'execution-plan.json',plan)
        commands.run('actual-browser-oracles',[sys.executable,str(wrapper),str(plan_path),digest(plan_path)],270)
        report=json.loads((runtime_output/'results.json').read_bytes());counts=report['counts']
        if counts!={'discovered':expected,'executed':expected,'passed':expected,'failed':0,'notRun':0}:raise RuntimeError('Unchanged browser case counts did not all pass')
        if args.suite=='wave14':
            if report.get('candidateCommit')!=COMMIT or report.get('candidateManifestSha256')!=local_sha or any(report['diagnostics']['counts'].values()) or report.get('finalizationFailures'):raise RuntimeError('Strict Wave runtime/candidate integrity failed')
        elif report.get('sourceCommit')!=COMMIT or report.get('cleanupFailures') or any(report['unexpected'].values()):raise RuntimeError('Strict Accounts runtime/candidate integrity failed')
        family=json.loads((output/'control/owned-family-receipt.json').read_bytes())
        if family.get('state')!='CLOSED' or family.get('custodyClean') is not True or family.get('runnerExitCode')!=0 or family.get('kernelOwnChildrenEmpty') is not True or any(row['signalCount'] for row in family['ownedFamily']):raise RuntimeError('Actual owned browser family did not close normally')
        if args.suite=='wave14' and family.get('boundTopBrowserLauncherCount')!=3:raise RuntimeError('Exact three sequential Wave launchers required')
        if args.suite=='accounts7' and family.get('uniqueBoundTopBrowserLauncher') is not True:raise RuntimeError('Exact one Accounts launcher required')
        result.update(status='PASS',counts=counts)
    except Exception as error:
        result.update(status='FAIL',error=repr(error))
    finally:
        try:
            if local_path:commands.run('full-inventory-final',['node',str(root/'apps/Web/Tests/verify-publish-receipt.cjs'),str(local_path),str(bundle),COMMIT,local_sha],30)
            if receipt:transport_verified(transport)
            for row in source_before:
                if digest(Path(row['path']))!=row['sha256']:raise RuntimeError('Final source input changed')
            verify_pins(tool_pins)
            if tool_pins and public_pins(tool_roots)!=tool_pins:raise RuntimeError('Complete installed public tool member set changed')
            if fixture and (stat.S_IMODE(fixture.stat().st_mode)!=0o700 or key.is_symlink() or stat.S_IMODE(key.stat().st_mode)!=0o600 or (key.stat().st_dev,key.stat().st_ino,key.stat().st_size,key.stat().st_mtime_ns)!=key_stat):raise RuntimeError('Ephemeral TLS ownership/mode changed')
            commands.run('driver-clean-final',['git','diff','--exit-code','HEAD','--'],15)
        except Exception as error:result.update(status='FAIL',finalCustodyError=repr(error))
        write_json(diagnostics/'source-pins.json',source_before)
        try:
            copied=0
            for folder,prefix in [(runtime_output,'browser'),(output/'control','owned-family')]:
                if folder.exists():
                    target=diagnostics/prefix;target.mkdir(exist_ok=True)
                    for p in folder.iterdir():
                        if p.is_file() and not p.is_symlink() and (p.suffix in ('.json','.png','.zip','.log')):
                            copied+=p.stat().st_size
                            if copied>192000000:raise RuntimeError('Bounded public diagnostic copy exceeded')
                            shutil.copyfile(p,target/p.name)
            result['uploadedBrowserDiagnosticBytes']=copied
        except Exception as error:result.update(status='FAIL',diagnosticCopyError=repr(error))
        write_json(diagnostics/'result.json',result)
    print(json.dumps(result,indent=2));return 0 if result['status']=='PASS' else 1
if __name__=='__main__':raise SystemExit(main())
