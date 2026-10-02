import pathlib,hashlib,json,os,re,subprocess,sys,shutil
def prepare(root,out,digest,command,verify):
 canvas_donor ='9to1 Workspace/Canvas/Source/Rnote'
 if subprocess .check_output (['git','-C',canvas_donor ,'rev-parse','HEAD'],text =True ).strip ()!='1a728d6a85db3528f9c79dc0990700e91b22696f':raise SystemExit ('Rnote donor pin mismatch')
 if subprocess .check_output (['git','-C',canvas_donor ,'status','--porcelain','--untracked-files=no']):raise SystemExit ('Rnote tracked donor source dirty')
 for tool in ('rustc','cargo'):
  version =subprocess .check_output ([tool ,'--version'],text =True );(out /(tool +'-version.txt')).write_text (version )
  if not version .startswith (tool +' 1.98.1 '):raise SystemExit ('unexpected actual Rust toolchain')
 
 canvas_lock =root /'9to1 Workspace/Canvas/rnote-poc/Cargo.lock';canvas_lockSha =digest (canvas_lock )
 os .environ ['CARGO_TARGET_DIR']=str (pathlib .Path (os .environ ['RUNNER_TEMP'])/'astra-canvas65-fresh-rust-target')
 if pathlib .Path (os .environ ['CARGO_TARGET_DIR']).exists ():raise SystemExit ('fresh Rust target already exists')
 cargo =['cargo','--locked','--manifest-path','9to1 Workspace/Canvas/rnote-poc/Cargo.toml']
 # --locked is per-command; no lock regeneration or old native binary substitution.
 for command_name ,args in [('cargo-tree',['cargo','tree',*cargo [1 :]]),('cargo-build',['cargo','build','--release','--lib',*cargo [1 :]]),('cargo-test',['cargo','test','--release','--all-targets',*cargo [1 :],'--','--nocapture'])]:
  code =command (args ,command_name );verify ()
  if code or digest (canvas_lock )!=canvas_lockSha :raise SystemExit (code or 'Cargo.lock changed')
 text =(out /'cargo-tree.log').read_text ()
 if re .search (r'(^|\s)(gtk4|libadwaita)\s',text ):raise SystemExit ('headless donor unexpectedly depends on GTK/adwaita')
 testlog =(out /'cargo-test.log').read_text ()
 summary =re .findall (r'test result: ok\. (\d+) passed; (\d+) failed; (\d+) ignored;',testlog )
 if not summary or sum (int (x [0 ])for x in summary )<=0 or any (int (x [1 ])or int (x [2 ])for x in summary ):raise SystemExit ('Rust no positive zero-ignored result')
 for witness in ['quick_hit_uses_real_hitboxes_layer_first_order_and_refuses_ambiguous_chronology_read_only','genuine_split_materializes_exact_mixed_curve_fragments_and_original_pressure_style_identity','quick_native_boundary_uses_actual_engine_render_order_and_resets_refusal_outputs','split_native_boundary_returns_actual_two_fragment_candidate_preserves_original_and_resets_all_refusals']:
  if len (re .findall (r'^test .*::'+re .escape (witness )+r' \.\.\. ok$',testlog ,re .M ))!=1 :raise SystemExit ('missing unique Rust witness '+witness )
 canvas_library =pathlib .Path (os .environ ['CARGO_TARGET_DIR'])/'release/libcakeos_canvas_rnote_poc.so'
 if not canvas_library .is_file ():raise SystemExit ('fresh native library absent')
 canvas_librarySha =digest (canvas_library )
 for tool ,args in [('readelf-dynamic',['readelf','-d',str (canvas_library )]),('readelf-symbols',['readelf','--dyn-syms','--wide',str (canvas_library )]),('native-needed',['ldd',str (canvas_library )])]:
  if command (args ,tool ):raise SystemExit ('native inspection failed')
 os .environ ['LD_LIBRARY_PATH']=str (canvas_library .parent )+(':'+os .environ ['LD_LIBRARY_PATH']if os .environ .get ('LD_LIBRARY_PATH')else '')
 canvas_probe =out /'native-load-probe.py';canvas_probe .write_text ("import ctypes,json,pathlib,hashlib,os\np=pathlib.Path(os.environ['ASTRA_FRESH_CANVAS_LIBRARY']);b=ctypes.CDLL(str(p))\nversions={}\nfor n,e in [('cake_canvas_abi_version',3),('cake_canvas_selection_api_version',1),('cake_canvas_stroke_mutation_api_version',1),('cake_canvas_quick_erase_api_version',1),('cake_canvas_split_erase_api_version',1)]:\n f=getattr(b,n);f.restype=ctypes.c_uint32;versions[n]=f();assert versions[n]==e\nmaps=pathlib.Path('/proc/self/maps').read_text();assert str(p) in maps\nprint(json.dumps({'pid':os.getpid(),'library':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'versions':versions,'actualMaps':maps}))\n")
 os .environ ['ASTRA_FRESH_CANVAS_LIBRARY']=str (canvas_library )
 if command ([sys .executable ,str (canvas_probe )],'native-loaded-identity'):raise SystemExit ('fresh native ABI load failed')
 if digest (canvas_library )!=canvas_librarySha :raise SystemExit ('native bytes changed')
 canvas_nativeOut =out /'native';canvas_nativeOut .mkdir ();shutil .copyfile (canvas_library ,canvas_nativeOut /canvas_library .name )
 if digest (canvas_nativeOut /canvas_library .name )!=canvas_librarySha :raise SystemExit ('retained native copy mismatch')
 picture_donor ='9to1 Workspace/Picture/Source/glycin'
 if subprocess .check_output (['git','-C',picture_donor ,'rev-parse','HEAD'],text =True ).strip ()!='84bed7782d1ae4486068a9ffbde691290c119909':raise SystemExit ('Glycin donor pin mismatch')
 if subprocess .check_output (['git','-C',picture_donor ,'status','--porcelain','--untracked-files=no']):raise SystemExit ('Glycin tracked source dirty')
 for tool in ('rustc','cargo'):
  version =subprocess .check_output ([tool ,'--version'],text =True );(out /(tool +'-version.txt')).write_text (version )
  if not version .startswith (tool +' 1.98.1 '):raise SystemExit ('unqualified Rust toolchain')
 
 picture_lock =root /picture_donor /'Cargo.lock';picture_lockSha =digest (picture_lock )
 if picture_lockSha !='e9d07e520c5159c92dc17842e9b35c4b955915b114a8e48fd0a8f17e31b6480f':raise SystemExit ('Glycin Cargo.lock pin mismatch')
 os .environ ['CARGO_TARGET_DIR']=str (pathlib .Path (os .environ ['RUNNER_TEMP'])/'astra-picture19-fresh-glycin')
 picture_fresh =pathlib .Path (os .environ ['CARGO_TARGET_DIR'])
 if picture_fresh .exists ():raise SystemExit ('native target not fresh')
 args =['cargo','build','--locked','--release','--manifest-path',str (root /picture_donor /'Cargo.toml'),'-p','libglycin','-p','glycin-image-rs']
 if command (args ,'glycin-build'):raise SystemExit ('fresh Glycin build failed')
 verify ()
 if digest (picture_lock )!=picture_lockSha :raise SystemExit ('native lock changed')
 picture_library =picture_fresh /'release/libglycin.so';picture_loader =picture_fresh /'release/glycin-image-rs'
 for p in (picture_library ,picture_loader ):
  if p .is_symlink ()or not p .is_file ():raise SystemExit ('fresh native output missing')
  picture_nativeOut =out /'native';picture_nativeOut .mkdir (exist_ok =True );shutil .copyfile (p ,picture_nativeOut /p .name )
  if digest (picture_nativeOut /p .name )!=digest (p ):raise SystemExit ('native retained copy mismatch')
  for tool ,args in [('readelf',['readelf','-d',str (p )]),('nm',['nm','-D','--defined-only',str (p )]),('ldd',['ldd',str (p )])]:
   if command (args ,tool +'-'+p .name ):raise SystemExit ('ELF dependency inspection failed')
   if tool =='ldd'and 'not found'in (out /(tool +'-'+p .name +'.log')).read_text ():raise SystemExit ('unresolved native dependency')
 picture_config =root /picture_donor /'glycin-loaders/glycin-image-rs/glycin-image-rs.conf'
 if digest (picture_config )!='58996dfbde91483ae3ab4c17d3323390e0e1c689398dd824027520371083d824':raise SystemExit ('loader config source pin')
 picture_data =pathlib .Path (os .environ ['RUNNER_TEMP'])/'astra-picture19-glycin-data'
 if picture_data .exists ():raise SystemExit ('private loader config directory not fresh')
 picture_conf =picture_data /'glycin-loaders/2+/conf.d';picture_conf .mkdir (parents =True );picture_generated =picture_conf /'glycin-image-rs.conf';picture_generated .write_text (picture_config .read_text ().replace ('@EXEC@',str (picture_loader )))
 os .environ ['GLYCIN_DATA_DIR']=str (picture_data );os .environ ['LD_LIBRARY_PATH']=str (picture_fresh /'release')+(':'+os .environ ['LD_LIBRARY_PATH']if os .environ .get ('LD_LIBRARY_PATH')else '')
 for key in os .environ :
  if key .startswith ('GLYCIN_')and ('DISABLE'in key or 'SANDBOX'in key ):raise SystemExit ('sandbox downgrade env refused')
 picture_bwrap =pathlib .Path (shutil .which ('bwrap')or '')
 if not picture_bwrap .is_file ():raise SystemExit ('genuine bubblewrap unavailable')
 # Resolve exactly the real managed glycin imports against freshly compiled native symbols.
 picture_decoder =(root /'9to1 Workspace/Picture/PictureGlycinDecoder.cs').read_text ()
 picture_required =set (re .findall (r'\[DllImport\(Glycin[^\n]+\] internal static extern \w+ (gly_\w+)\(',picture_decoder ))
 picture_exports =(out /'nm-libglycin.so.log').read_text ()
 if not picture_required or any (not re .search (r'\b'+re .escape (x )+r'$',picture_exports ,re .M )for x in picture_required ):raise SystemExit ('fresh library missing managed ABI exports')
 picture_probe =out /'fresh-glycin-loaded.py';picture_probe .write_text ("import ctypes,os,json,hashlib,pathlib\np=pathlib.Path("+repr (str (picture_library ))+");lib=ctypes.CDLL(str(p));symbols="+repr (sorted (picture_required ))+"\nfor name in symbols:getattr(lib,name)\nmaps=pathlib.Path('/proc/self/maps').read_text();assert str(p.resolve()) in maps\nprint(json.dumps({'pid':os.getpid(),'library':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'symbols':symbols,'actualMaps':maps}))\n")
 if command ([sys .executable ,str (picture_probe )],'actual-native-loaded'):raise SystemExit ('fresh Glycin load failed')
 (out /'native-environment.json').write_text (json .dumps ({'library':str (picture_library ),'librarySha256':digest (picture_library ),'loader':str (picture_loader ),'loaderSha256':digest (picture_loader ),'bwrap':str (picture_bwrap ),'bwrapSha256':digest (picture_bwrap ),'configSourceSha256':digest (picture_config ),'privateConfigSha256':digest (picture_generated ),'GLYCIN_DATA_DIR':str (picture_data ),'LD_LIBRARY_PATH':os .environ ['LD_LIBRARY_PATH'],'sandboxSelectorRequired':1 },indent =2 )+'\n')
 return [(canvas_library,digest(canvas_library)),(picture_library,digest(picture_library)),(picture_loader,digest(picture_loader))], [(canvas_lock,digest(canvas_lock)),(picture_lock,digest(picture_lock))]
