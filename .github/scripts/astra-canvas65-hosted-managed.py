"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import importlib.util
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil,time
def xunit_discovery_name(raw):
 if not isinstance(raw,str):raise ValueError('Actual test display name absent')
 escaped=raw.replace('\r','\\r').replace('\n','\\n').replace('\t','\\t')
 encoded=escaped.encode('utf-16-le',errors='strict')
 if len(encoded)>447*2:
  try:escaped=encoded[:444*2].decode('utf-16-le',errors='strict')+'···'
  except UnicodeDecodeError:raise ValueError('Unsupported split-surrogate discovery truncation')
 return escaped
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/canvas65-managed';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 if name=='native-ui' and 'sdkRecord' in globals():
  recordDir=out/'native-session-drain'/name;recordDir.mkdir(parents=True,exist_ok=False)
  (recordDir/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
  os.environ['ASTRA_NATIVE_SESSION_DRAIN_RECORDS']=str(recordDir)
  return nativeObserver.run(args,name,root,out,digest,compiledTargets[name],[(library,digest(library))],sdk_record=sdkRecord)
 with (out/(name+'.log')).open('wb') as log:
  if len(args)>1 and (args[0]=='dotnet' or args[0]==os.environ.get('ASTRA_CANVAS_OFFICIAL_SDK_ROOT','')+'/dotnet') and (args[1]=='test' or name=='files'):
   recordDir=out/'native-session-drain'/name;recordDir.mkdir(parents=True,exist_ok=False)
   (recordDir/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
   spec=importlib.util.spec_from_file_location('canvas_owned_session',root/'.github/scripts/astra-original-native-session-drain.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
   process=subprocess.Popen(args,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);session=None
   try:
    session=module.OriginalSession(process,recordDir)
    deadline=time.monotonic()+1200
    while process.poll() is None:
     session.observe()
     if time.monotonic()>deadline:raise TimeoutError('Canvas owned command deadline exceeded')
     time.sleep(.01)
    code=process.wait()
   finally:
    if session is not None:session.drain()
   seal=json.loads((recordDir/'expected-managed-launch.json').read_text())
   if not seal.get('drained'):raise RuntimeError('managed original sampled session cleanup not proved')
   return code
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-canvas65-cut.json':raise SystemExit('unexpected self-manifest path')
manifest=root/a.manifest
if not manifest.is_file() or digest(manifest)!=a.manifest_sha:raise SystemExit('manifest pin mismatch')
cut=json.loads(manifest.read_text());files=cut.get('files');links=cut.get('gitlinks');materialized=cut.get('materializedGitlinks')
if not isinstance(files,list) or not files or not isinstance(links,list) or not isinstance(materialized,list):raise SystemExit('cut schema missing files/gitlinks/materializedGitlinks')
requiredMaterialized={'framework/CUI/vendor/Avalonia/external/XamlX': '009d4815470cf4bf71d1adbb633a5d81dcb2bb52', 'framework/CUI/vendor/Avalonia/external/Avalonia.DBus': '864a05282841bf04006890f04d11d60d1a046aa9', '9to1 Workspace/Terminal/Source/libvterm': '934bc2fbf21800ac3458a499df8820ca5fb45fd3'}
def verify():
 # One batched immutable tree read, rather than9075 per-file Git subprocesses.
 actualFiles={};actualLinks={}
 for item in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
  if not item:continue
  header,path=item.split(b'\t',1);mode,kind,oid=header.decode().split();path=path.decode()
  if mode=='160000':actualLinks[path]=oid
  elif mode in ('100644','100755') and kind=='blob':actualFiles[path]=(mode,oid)
  else:raise SystemExit('unexpected tracked tree mode/type: '+path)
 # The exact externallySHA-pinned manifest is excluded from its own regular-file map.
 if a.manifest not in actualFiles:raise SystemExit('self-manifest not tracked in actual HEAD')
 del actualFiles[a.manifest]
 expectedFiles={}
 for f in files:
  relative=pathlib.PurePosixPath(f['path']);name=str(relative)
  if relative.is_absolute() or '..' in relative.parts or name in expectedFiles or name==a.manifest:raise SystemExit('unsafe/duplicate/self cut path')
  mode=f.get('mode',f.get('gitMode'))
  if 'mode' in f and 'gitMode' in f and f['mode']!=f['gitMode']:raise SystemExit('conflicting cut modes')
  if mode not in ('100644','100755'):raise SystemExit('invalid regularfile mode')
  path=root/name
  if path.is_symlink() or not path.is_file():raise SystemExit('source file missing/notregular: '+name)
  size=path.stat().st_size;sha256=hashlib.sha256();blob=hashlib.sha1();blob.update(('blob '+str(size)+'\0').encode());count=0
  with path.open('rb') as stream:
   while data:=stream.read(1024*1024):sha256.update(data);blob.update(data);count+=len(data)
  if count!=size or count!=f.get('bytes',count) or sha256.hexdigest()!=f['sha256']:raise SystemExit('source SHA/size mismatch: '+name)
  # Verify a supplied root blobOID, and always bind actual filebytes to the GitHEAD blob.
  supplied=f.get('object')
  if supplied is not None and (not re.fullmatch('[0-9a-f]{40}',supplied) or supplied!=blob.hexdigest()):raise SystemExit('source blobOID mismatch: '+name)
  expectedFiles[name]=(mode,blob.hexdigest())
 if actualFiles!=expectedFiles:raise SystemExit('complete regularfile path/mode/blobHEAD map mismatch')
 if digest(manifest)!=a.manifest_sha:raise SystemExit('self-manifest changed during lifetime')
 expectedLinks={}
 for link in links:
  relative=pathlib.PurePosixPath(link['path']);pin=link['commit']
  if relative.is_absolute() or '..' in relative.parts or str(relative) in expectedLinks or not re.fullmatch('[0-9a-f]{40}',pin):raise SystemExit('unsafe/duplicate/invalid gitlink pin')
  expectedLinks[str(relative)]=pin
 if len(expectedLinks)!=22 or actualLinks!=expectedLinks:raise SystemExit('complete22 tracked gitlink pin set mismatch')
 actualMaterialized={}
 for link in materialized:
  if link['path'] in actualMaterialized:raise SystemExit('duplicate materialized gitlink')
  actualMaterialized[link['path']]=link['commit']
 if actualMaterialized!=requiredMaterialized:raise SystemExit('materialized subset must be exact3 reviewed source dependencies')
 for path,pin in actualMaterialized.items():
  if expectedLinks.get(path)!=pin:raise SystemExit('materialized pin not in complete tracked set')
  actualTop=pathlib.Path(subprocess.check_output(['git','-C',path,'rev-parse','--show-toplevel'],text=True).strip()).resolve()
  if actualTop!=(root/path).resolve() or subprocess.check_output(['git','-C',path,'rev-parse','HEAD'],text=True).strip()!=pin:raise SystemExit('actual materialized dependency checkout mismatch')
  if subprocess.check_output(['git','-C',path,'status','--porcelain','--untracked-files=no'],text=True).strip():raise SystemExit('tracked materialized dependency dirt')
 # Independent existing repository check validates all three trackedclean pins/header inputs;
 # --check never updates clones or rewrites tracked source.
 if subprocess.run(['bash','9to1 Workspace/shared/eng/prepare-cui-source.sh','--check'],check=False).returncode:raise SystemExit('independent clean pinned source check failed')
verify()
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','linux-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
artifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
checks=[('files', '9to1 Workspace/Files/CUI.Tests/HavenOS.Files.CUI.Tests.csproj', None), ('core', '9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj', 'FullyQualifiedName~Haven.Core.Tests.CanvasSchema2Tests|FullyQualifiedName~Haven.Core.Tests.CanvasLayerDonorTransactionTests'), ('canvas', '9to1 Workspace/Canvas/Tests/HavenOS.Canvas.Tests.csproj', ''), ('native-ui', '9to1 Workspace/Canvas/NativeUI.Tests/HavenOS.Canvas.NativeUI.Tests.csproj', ''), ('home', '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', ''), ('desktop', '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj', 'FullyQualifiedName~Haven.Desktop.Tests.FilesCompatibilityPackageContentSourceTests|FullyQualifiedName~Haven.Desktop.Tests.FilesNativeChildFolderReadTests|FullyQualifiedName~Haven.Desktop.Tests.NativeFilesMediaOriginalStoreTests|FullyQualifiedName~Haven.Desktop.Tests.FilesOriginalCanonicalReadTests|FullyQualifiedName~Haven.Desktop.Tests.PictureFilesCopyResourceAdmissionTests|FullyQualifiedName~Haven.Desktop.Tests.FilesOriginalOwnerCompositionTests|FullyQualifiedName~Haven.Desktop.Tests.SpacesConversationRefreshTests'), ('os', '9to1 OS/tests/NineToOne.Os.Shell.Tests/NineToOne.Os.Shell.Tests.csproj', 'FullyQualifiedName~NineToOne.Os.Shell.Tests.CompatibilityPackageInspectorTests')]
# Root pins this exact evidence file in the cut; verify names exist in actual immutable source.
provenance=json.loads((root/'.github/validation/astra-canvas65-test-classes.json').read_text())
cutPaths={f['path']:f['sha256'] for f in files}
def exact_module(name,relative):
 path=root/relative
 if path.is_symlink() or digest(path)!=cutPaths[relative]:raise SystemExit('Actual caller module differs from source cut')
 spec=importlib.util.spec_from_file_location(name,path);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
sdkPreflight=exact_module('canvas_sdk_preflight','.github/scripts/astra-canvas-sdk-provider-preflight.py')
sdkRecord,sdkAdmission=sdkPreflight.capture(root,out,cutPaths,command,digest)
nativeObserver=exact_module('canvas_native_original','.github/scripts/astra-canvas-owned-native-maps.py')
command([sdkRecord['path'],'--info'],'toolchain');command([sdkRecord['path'],'workload','list'],'workloads');verify()
for entry in provenance['classes']:
 path=root/entry['path']
 if entry['path'] not in cutPaths or cutPaths[entry['path']]!=entry['sha256'] or digest(path)!=entry['sha256']:raise SystemExit('owning test source not pinned')
 if not re.search(r'\bclass\s+'+re.escape(entry['class'])+r'\b',path.read_text()):raise SystemExit('owning test class absent')
for dependency in provenance['dependencyPins']:
 if cutPaths.get(dependency['path'])!=dependency['sha256'] or digest(root/dependency['path'])!=dependency['sha256']:raise SystemExit('Models18 existing dependency pin mismatch')
donor='9to1 Workspace/Canvas/Source/Rnote'
if subprocess.check_output(['git','-C',donor,'rev-parse','HEAD'],text=True).strip()!='53981d09b0cdcc7f12af9ee579c4c4bfb95d6821':raise SystemExit('Rnote donor pin mismatch')
if subprocess.check_output(['git','-C',donor,'status','--porcelain','--untracked-files=no']):raise SystemExit('Rnote tracked donor source dirty')
for tool in ('rustc','cargo'):
 version=subprocess.check_output([tool,'--version'],text=True);(out/(tool+'-version.txt')).write_text(version)
 if not version.startswith(tool+' 1.98.1 '):raise SystemExit('unexpected actual Rust toolchain')

lock=root/'9to1 Workspace/Canvas/rnote-poc/Cargo.lock';lockSha=digest(lock)
os.environ['CARGO_TARGET_DIR']=str(pathlib.Path(os.environ['RUNNER_TEMP'])/'astra-canvas65-fresh-rust-target')
if pathlib.Path(os.environ['CARGO_TARGET_DIR']).exists():raise SystemExit('fresh Rust target already exists')
cargo=['cargo','--locked','--manifest-path','9to1 Workspace/Canvas/rnote-poc/Cargo.toml']
# --locked is per-command; no lock regeneration or old native binary substitution.
for command_name,args in [('cargo-tree',['cargo','tree',*cargo[1:]]),('cargo-build',['cargo','build','--release','--lib',*cargo[1:]]),('cargo-test',['cargo','test','--release','--all-targets',*cargo[1:],'--','--nocapture'])]:
 code=command(args,command_name);verify()
 if code or digest(lock)!=lockSha:raise SystemExit(code or 'Cargo.lock changed')
text=(out/'cargo-tree.log').read_text()
if re.search(r'(^|\s)(gtk4|libadwaita)\s',text):raise SystemExit('headless donor unexpectedly depends on GTK/adwaita')
testlog=(out/'cargo-test.log').read_text()
summary=re.findall(r'test result: ok\. (\d+) passed; (\d+) failed; (\d+) ignored;',testlog)
if not summary or sum(int(x[0]) for x in summary)<=0 or any(int(x[1]) or int(x[2]) for x in summary):raise SystemExit('Rust no positive zero-ignored result')
for witness in ['quick_hit_uses_real_hitboxes_layer_first_order_and_refuses_ambiguous_chronology_read_only','genuine_split_materializes_exact_mixed_curve_fragments_and_original_pressure_style_identity','quick_native_boundary_uses_actual_engine_render_order_and_resets_refusal_outputs','split_native_boundary_returns_actual_two_fragment_candidate_preserves_original_and_resets_all_refusals']:
 if len(re.findall(r'^test .*::'+re.escape(witness)+r' \.\.\. ok$',testlog,re.M))!=1:raise SystemExit('missing unique Rust witness '+witness)
for requiredCargoTest in ['tests::controlled_donor_imports_editable_xopp_and_round_trips_rendering', 'tests::selected_native_export_preserves_keys_and_original_paths_without_unselected_entities', 'tests::keyed_mutations_use_donor_geometry_preserve_survivors_and_reject_without_side_effects', 'tests::pressure_is_clamped_but_tilt_is_preserved_at_hui_boundary', 'tests::incremental_stroke_boundary_enforces_event_lifecycle', 'tests::completed_stroke_participates_in_undo_redo_history', 'tests::renderer_neutral_frame_carries_export_coordinate_metadata', 'tests::eraser_trashes_stroke_and_history_restores_state', 'tests::headless_engine_draws_exports_saves_and_reloads', 'tests::pen_styles_default_to_engine_values_and_round_trip', 'tests::rnote_tools_camera_and_atomic_reopen_preserve_world_coordinates', 'tests::quick_hit_uses_real_hitboxes_layer_first_order_and_refuses_ambiguous_chronology_read_only', 'tests::genuine_split_materializes_exact_mixed_curve_fragments_and_original_pressure_style_identity', 'tests::genuine_user_layer_rank_changes_native_render_order_and_survives_reopen', 'ffi::tests::native_bridge_draws_renders_saves_and_reloads', 'ffi::tests::native_bridge_rejects_bad_handles_arguments_and_mid_stroke_changes', 'ffi::tests::pen_style_and_eraser_options_apply_and_validate', 'ffi::quick_tests::quick_native_boundary_uses_actual_engine_render_order_and_resets_refusal_outputs', 'ffi::split_tests::split_native_boundary_returns_actual_two_fragment_candidate_preserves_original_and_resets_all_refusals', 'native_selector_tests::genuine_selector_single_and_rectangle_are_read_only_with_exact_original_keys', 'ffi::selector_boundary_tests::genuine_selector_ffi_returns_owned_exact_keys_and_resets_rejected_output', 'tests::native_save_preserves_exact_live_brush_geometry_pressure_width_and_color']:
 if len(re.findall(r'^test '+re.escape(requiredCargoTest)+r' \.\.\. ok$',testlog,re.M))!=1:raise SystemExit('missing exact preserved/new actual Cargo Passed identity '+requiredCargoTest)
library=pathlib.Path(os.environ['CARGO_TARGET_DIR'])/'release/libcakeos_canvas_rnote_poc.so'
if not library.is_file():raise SystemExit('fresh native library absent')
librarySha=digest(library)
for tool,args in [('readelf-dynamic',['readelf','-d',str(library)]),('readelf-symbols',['readelf','--dyn-syms','--wide',str(library)]),('native-needed',['ldd',str(library)])]:
 if command(args,tool):raise SystemExit('native inspection failed')
os.environ['LD_LIBRARY_PATH']=str(library.parent)+(':'+os.environ['LD_LIBRARY_PATH'] if os.environ.get('LD_LIBRARY_PATH') else '')
probe=out/'native-load-probe.py';probe.write_text("import ctypes,json,pathlib,hashlib,os\np=pathlib.Path(os.environ['ASTRA_FRESH_CANVAS_LIBRARY']);b=ctypes.CDLL(str(p))\nversions={}\nfor n,e in [('cake_canvas_abi_version',3),('cake_canvas_selection_api_version',1),('cake_canvas_stroke_mutation_api_version',1),('cake_canvas_quick_erase_api_version',1),('cake_canvas_split_erase_api_version',1),('cake_canvas_user_layer_rank_api_version',1),('cake_canvas_visible_keys_render_api_version',1),('cake_canvas_selector_api_version',1)]:\n f=getattr(b,n);f.restype=ctypes.c_uint32;versions[n]=f();assert versions[n]==e\n[getattr(b,n) for n in ['cake_canvas_read_user_layer_ranks','cake_canvas_assign_user_layer_ranks','cake_canvas_render_visible_keys','cake_canvas_preview_selection']]\nmaps=pathlib.Path('/proc/self/maps').read_text();assert str(p) in maps\nprint(json.dumps({'pid':os.getpid(),'library':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'versions':versions,'actualMaps':maps}))\n")
os.environ['ASTRA_FRESH_CANVAS_LIBRARY']=str(library)
if command([sys.executable,str(probe)],'native-loaded-identity'):raise SystemExit('fresh native ABI load failed')
if digest(library)!=librarySha:raise SystemExit('native bytes changed')
nativeOut=out/'native';nativeOut.mkdir();shutil.copyfile(library,nativeOut/library.name)
if digest(nativeOut/library.name)!=librarySha:raise SystemExit('retained native copy mismatch')
restoreSpec=importlib.util.spec_from_file_location('restore_evidence',root/'.github/scripts/astra-canvas65-restore-evidence.py');restore=importlib.util.module_from_spec(restoreSpec);restoreSpec.loader.exec_module(restore)
restoreBefore={}
entries=[(name,project) for name,project,_ in checks]+[('host','9to1 Workspace/Canvas/Host/HavenOS.Canvas.Host.csproj')]
for name,project in entries:
 code=command(['dotnet','restore',project,*base[2:],'-p:Configuration=Release','-p:TargetFramework=net10.0','-p:EnableWindowsTargeting=true'],name+'-restore');verify()
 if code:raise SystemExit(code)
hostArtifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-host-build-tasks'),'-p:IncludeProjectNameInArtifactsPaths=true']
hostTaskProject='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
hostTaskRestoreProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*hostArtifactsProps]
code=command(['dotnet','restore',hostTaskProject,'--disable-build-servers','-m:1','-nr:false',*hostTaskRestoreProps],'avalonia-build-tasks-restore');verify()
if code:raise SystemExit(code)
# These six netstandard assets are written LAST; no subsequent build may restore.
toolProjects=['framework/CUI/vendor/Avalonia/src/tools/DevAnalyzers/DevAnalyzers.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CSharp/Avalonia.Analyzers.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CodeFixes.CSharp/Avalonia.Analyzers.CodeFixes.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.VisualBasic/Avalonia.Analyzers.VisualBasic.csproj', 'framework/CUI/vendor/Avalonia/src/tools/DevGenerators/DevGenerators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.DBus.Generators/Avalonia.DBus.Generators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Generators/Avalonia.Generators.csproj']
for tool in toolProjects:
 code=command(['dotnet','restore',tool,*base[2:],'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true'],'tool-restore-'+pathlib.Path(tool).stem);verify()
 if code:raise SystemExit(code)
 toolQuery=subprocess.run(['dotnet','msbuild',tool,'-nologo','-m:1','-nr:false','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*artifactsProps,'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true','-getProperty:TargetFramework,MSBuildProjectFullPath,ProjectAssetsFile'],capture_output=True,text=True)
 (out/('tool-framework-'+pathlib.Path(tool).stem+'.stdout')).write_text(toolQuery.stdout);(out/('tool-framework-'+pathlib.Path(tool).stem+'.stderr')).write_text(toolQuery.stderr)
 if toolQuery.returncode:raise SystemExit(toolQuery.returncode)
 toolActual=json.loads(toolQuery.stdout)['Properties'];assetPath=pathlib.Path(toolActual['ProjectAssetsFile']);assetPath=assetPath if assetPath.is_absolute() else (root/tool).parent/assetPath
 if toolActual['TargetFramework']!='netstandard2.0' or pathlib.Path(toolActual['MSBuildProjectFullPath']).resolve()!=(root/tool).resolve() or not assetPath.resolve().is_relative_to(root) or not assetPath.is_file():raise SystemExit('actual restored analyzer framework/path mismatch')
 toolAssets=json.loads(assetPath.read_text())
 if not any(key.split('/')[0]=='netstandard2.0' for key in toolAssets['targets']):raise SystemExit('actual analyzer assets missing declared netstandard2.0')
taskProject=hostTaskProject
toolProjects.append(taskProject)
for name,project in entries:
 restoreBefore[name]=restore.snapshot_restore(root,project,toolProjects,bootstrap=True)
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore[name],indent=2)+'\n')
 for item in restoreBefore[name]['projects']:
  sourceProject=item['path'];effectiveFramework=item['effectiveFramework'];key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource,MicroComIdl','-getProperty:TargetPath,MSBuildProjectExtensionsPath,MicroComGeneratorMSBuildDll,UseLocalMicroComBuild']
  if item.get('hostContext'):
   args=[v for v in args if not v.startswith(('-p:RuntimeIdentifier=','-p:RuntimeIdentifiers=','-p:SelfContained=','-p:ArtifactsPath='))]+['-p:ArtifactsPath='+str(root/'artifacts/canvas65-host-build-tasks')]
  key+=('-host' if item.get('hostContext') else '-consumer')
  if command(args,'evaluated-'+key):raise SystemExit('actual evaluated source/generator graph failed')

# Bind actual evaluated source/generator paths to pinned source, exact materialized donors,
# or completely hashed restored package/SDK payloads; unknown physical input denies.
sdkRoot=pathlib.Path(shutil.which('dotnet')).resolve().parent
sdkFiles={}
for prefix in ('sdk','packs'):
 for file in sorted((sdkRoot/prefix).rglob('*')):
  if file.is_symlink():raise SystemExit('SDK input symlink requires explicit admission')
  if file.is_file():sdkFiles[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
(out/'sdk-input-pins.json').write_text(json.dumps(sdkFiles,indent=2)+'\n')
admitted={str((root/x['path']).resolve()):x['sha256'] for x in files}
for graph in restoreBefore.values():
 for package in graph['packages'].values():
  for file in package['files']:admitted[str((pathlib.Path(package['root'])/file['path']).resolve())]=file['sha256']
for file,evidence in sdkFiles.items():admitted[file]=evidence['sha256']
for donorPath in list(requiredMaterialized)+[donor]:
 for record in subprocess.check_output(['git','-C',donorPath,'ls-files','-z']).split(b'\0'):
  if record:
   file=root/donorPath/record.decode()
   if file.is_file() and not file.is_symlink():admitted[str(file.resolve())]=digest(file)
for log in out.glob('evaluated-*.log'):
 data=json.loads(log.read_text())
 for items in data.get('Items',{}).values():
  for item in items:
   value=item.get('FullPath')
   if not value:raise SystemExit('evaluated input missing full path')
   file=pathlib.Path(value).resolve()
   if not file.is_file() or admitted.get(str(file))!=digest(file):raise SystemExit('unknown or changed evaluated compiler/generator input: '+str(file))
(out/'evaluated-input-admission.json').write_text(json.dumps({'admittedPaths':len(admitted),'actualQueries':len(list(out.glob('evaluated-*.log'))),'qualification':'Actual physical inputs hashed; no publisher certificate trust inferred'},indent=2)+'\n')

# Source-built genuine build-host task; its ProjectReference removes application RID.
taskProject='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
taskProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*hostArtifactsProps]
code=command(['dotnet','build',taskProject,'--no-restore','-c','Release','-f','netstandard2.0','--disable-build-servers','-m:1','-nr:false',*taskProps],'avalonia-build-tasks-build');verify()
if code:raise SystemExit(code)
taskQuery=subprocess.run(['dotnet','msbuild',taskProject,'-nologo','-m:1','-nr:false',*taskProps,'-getProperty:TargetPath,OutputPath,TargetFramework,Configuration'],capture_output=True,text=True)
(out/'avalonia-build-tasks-target.stdout').write_text(taskQuery.stdout);(out/'avalonia-build-tasks-target.stderr').write_text(taskQuery.stderr)
if taskQuery.returncode:raise SystemExit(taskQuery.returncode)
taskEvaluated=json.loads(taskQuery.stdout)['Properties'];taskTarget=pathlib.Path(taskEvaluated['TargetPath']).resolve()
taskOutput=pathlib.Path(taskEvaluated['OutputPath']);taskOutput=taskOutput if taskOutput.is_absolute() else (root/taskProject).parent/taskOutput;taskOutput=taskOutput.resolve()
if taskEvaluated['TargetFramework']!='netstandard2.0' or taskEvaluated['Configuration']!='Release' or not taskOutput.is_relative_to(root) or not taskTarget.is_relative_to(taskOutput) or not taskTarget.is_file():raise SystemExit('invalid actual source-built host task output')
def task_snapshot():
 result=[]
 for file in sorted(taskTarget.parent.rglob('*')):
  if file.is_symlink():raise SystemExit('build task output symlink')
  if file.is_file():result.append({'path':str(file.relative_to(root)),'bytes':file.stat().st_size,'sha256':digest(file)})
 return result
taskBefore=task_snapshot();(out/'avalonia-build-tasks-compiled-before.json').write_text(json.dumps({'project':taskProject,'target':str(taskTarget.relative_to(root)),'files':taskBefore},indent=2)+'\n')
retainedTasks=out/'compiled'/'avalonia-build-tasks';retainedTasks.mkdir(parents=True,exist_ok=True)
for file in sorted(taskTarget.parent.rglob('*')):
 if file.is_file():
  destination=retainedTasks/file.relative_to(taskTarget.parent);destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,destination)
  if digest(destination)!=digest(file):raise SystemExit('retained actual build task closure mismatch')
actualTaskProperty='-p:AvaloniaBuildTasksLocation='+str(taskTarget)
base.append(actualTaskProperty);artifactsProps.append(actualTaskProperty);os.environ['ASTRA_ACTUAL_AVALONIA_BUILD_TASKS']=str(taskTarget)
def assert_task_unchanged():
 after=task_snapshot();(out/'avalonia-build-tasks-compiled-after.json').write_text(json.dumps({'project':taskProject,'target':str(taskTarget.relative_to(root)),'files':after},indent=2)+'\n')
 if after!=taskBefore:raise SystemExit('actual source-built task closure changed')

# Admit the fresh host output only after bootstrap restored/input admission completed.
for name,project in entries:
 afterTaskRestore=restore.snapshot_restore(root,project,toolProjects)
 (out/(name+'-restore-after-task-build.json')).write_text(json.dumps(afterTaskRestore,indent=2)+'\n')
 if afterTaskRestore!=restoreBefore[name]:raise SystemExit('restored graph changed during isolated host compilation')

compiledTargets={}
def assert_compiled_target_unchanged(name):
 target,closure=compiledTargets[name]
 current=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('compiled dependency output became symlink')
  if path.is_file():current.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=closure:raise SystemExit('compiled entire pinned output closure changed during execution')
 assert_task_unchanged()
def build_and_pin(name,project):
 code=command(['dotnet','build',project,*base,'-f','net10.0','-p:EnableWindowsTargeting=true','--no-restore','-bl:'+str(out/(name+'-build.binlog'))],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']+[actualTaskProperty]
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,RuntimeIdentifier,Configuration,OutputPath,AvaloniaBuildTasksLocation'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 actualProps=json.loads(query.stdout)['Properties'];target=pathlib.Path(actualProps['TargetPath']).resolve()
 if pathlib.Path(actualProps['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('consumer UsingTask property differs from pinned source-built task')
 assert_task_unchanged()
 outputPath=pathlib.Path(actualProps['OutputPath']);outputPath=outputPath if outputPath.is_absolute() else (root/project).parent/outputPath;outputPath=outputPath.resolve()
 if actualProps['RuntimeIdentifier']!='linux-x64' or actualProps['Configuration']!='Release' or not target.is_relative_to(outputPath) or not target.is_relative_to(root.resolve()) or not target.is_file() or target.suffix!='.dll':raise SystemExit('missing/unexpected actual Release Linux TargetPath/OutputPath')
 closure=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('unexpected compiled output symlink')
  if path.is_file():closure.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 (out/(name+'-compiled.json')).write_text(json.dumps({'target':str(target.relative_to(root)),'targetSha256':digest(target),'files':closure},indent=2)+'\n')
 retained=out/'compiled'/name;retained.mkdir(parents=True,exist_ok=True)
 for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
  candidate=target.with_name(target.stem+extension)
  if candidate.is_file():shutil.copyfile(candidate,retained/candidate.name)
 compiledTargets[name]=(target,closure)
 return target
results=[]
def bind_actual_adapter(name,project):
 restored=restore.snapshot_restore(root,project,toolProjects)
 adapter=restored['packages'].get('xunit.runner.visualstudio/3.1.5')
 if adapter is None or adapter['physicalArchiveSha512']!='b/tvN9kXtUd3wSSYbC80Ic6y/dYlYbNSvatYiv71iozPhXqM4EaOuHujkIJh85b+wY6dgf25k9ENSy8jN6mlvQ==':raise ValueError('Actual restored discovery adapter differs from official source-bound3.1.5 package')
 target,closure=compiledTargets[name];package_hashes={row['sha256'] for row in adapter['files'] if row['path'].endswith('.dll')}
 copied=[row for row in closure if pathlib.Path(row['path']).name.startswith('xunit.runner.visualstudio.') and row['path'].endswith('.dll')]
 if not copied or any(row['sha256'] not in package_hashes for row in copied):raise ValueError('Actual compiled discovery adapter not copied from official restored package')
 config=[]
 for row in closure:
  if row['path'].endswith(('xunit.runner.json','.runsettings')):
   matches=[relative for relative,value in cutPaths.items() if value==row['sha256']]
   if not matches:raise ValueError('Unknown compiled discovery configuration not exact source cut')
   config.append({'output':row,'sourcePaths':matches})
 for key in ('VSTEST_TESTADAPTER_PATH','VSTEST_RUNSETTINGS'):
  if os.environ.get(key):raise ValueError('Ambient discovery adapter/settings override forbidden')
 return {'sourceCommit':'1b188a7b0a069d7fc94ae3c0b251f1302b602b63','package':adapter,'actualCopiedAdapter':copied,'actualCopiedConfiguration':config,'runsettingsCLI':None,'qualification':'Source-bound discovery normalization; non-preenumerated dynamic theory/set mismatch and collisions fail closed, no guessed counts.'}
for name,project,filter_value in checks:
 target=build_and_pin(name,project)
 if name=='files':
  code=command(['dotnet',str(target)],name);assert_compiled_target_unchanged(name);verify()
  marker='Files CUI domain contract checks passed, including actual interprocess lease/CAS/cancellation/recovery.'
  if code or (out/(name+'.log')).read_text().splitlines().count(marker)!=1:raise SystemExit(code or 'missing Files owning completion')
  results.append({'suite':name,'exit':code,'completion':marker,'qualification':'Actual owning console no TRX count invented'})
  continue
 args=['dotnet','test',project,*base,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(out/'trx')]
 if filter_value:args+=['--filter',filter_value]
 args+=['-f','net10.0','-p:EnableWindowsTargeting=true','--no-build','--no-restore']
 adapterBefore=bind_actual_adapter(name,project);(out/(name+'-adapter-before.json')).write_text(json.dumps(adapterBefore,indent=2)+'\n')
 discoveryArgs=[*args,'--list-tests']
 discoveryCode=command(discoveryArgs,name+'-discovery');assert_compiled_target_unchanged(name);verify()
 if discoveryCode:raise SystemExit(discoveryCode)
 discoveryText=(out/(name+'-discovery.log')).read_text()
 heading='The following Tests are available:'
 if discoveryText.splitlines().count(heading)!=1:raise SystemExit('Unique actual VSTest discovered-case heading absent')
 discoveryLines=discoveryText.split(heading,1)[1].splitlines()
 discovered=[]
 for line in discoveryLines:
  if not line:continue
  if not line.startswith('    ') or not line[4:]:raise SystemExit('Unknown discovery diagnostic or malformed case line')
  discovered.append(line[4:])
 if not discovered or len(discovered)!=len(set(discovered)):raise SystemExit('Actual discovered case inventory empty or duplicate')
 declared=json.loads((root/'.github/validation/astra-canvas49-owning-case-declarations.json').read_text())
 projectPrefix=(root/project).parent.relative_to(root).as_posix()+'/'
 for sourceFile in declared['wholeSelectedTestFiles']:
  if not sourceFile['path'].startswith(projectPrefix):continue
  if sourceFile['sourceSha256']!=cutPaths.get(sourceFile['path']):raise SystemExit('Declared new owning method source differs from compiled source cut')
  for method in sourceFile['declaredRequiredMethods']:
   prefix=method['class']+'.'+method['method']
   if not any(case==prefix or case.startswith(prefix+'(') for case in discovered):raise SystemExit('Actual discovery missing required whole-source owning method: '+prefix)
 (out/(name+'-discovered-cases.json')).write_text(json.dumps({'project':project,'argv':discoveryArgs,'actualCaseNames':discovered,'qualification':'Actual compiled SDK discovery, not lexical source count; every discovered case must subsequently Pass.'},indent=2)+'\n')
 code=command(args,name);assert_compiled_target_unchanged(name);verify()
 adapterAfter=bind_actual_adapter(name,project);(out/(name+'-adapter-after.json')).write_text(json.dumps(adapterAfter,indent=2)+'\n')
 if adapterBefore!=adapterAfter:raise ValueError('Actual restored/copied adapter/configuration changed across discovery/execution')
 record={'suite':name,'argv':args,'processExit':code,'counts':None,'acceptance':'failed'}
 # Require real results and zero failures/skips/not-executed; process exit alone is insufficient.
 try:
  import xml.etree.ElementTree as ET
  trx=list((out/'trx').glob(name+'.trx'))
  if len(trx)!=1:raise ValueError('missing unique test result')
  ns='{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
  tree=ET.parse(trx[0]).getroot();counters=tree.find('.//'+ns+'Counters')
  if counters is None:raise ValueError('missing counters')
  counts={k:int(v) for k,v in counters.attrib.items()};record['counts']=counts
  if counts.get('passed',0)<=0 or counts.get('executed',0)<=0:raise ValueError('no passed/executed tests')
  if counts.get('total')!=counts['executed'] or counts['executed']!=counts['passed']:raise ValueError('not every discovered test executed and passed')
  if any(counts.get(k,0)!=0 for k in ('failed','error','timeout','aborted','inconclusive','notExecuted','notRunnable','skipped')):raise ValueError('failed/skipped/not-executed test count')
  actual=tree.findall('.//'+ns+'UnitTestResult')
  if not actual or any(t.attrib.get('outcome')!='Passed' for t in actual):raise ValueError('non-passed actual test outcome')
  if filter_value:
   passedIds={x.attrib.get('testId') for x in actual if x.attrib.get('outcome')=='Passed'}
   observedClasses=set()
   for definition in tree.findall('.//'+ns+'UnitTest'):
    method=definition.find(ns+'TestMethod')
    if method is not None and definition.attrib.get('id') in passedIds:observedClasses.add(method.attrib.get('className',''))
   for clause in filter_value.split('|'):
    if not clause.startswith('FullyQualifiedName~'):raise ValueError('unsupported filter clause')
    required=clause.removeprefix('FullyQualifiedName~')
    expectedClasses={entry['namespace']+'.'+entry['class'] for entry in provenance['classes'] if required in entry['namespace']+'.'+entry['class']}
    if len(expectedClasses)!=1 or not expectedClasses.issubset(observedClasses):raise ValueError('exact pinned owning class has no actual Passed TRX execution: '+required)
  if code:raise ValueError('nonzero test process exit')
  if len(actual)!=counts['executed']:raise ValueError('actual test result count mismatch')
  passedCaseNames=[result.attrib.get('testName') for result in actual]
  normalizedPassed=[xunit_discovery_name(case) for case in passedCaseNames]
  if len(passedCaseNames)!=len(set(passedCaseNames)) or len(normalizedPassed)!=len(set(normalizedPassed)) or set(normalizedPassed)!=set(discovered):raise ValueError('Actual Passed/discovery relation differs or has ambiguous normalized collisions')
  if name=='home':
   passedIds={result.attrib.get('testId') for result in actual}
   observedHomeClasses={definition.find(ns+'TestMethod').attrib.get('className','') for definition in tree.findall('.//'+ns+'UnitTest') if definition.attrib.get('id') in passedIds and definition.find(ns+'TestMethod') is not None}
   requiredHomeClasses={entry['namespace']+'.'+entry['class'] for entry in provenance['classes'] if entry['path'].startswith('9to1 Workspace/Home/Tests/')}
   if not requiredHomeClasses.issubset(observedHomeClasses):raise ValueError('Whole current Home test-bearing class missing actual Passed outcome')
  if name in ('core','canvas','native-ui'):
   prior=json.loads((root/'.github/validation/astra-canvas49-predecessor-passed-cases.json').read_text())
   required={tuple((entry['class'],entry['method'],entry['testName'])) for suite in prior['suites'] if suite['trxPath']=='trx/'+name+'.trx' for entry in suite['requiredPassedCases']}
   if not required:raise ValueError('actual predecessor Passed case inventory absent for '+name)
   definitions={definition.attrib['id']:definition.find(ns+'TestMethod') for definition in tree.findall('.//'+ns+'UnitTest')}
   observed=set()
   for result in actual:
    method=definitions.get(result.attrib.get('testId'))
    if method is not None and result.attrib.get('outcome')=='Passed':observed.add((method.attrib.get('className'),method.attrib.get('name'),result.attrib.get('testName')))
   if not required.issubset(observed):raise ValueError('actual predecessor Passed case identities missing: '+str(sorted(required-observed)))
  if name=='native-ui':
   eraserCases={'Accessible_chosen_stroke_requires_actual_Home_approval_and_commits_one_canonical_delete':1,'Real_natural_pointer_gesture_routes_exact_whole_or_partial_owner_and_has_no_write_before_approval':2,'Real_Quick_pointer_capture_commits_only_after_actual_Home_review_and_retires_old_surface':1,'View_change_cancels_real_pointer_capture_and_stale_Files_revision_retires_surface_without_request':1,'History_controls_request_fresh_exact_Home_operations_and_reopen_actual_donor_Undo_Redo':1,'Original_store_draw_successor_denies_actual_foreign_store_substitution_before_adoption_or_write':1}
   definitions={x.attrib['id']:x.find(ns+'TestMethod') for x in tree.findall('.//'+ns+'UnitTest')}
   observed={key:0 for key in eraserCases}
   for result in actual:
    method=definitions.get(result.attrib.get('testId'))
    if method is not None and method.attrib.get('className','').endswith('.CanvasNativeEraserControlTests') and method.attrib.get('name') in observed:observed[method.attrib['name']]+=1
   if observed!=eraserCases:raise ValueError('actual seven native eraser cases missing or duplicated: '+str(observed))
  if name in ('canvas','native-ui'):
   prefix='9to1 Workspace/Canvas/'+('Tests/' if name=='canvas' else 'NativeUI.Tests/')
   passedIds={x.attrib.get('testId') for x in actual}
   observedClasses={definition.find(ns+'TestMethod').attrib.get('className','') for definition in tree.findall('.//'+ns+'UnitTest') if definition.attrib.get('id') in passedIds and definition.find(ns+'TestMethod') is not None}
   for entry in provenance['classes']:
    if entry['path'].startswith(prefix) and entry['namespace']+'.'+entry['class'] not in observedClasses:raise ValueError('owning full-suite class not executed: '+entry['class'])
  record['acceptance']='passed-zero-skips'
 except (ValueError,ET.ParseError) as error:record['reason']=str(error)
 results.append(record)
 (out/'receipt.json').write_text(json.dumps({'commit':commit,'manifestSha256':a.manifest_sha,'results':results,'qualification':'Bounded managed follow-up only; no GUI/provider/login/WPE/package/Android acceptance'},indent=2)+'\n')
 if record['acceptance']!='passed-zero-skips':sys.exit(code or 1)
verify()

# Fresh actual central Canvas Host build, not GUI acceptance.
build_and_pin("host","9to1 Workspace/Canvas/Host/HavenOS.Canvas.Host.csproj");verify()
if digest(library)!=librarySha or digest(lock)!=lockSha:raise SystemExit("native source/output changed")

for name,project in entries:
 after=restore.snapshot_restore(root,project,toolProjects)
 (out/(name+"-restore-after.json")).write_text(json.dumps(after,indent=2)+"\n")
 if after!=restoreBefore[name]:raise SystemExit("actual restored dependency graph changed during owning gates")

if subprocess.check_output(['git','-C',donor,'status','--porcelain','--untracked-files=no']):raise SystemExit('donor changed')

# Capture postbuild generated/compiler-source inputs separately; initial item query is not complete generated-input proof.
generated={}
for generatedRoot in (root/'artifacts/canvas65-managed-build',root/'artifacts/canvas65-host-build-tasks'):
 for file in sorted(generatedRoot.rglob('*')):
  if file.is_symlink():raise SystemExit('generated input symlink')
  if file.is_file():generated[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
(out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
for name,project in entries:
 graph=restoreBefore[name]
 for item in graph['projects']:
  sourceProject=item['path'];effectiveFramework=item['effectiveFramework'];key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource,MicroComIdl','-getProperty:TargetPath,MSBuildProjectExtensionsPath,MicroComGeneratorMSBuildDll,UseLocalMicroComBuild']+[actualTaskProperty]
  if item.get('hostContext'):
   args=[v for v in args if not v.startswith(('-p:RuntimeIdentifier=','-p:RuntimeIdentifiers=','-p:SelfContained=','-p:ArtifactsPath='))]+['-p:ArtifactsPath='+str(root/'artifacts/canvas65-host-build-tasks')]
  key+=('-host' if item.get('hostContext') else '-consumer')
  if command(args,'postbuild-evaluated-'+key):raise SystemExit('postbuild compiler input query failed')
  data=json.loads((out/('postbuild-evaluated-'+key+'.log')).read_text())
  # Complete exact maintained donor MicroCom map; evaluated items must equal these source-declared pairs.
  microComProjects={
   'framework/CUI/vendor/Avalonia/src/Avalonia.Native/Avalonia.Native.csproj':[('avn.idl','Interop.Generated.cs')],
   'framework/CUI/vendor/Avalonia/src/Windows/Avalonia.Win32/Avalonia.Win32.csproj':[('WinRT/winrt.idl','WinRT/WinRT.Generated.cs'),('Win32Com/win32.idl','Win32Com/Win32.Generated.cs'),('DirectX/directx.idl','DirectX/directx.Generated.cs'),('DComposition/dcomp.idl','DComposition/DComp.Generated.cs')]
  }
  projectKey=pathlib.Path(sourceProject).resolve().relative_to(root).as_posix()
  if projectKey in microComProjects:
   nativeProject=root/projectKey
   assert admitted[str(nativeProject.resolve())]==digest(nativeProject)
   props=data['Properties'];assert props.get('UseLocalMicroComBuild','').lower()!='true'
   idls=data['Items'].get('MicroComIdl',[]);expectedPairs=microComProjects[projectKey]
   actualPairs=[(pathlib.Path(i['FullPath']).resolve().relative_to(nativeProject.parent).as_posix(),i['CSharpInteropPath'].replace(chr(92),'/'))for i in idls]
   assert len(actualPairs)==len(set(actualPairs)) and sorted(actualPairs)==sorted(expectedPairs)
   for entry in idls:
    idl=pathlib.Path(entry['FullPath']).resolve();output=(nativeProject.parent/entry['CSharpInteropPath'].replace(chr(92),'/')).resolve()
    assert (idl.relative_to(nativeProject.parent).as_posix(),output.relative_to(nativeProject.parent).as_posix()) in expectedPairs
    assert not idl.is_symlink() and not output.is_symlink() and idl.is_file() and output.is_file()
    assert admitted[str(idl)]==digest(idl)
    generator=pathlib.Path(props['MicroComGeneratorMSBuildDll']).resolve()
    assert generator.as_posix().lower().endswith('/microcom.codegenerator.msbuild/0.11.4/tools/netstandard2.0/microcom.codegenerator.msbuild.dll')
    assert admitted[str(generator)]==digest(generator)
    packageRoot=generator.parents[2];targets=packageRoot/'build/MicroCom.CodeGenerator.MSBuild.targets'
    assert admitted[str(targets.resolve())]==digest(targets)
    imported=root/'framework/CUI/vendor/Avalonia/build/MicroCOM.props';assert admitted[str(imported.resolve())]==digest(imported)
    compilePaths=[pathlib.Path(x['FullPath']).resolve()for x in data['Items']['Compile']];assert compilePaths.count(output)==1
    generated[str(output)]={'bytes':output.stat().st_size,'sha256':digest(output),'producer':'MicroCom.CodeGenerator.MSBuild/0.11.4 GenerateMicroComItems → UpdateMicroComCompileItems','projectSha256':digest(nativeProject),'idlSha256':digest(idl),'generatorSha256':digest(generator),'targetsSha256':digest(targets),'importSha256':digest(imported)}
    (out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
  for items in data.get('Items',{}).values():
   for item in items:
    file=pathlib.Path(item['FullPath']).resolve();expected=admitted.get(str(file),generated.get(str(file),{}).get('sha256'))
    if not file.is_file() or expected!=digest(file):raise SystemExit('unknown postbuild generated/compiler input '+str(file))
verify()

finalSdk=exact_module('final_sdk_inventory','.github/scripts/astra-canvas-sdk-archive-admission.py').verify_install(os.environ['ASTRA_CANVAS_OFFICIAL_SDK_ROOT'],os.environ['ASTRA_CANVAS_OFFICIAL_SDK_ARCHIVE'])
if any(finalSdk[key]!=sdkAdmission[key] for key in ('sdkFiles','sdkLinks','sdkDirectories','archiveSha512')):raise ValueError('Official SDK full installed inventory changed during owning execution')
(out/'sdk-install-after-owning.json').write_text(json.dumps(finalSdk,indent=2)+'\n')
