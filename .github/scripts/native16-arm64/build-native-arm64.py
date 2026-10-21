#!/usr/bin/env python3
"""Native ARM runner only. Exact source baseline, normal references, fresh outputs."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,sys
root=Path.cwd();infra=Path(__file__).resolve().parent;out=Path(os.environ['RUNNER_TEMP'])/'9to1-native16-arm64-source';out.mkdir(exist_ok=False);evidence=root/'evidence';evidence.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();now=lambda:datetime.datetime.now(datetime.timezone.utc).isoformat()
identity=json.loads((infra/'native16-source-identity.json').read_text());actual_head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();subprocess.run(['git','merge-base','--is-ancestor',identity['source_commit'],actual_head],check=True)
for name,wanted in identity['changed_files_sha256'].items():
 if sha(root/name)!=wanted:raise SystemExit('Frozen Native16 source mismatch: '+name)
for item in json.loads((infra/'fork-extra-source-manifest.json').read_text())['files']:
 if sha(root/item['path'])!=item['sha256']:raise SystemExit('Frozen generated fork source mismatch: '+item['path'])
submodules=json.loads((infra/'fork-extra-source-manifest.json').read_text())['submodules']
for name,wanted in submodules.items():
 if subprocess.check_output(['git','-C',name,'rev-parse','HEAD'],text=True).strip()!=wanted:raise SystemExit('Wrong source submodule '+name)
 if subprocess.check_output(['git','-C',name,'status','--porcelain'],text=True).strip():raise SystemExit('Dirty source submodule '+name)
if subprocess.check_output(['uname','-m'],text=True).strip()!='aarch64':raise SystemExit('Native AArch64 runner required')
if subprocess.check_output(['dpkg','--print-architecture'],text=True).strip()!='arm64':raise SystemExit('Native arm64 Debian environment required')
receipt={'started_at':now(),'source_head':actual_head,'original_source_head':identity['source_commit'],'source_patch_sha256':identity['patch_sha256'],'native_host':'aarch64','stages':[],'status':'RUNNING','build_kind':'INTERNAL_NATIVE16_ARM_NATIVE_REBUILD','original_cloud_package_sha256':'a43bf4b7d28a049b9cadac18ccc303b08b2c3122ba99e5150ba85ce7dd623782','binary_identity_note':'New native build; not identical to locally cross-published package','normal_refs':True,'application_acceptance':'NOT RUN','laptop_acceptance':'NOT RUN'}
common=['-m:1','-nodeReuse:false','-p:BuildInParallel=false','-p:UseSharedCompilation=false'];hostart=out/'host-tool-artifacts';appart=out/'artifacts';pub=out/'publish';hostproj='framework/CUI/Build.Tasks/CakeOS.Cui.Build.Compiler.csproj';appproj='9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj'
def props(path):return ['-p:ArtifactsPath='+str(path),'-p:UseArtifactsOutput=true','-p:UseArtifactsIntermediateOutput=true','-p:IncludeProjectNameInArtifactsPaths=true']
def run(name,args):
 stage={'name':name,'command':args,'started_at':now()};receipt['stages'].append(stage);log=evidence/(name+'.log')
 with log.open('w') as f:res=subprocess.run(args,stdout=f,stderr=subprocess.STDOUT)
 stage.update(exit_code=res.returncode,ended_at=now(),log_sha256=sha(log));(evidence/'native-build-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
 if res.returncode:print(log.read_text()[-18000:]);raise RuntimeError(name+' failed '+str(res.returncode))
try:
 donorproj='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
 run('donor-task-restore',['dotnet','restore',donorproj,*common,*props(hostart)])
 run('donor-task-build',['dotnet','build',donorproj,'-c','Release','--no-restore',*common,*props(hostart)])
 donorquery=['dotnet','msbuild',donorproj,'-nologo','-getProperty:TargetPath','-p:Configuration=Release',*common,*props(hostart)]
 donorraw=subprocess.check_output(donorquery,text=True).strip();(evidence/'donor-task-target-path.txt').write_text(donorraw+'\n');donortask=Path(donorraw)
 if not donortask.is_absolute() or not donortask.is_file():raise RuntimeError('Actual same-source donor compiler output missing: '+donorraw)
 receipt['donor_host_compiler']={'path':str(donortask),'sha256':sha(donortask),'project':donorproj,'target_path_query':donorquery}
 run('host-tool-restore',['dotnet','restore',hostproj,*common,*props(hostart)])
 run('host-tool-build',['dotnet','build',hostproj,'-c','Release','--no-restore',*common,*props(hostart)])
 task=hostart/'bin/CakeOS.Cui.Build.Compiler/release/CakeOS.Cui.Build.Compiler.dll'
 if not task.is_file():raise RuntimeError('Actual same-source compiler output missing')
 receipt['host_compiler']={'path':str(task),'sha256':sha(task),'native_host':'arm64'}
 tool=['-p:CuiUsePrebuiltCompiler=true','-p:CuiCompilerTaskAssembly='+str(task),'-p:AvaloniaBuildTasksLocation='+str(donortask)]
 run('app-restore',['dotnet','restore',appproj,'-r','linux-arm64','-p:SelfContained=true','-p:UseAppHost=true',*common,*props(appart),*tool])
 run('app-publish',['dotnet','publish',appproj,'-c','Release','-f','net10.0','-r','linux-arm64','--self-contained','true','--no-restore',*common,'-p:UseAppHost=true','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false','-p:ProductDisplayName=9to1 Preview (Incomplete)','-o',str(pub),*props(appart),*tool])
 shape=subprocess.run([sys.executable,str(infra/'verify-arm64-publish.READONLY.py'),str(pub)],capture_output=True,text=True);(evidence/'arm64-publish-shape.json').write_text(shape.stdout)
 if shape.returncode:raise RuntimeError('Actual native publish shape failed')
 inspected=json.loads(shape.stdout)
 provenance={**receipt,'rid':'linux-arm64','self_contained':True,'publish_exit_code':0,'publish_directory':str(pub),'published_tree_manifest_sha256':inspected['tree_manifest_sha256'],'published_file_count':inspected['files'],'published_bytes':inspected['bytes'],'native_dependency_floor':{'glibc':inspected['effective_max_glibc'],'glibcxx':inspected['effective_max_glibcxx']},'build_kind':'INTERNAL_NATIVE16_ARM_NATIVE_REBUILD','original_cloud_source_tree':'Native16WIP72','corresponding_source':'Exact source commit plus pinned submodule contents included as separate source artifact','AI_browser_features':'NOT PROMISED; no engine/model included'}
 (evidence/'source-and-build.json').write_text(json.dumps(provenance,indent=2)+'\n')
 version='0.1.0~internal.native16.20261021.'+os.environ['GITHUB_RUN_ID']+'.'+os.environ['GITHUB_RUN_ATTEMPT']
 run('package',['python3',str(infra/'package-arm64-deb.ROOT-ONLY.py'),'--publish',str(pub),'--output',str(out/'package'),'--version',version,'--maintainer','CroakyJake12 <124530029+CroakyJake12@users.noreply.github.com>','--build-provenance',str(evidence/'source-and-build.json'),'--readme',str(infra/'PREVIEW-README.md'),'--notices',str(infra/'notices'),'--source-licence',str(infra/'LICENSE')])
 package=json.loads((out/'package/package-receipt.json').read_text());receipt['package']=package;receipt['status']='PASS_BUILD_AND_PACKAGE_ONLY_NOT_NATIVE_APP_ACCEPTED'
 with open(os.environ['GITHUB_ENV'],'a') as f:f.write('PREVIEW_DEB='+package['package']+'\nPREVIEW_PACKAGE_DIR='+str(out/'package')+'\n')
except BaseException as e:
 receipt['status']='FAIL';receipt['error']=repr(e);raise
finally:
 receipt['ended_at']=now();(evidence/'native-build-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
