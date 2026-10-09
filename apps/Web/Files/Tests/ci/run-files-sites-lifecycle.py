#!/usr/bin/env python3
"""Ordinary lifecycle source projects; exact maintained Commands is imported.

No process custodian copy, monkeypatch, warm DLL, authority provider or engine.
Negative native FAIL1 remains raw evidence for the separate exact validator.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys

COMMON_SHA = 'a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
PROGRAM_SHA = '94eba9b9970af68a81e1b49fb890fcebb022dd08d59349b3516484189e850b57'
PROJECTS = {
    'lifecycle14': 'apps/Web/Files/Tests/Lifecycle/FilesSites.PrivateLifecycle.Tests.csproj',
    'lifecycle-original01': 'apps/Web/Files/Tests/Lifecycle/SourceControls/FilesSites.PrivateLifecycle.Original01.Tests.csproj',
    'lifecycle-original02': 'apps/Web/Files/Tests/Lifecycle/SourceControls/FilesSites.PrivateLifecycle.Original02.Tests.csproj',
}
ROUTE_PINS = {'apps/Web/Files/Tests/Lifecycle/SourceControls/Original01/FilesBrowserRoute.cs': 'b7968028dba03041f90b4515acb9c227d0bf61ba2da894b8b24c29b76aa1bb86', 'apps/Web/Files/Tests/Lifecycle/SourceControls/Original01/SitesBrowserRoute.cs': 'bce396bc4f18060debdc3a7633053eb9df662a4e8da4ebee64b9502f46e4e9bc', 'apps/Web/Files/Tests/Lifecycle/SourceControls/Original02/FilesBrowserRoute.cs': '62e874fb5065fbbfc30c81ec06556ddb13c7c97333807113a56e9aa4d3f64a98', 'apps/Web/Files/Tests/Lifecycle/SourceControls/Original02/SitesBrowserRoute.cs': 'ff27b52f800c246bb52767d21fb0008b0dee0cc09453c07d0eb923a22107c207'}
CASES = ['actual-files-dirty-refusal-save-close-canonical-reopen', 'actual-files-held-init-fence-failure-awaits', 'actual-sites-dirty-refusal-save-close-canonical-reopen', 'actual-sites-held-init-fence-failure-awaits', 'actual-files-populated-draft-clears-on-fence-failure', 'actual-sites-populated-draft-clears-on-fence-failure', 'actual-files-reentrant-negative-owner-drain-issued-once', 'actual-sites-reentrant-negative-owner-drain-issued-once', 'actual-files-held-preparation-opening-remains-issued', 'actual-sites-held-preparation-opening-remains-issued', 'actual-files-repeated-missing-fence-stays-failed', 'actual-sites-repeated-missing-fence-stays-failed', 'actual-files-owner-action-reentry-joins-fence-settlement', 'actual-sites-owner-action-reentry-joins-fence-settlement']

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--suite',required=True,choices=PROJECTS)
    parser.add_argument('--expected-commit',required=True)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    root=Path.cwd().resolve(); output=args.output.resolve()
    if not re.fullmatch(r'[0-9a-f]{40}',args.expected_commit) or output.exists() or output.is_relative_to(root):
        raise RuntimeError('Require exact actual adopted commit and fresh isolated output outside checkout')
    output.mkdir(parents=True); diagnostics=output/'diagnostics'; diagnostics.mkdir()
    result={'suite':args.suite,'project':PROJECTS[args.suite],'expected':14,'status':'NOT_RUN','sourceCommit':None,
            'scope':'Actual local canonical durable-provider/native OS-Home lifecycle controls only; real issuer-group authority BLOCKED; browser/provider/deployment/full parity NOT_RUN',
            'preservedHistory':'Original01/02 and all prior source06/07/native17/18 evidence unchanged; source-only predictions are not observed red controls',
            'sharedCommandsSHA256':COMMON_SHA}
    commands=None; module=None
    try:
        common=root/'apps/Web/Tests/ci/run-ordinary-native.py'
        if sha(common)!=COMMON_SHA:
            raise RuntimeError('Reviewed actual shared process custodian changed; no silent rehash')
        # No module main execution or global mutation. Normal import only after whole-body pin.
        sys.dont_write_bytecode=True
        spec=importlib.util.spec_from_file_location('maintained_b1_lifecycle_commands',common)
        module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        if sha(root/'apps/Web/Files/Tests/Lifecycle/Program.cs')!=PROGRAM_SHA:
            raise RuntimeError('Same actual14 fixture Program changed')
        for path,expected in ROUTE_PINS.items():
            if sha(root/path)!=expected:
                raise RuntimeError('Preserved original01/02 route snapshot changed')
        env=os.environ.copy()
        for variable,directory in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget',
                'NUGET_HTTP_CACHE_PATH':'http','NUGET_PLUGINS_CACHE_PATH':'plugins','XDG_CACHE_HOME':'font-cache',
                'TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp','HAVEN_DATA_DIR':'fixture-data'}.items():
            target=output/directory; target.mkdir(exist_ok=True); env[variable]=str(target)
        env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_TELEMETRY_OPTOUT='1',
                   DOTNET_NOLOGO='1',MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1')
        commands=module.Commands(diagnostics,env,root)
        head=commands.run('git-head',['git','rev-parse','HEAD'],15).strip()
        if head!=args.expected_commit: raise RuntimeError('Checkout differs from actual requested commit')
        result['sourceCommit']=head
        commands.run('git-clean-before',['git','diff','--exit-code','HEAD','--'],15)
        commands.run('source-tree-pins',['git','ls-tree','-r','HEAD'],15)
        commands.run('native-submodule-pins',['git','submodule','status','--recursive','--',
                     'framework/CUI/vendor/Avalonia/external/XamlX','framework/CUI/vendor/Avalonia/external/Avalonia.DBus'],15)
        version=commands.run('dotnet-version',['dotnet','--version'],30).strip()
        if version!='10.0.401': raise RuntimeError('Actual SDK10.0.401 required')
        commands.run('dotnet-info',['dotnet','--info'],30)
        artifacts=output/'artifacts'; task=artifacts/'bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll'
        properties=['-p:SelfContained=false','-p:UseSharedCompilation=false','-p:AvaloniaBuildTasksLocation='+str(task),
                    '-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false','-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false',
                    '-p:CreateHardLinksForCopyLocalIfPossible=false','-p:CreateHardLinksForPublishFilesIfPossible=false']
        project=PROJECTS[args.suite]
        # Ordinary maintained owner graph restore and SDK compile; no generated-source/task substitute.
        commands.run('restore',['dotnet','restore',project,'--artifacts-path',str(artifacts),'-r','linux-x64',
                     '--configfile',str(root/'NuGet.Config'),'--disable-build-servers','-p:Configuration=Release',
                     '-m:1','-nodeReuse:false']+properties,600)
        commands.run('build',['dotnet','build',project,'--no-restore','-c','Release','--artifacts-path',str(artifacts),
                     '-r','linux-x64','--disable-build-servers','-m:1','-nodeReuse:false']+properties,1200)
        evaluated=module.parse_sdk(commands.run('target-properties',['dotnet','msbuild',project,
                     '-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true',
                     '-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false',
                     '-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation']+properties,30))
        module.write_json(diagnostics/'target-properties.json',evaluated)
        target=Path(evaluated['TargetPath']).resolve(); apphost=target.with_suffix('')
        if evaluated['TargetFramework']!='net10.0' or evaluated['RuntimeIdentifier']!='linux-x64' or evaluated['UseAppHost'].lower()!='true':
            raise RuntimeError('Actual framework/RID/apphost differs')
        if not target.is_relative_to(artifacts) or not target.is_file() or not apphost.is_file() or not os.access(apphost,os.X_OK):
            raise RuntimeError('Real isolated native entry DLL/apphost missing')
        inventory={'peCount':0,'portablePdbCount':0,'pdbCount':0,
                   'entryDLL':{'path':str(target.relative_to(artifacts)),'sha256':module.digest(target)},
                   'apphost':{'path':str(apphost.relative_to(artifacts)),'sha256':module.digest(apphost)}}
        for path in artifacts.rglob('*'):
            if path.is_file() and path.suffix in ('.dll','.pdb'):
                with path.open('rb') as stream: magic=stream.read(4)
                if path.suffix=='.dll' and magic[:2]==b'MZ': inventory['peCount']+=1
                elif path.suffix=='.pdb':
                    inventory['pdbCount']+=1; inventory['portablePdbCount']+=int(magic==b'BSJB')
        if not task.is_file(): raise RuntimeError('Actual maintained owner Avalonia task output absent')
        inventory['ownerBuildTask']={'sha256':module.digest(task),'bytes':task.stat().st_size}
        module.write_json(diagnostics/'pe-pdb-counts.json',inventory)
        result['nativeStarted']=True
        # Shared Commands preserves negative exit1 in commands.json and raises; no accepted-exit override.
        text=commands.run('native',[str(apphost),str(output/'fixture-data/lifecycle')],120)
        if args.suite!='lifecycle14':
            raise RuntimeError('Preserved negative control unexpectedly exited0; actual expected-red validator must reject it')
        passed=re.findall(r'^PASS (actual-[^\r\n]+)$',text,re.MULTILINE)
        summary='RESULT expected=14 executed=14 passed=14 failed=0; realOwnerGroupAuthority=BLOCKED browser=NOT_RUN'
        if len(passed)!=14 or len(set(passed))!=14 or set(passed)!=set(CASES) or re.search(r'^FAIL ',text,re.MULTILINE) or len(re.findall('^'+re.escape(summary)+'$',text,re.MULTILINE))!=1:
            raise RuntimeError('Actual unchanged14 names/count/qualified summary did not all pass')
        result.update(status='PASS',executed=14,passed=14,failed=0,realOwnerGroupAuthority='BLOCKED',browser='NOT_RUN')
    except Exception as error:
        result.update(status='FAIL',error=repr(error))
    finally:
        if commands:
            try:
                commands.run('git-clean-after',['git','diff','--exit-code','HEAD','--'],15)
                if commands.run('git-head-after',['git','rev-parse','HEAD'],15).strip()!=args.expected_commit:
                    raise RuntimeError('Actual source HEAD changed')
            except Exception as error:
                result.update(status='FAIL',sourceCustodyError=repr(error))
        # Small orchestration receipt only; source/data/native logs remain separate actual evidence.
        (diagnostics/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
    return 0 if result['status']=='PASS' else 1

if __name__=='__main__':
    raise SystemExit(main())
