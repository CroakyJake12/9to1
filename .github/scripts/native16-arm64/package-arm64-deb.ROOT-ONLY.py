#!/usr/bin/env python3
"""Root-only package assembly, not run by reviewer. No SDK, installation, postinst or publishing."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,re,shutil,stat,subprocess,sys
p=argparse.ArgumentParser();p.add_argument('--publish',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--version',required=True);p.add_argument('--maintainer',required=True);p.add_argument('--build-provenance',type=Path,required=True);p.add_argument('--readme',type=Path,required=True);p.add_argument('--notices',type=Path,required=True);p.add_argument('--source-licence',type=Path,required=True);p.add_argument('--inference-stage',type=Path);args=p.parse_args();src=args.publish.resolve();out=args.output.resolve()
if out.exists():raise SystemExit('Refuse existing package output: choose a new directory')
if not src.is_dir() or not args.notices.is_dir() or not all(x.is_file() for x in [args.build_provenance,args.readme,args.source_licence]):raise SystemExit('Missing genuine publish/provenance/README/notices/license input')
subprocess.run(['dpkg','--validate-version',args.version],check=True)
if '\n' in args.maintainer or '\r' in args.maintainer or not re.fullmatch(r'[^<>]+ <[^<>@ ]+@[^<> ]+>',args.maintainer):raise SystemExit('Supply one reviewed Debian maintainer name/address')
provenance=json.loads(args.build_provenance.read_text())
for k in ['source_head','source_patch_sha256','rid','self_contained','publish_exit_code','publish_directory']:
 if k not in provenance:raise SystemExit('Missing actual build provenance field: '+k)
if provenance['rid']!='linux-arm64' or provenance['self_contained'] is not True or provenance['publish_exit_code']!=0 or Path(provenance['publish_directory']).resolve()!=src:raise SystemExit('Provenance does not attest this successful ARM64 self-contained publish')
verifier=Path(__file__).with_name('verify-arm64-publish.READONLY.py');check=subprocess.run([sys.executable,str(verifier),str(src)],capture_output=True,text=True);inspection=json.loads(check.stdout)
if check.returncode:print(check.stdout);raise SystemExit('ARM64 publish shape rejected; no package created')
if provenance.get('published_tree_manifest_sha256')!=inspection['tree_manifest_sha256']:raise SystemExit('Frozen published file manifest differs from actual baseline; refuse stale provenance')
app_glibc=tuple(map(int,inspection['effective_max_glibc'].split('.')))
libc_floor='.'.join(map(str,max(app_glibc,(2,36) if args.inference_stage else (2,34))))
python_dep=', python3' if args.inference_stage else ''
inference_summary=None
out.mkdir(mode=0o700);(out/'publish-architecture-inspection.json').write_text(check.stdout)
stage=out/'stage';runtime=stage/'opt/9to1-preview';runtime.parent.mkdir(parents=True);shutil.copytree(src,runtime,symlinks=True)
for d in [stage/'DEBIAN',stage/'usr/bin',stage/'usr/share/applications',stage/'usr/share/doc/9to1-preview']:d.mkdir(parents=True,exist_ok=True)
doc=stage/'usr/share/doc/9to1-preview';shutil.copy2(args.readme,doc/'PREVIEW-README.md');shutil.copy2(args.source_licence,doc/'LICENSE');shutil.copy2(args.build_provenance,doc/'source-and-build.json');shutil.copytree(args.notices,doc/'third-party-notices')
if args.inference_stage:
 from inference_stage import merge_reviewed_stage
 inference_summary=merge_reviewed_stage(args.inference_stage,stage,Path(__file__).resolve().parents[3])
installed_kb=(sum(x.stat().st_size for x in stage.rglob('*') if x.is_file())+1023)//1024
control=f'''Package: 9to1-preview
Version: {args.version}
Section: devel
Priority: optional
Architecture: arm64
Maintainer: {args.maintainer}
Installed-Size: {installed_kb}
Depends: libc6 (>= {libc_floor}){python_dep}, libgcc-s1, libstdc++6 (>= 11), zlib1g, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libfreetype6, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libice6, libsm6, libgl1, libegl1, libdbus-1-3, libgomp1
Recommends: fonts-dejavu-core, xdg-utils
Suggests: libasound2t64 | libasound2, libespeak-ng1
Homepage: https://9to1.uk
Description: 9to1 Lesson Preview (incomplete)
 Self-contained .NET 10 ARM64 desktop preview for testing.
 Preview features are incomplete; see installed PREVIEW-README.md.
'''
(stage/'DEBIAN/control').write_text(control)
launcher='''#!/bin/sh
set -eu
umask 077
if [ -z "${HAVEN_DATA_DIR:-}" ]; then
  HAVEN_DATA_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/9to1-preview"
  export HAVEN_DATA_DIR
fi
cd /opt/9to1-preview
exec /opt/9to1-preview/Haven "$@"
'''
(stage/'usr/bin/9to1-preview').write_text(launcher)
(stage/'usr/share/applications/9to1-preview.desktop').write_text('''[Desktop Entry]
Type=Application
Name=9to1 Lesson Preview (Incomplete)
Comment=Incomplete ARM64 lesson preview for testing
TryExec=9to1-preview
Exec=9to1-preview
Icon=applications-development
Terminal=false
Categories=Development;Office;
StartupNotify=true
''')
# Publish files were created under private cloud umask. Normalize installed readability,
# preserving executable tools; never install owner data/config or root-owned user state.
stage.chmod(0o755)
for f in stage.rglob('*'):
 if f.is_symlink():continue
 if f.is_dir():f.chmod(0o755)
 else:f.chmod(0o755 if f.stat().st_mode&0o111 else 0o644)
(stage/'usr/bin/9to1-preview').chmod(0o755);(runtime/'Haven').chmod(0o755)
package=out/f'9to1-preview_{args.version}_arm64.deb';subprocess.run(['dpkg-deb','--build','--root-owner-group',str(stage),str(package)],check=True)
fields=subprocess.check_output(['dpkg-deb','--field',str(package),'Package','Architecture','Version'],text=True)
if 'Architecture: arm64' not in fields:raise SystemExit('Unexpected .deb architecture')
digest=hashlib.sha256(package.read_bytes()).hexdigest();(out/'SHA256SUMS').write_text(digest+'  '+package.name+'\n');(out/'package-metadata.txt').write_text(fields);(out/'package-contents.txt').write_text(subprocess.check_output(['dpkg-deb','--contents',str(package)],text=True))
receipt={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'package':str(package),'sha256':digest,'bytes':package.stat().st_size,'architecture':'arm64','publish_tree_manifest_sha256':inspection['tree_manifest_sha256'],'source_head':provenance['source_head'],'source_patch_sha256':provenance['source_patch_sha256'],'package_status':'ASSEMBLED_NOT_NATIVE_ACCEPTED','ARM_native_execution':'NOT RUN','installation':'NOT RUN','lesson_workflow':'NOT RUN','published':'NO','no_maintainer_scripts':True,'private_data_directory_default':'${XDG_CONFIG_HOME:-$HOME/.config}/9to1-preview','Windows_setup_flag_used':False,'inference':inference_summary,'effective_libc6_floor':libc_floor};(out/'package-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n');print(json.dumps(receipt,indent=2))
