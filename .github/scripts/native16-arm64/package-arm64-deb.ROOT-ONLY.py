#!/usr/bin/env python3
"""Root-only package assembly, not run by reviewer. No SDK, installation, postinst or publishing."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,re,shutil,stat,subprocess,sys
sys.path.insert(0,str(Path(__file__).resolve().parent/'chromium-native'))
from browser_stage_guard import validate_browser_proof, validate_browser_stage, derive_browser_deps, derive_libc_floor, derive_provider_version
from chromium_native_common import normalize_permissions
p=argparse.ArgumentParser();p.add_argument('--rnote-native-receipt',type=Path);p.add_argument('--publish',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--version',required=True);p.add_argument('--maintainer',required=True);p.add_argument('--build-provenance',type=Path,required=True);p.add_argument('--readme',type=Path,required=True);p.add_argument('--notices',type=Path,required=True);p.add_argument('--source-licence',type=Path,required=True);p.add_argument('--inference-stage',type=Path);p.add_argument('--browser-stage',type=Path);p.add_argument('--browser-proof',type=Path);p.add_argument('--source-dir',type=Path);p.add_argument('--source-pins',type=Path);p.add_argument('--ubuntu-inrelease',type=Path);p.add_argument('--ubuntu-keyring',type=Path);p.add_argument('--provider-cache',type=Path);args=p.parse_args();src=args.publish.resolve();out=args.output.resolve()
if out.exists():raise SystemExit('Refuse existing package output: choose a new directory')
if not src.is_dir() or not args.notices.is_dir() or not all(x.is_file() for x in [args.build_provenance,args.readme,args.source_licence]):raise SystemExit('Missing genuine publish/provenance/README/notices/license input')
subprocess.run(['dpkg','--validate-version',args.version],check=True)
if '\n' in args.maintainer or '\r' in args.maintainer or not re.fullmatch(r'[^<>]+ <[^<>@ ]+@[^<> ]+>',args.maintainer):raise SystemExit('Supply one reviewed Debian maintainer name/address')
provenance=json.loads(args.build_provenance.read_text())
for k in ['source_head','source_patch_sha256','rid','self_contained','publish_exit_code','publish_directory']:
 if k not in provenance:raise SystemExit('Missing actual build provenance field: '+k)
if provenance['rid']!='linux-arm64' or provenance['self_contained'] is not True or provenance['publish_exit_code']!=0 or Path(provenance['publish_directory']).resolve()!=src:raise SystemExit('Provenance does not attest this successful ARM64 self-contained publish')
sys.path.insert(0,str(Path(__file__).resolve().parent/'rnote-native'))
from rnote_native_stage import validate_stage as validate_rnote_stage, verify_package as verify_rnote_package
rnote_receipt = validate_rnote_stage(Path(__file__).resolve().parents[3], src, args.rnote_native_receipt) if args.rnote_native_receipt else None
if (src/'libcakeos_canvas_rnote_poc.so').exists() and rnote_receipt is None:raise SystemExit('Native Rnote library requires its verified source/dependency receipt')
if rnote_receipt and rnote_receipt['source_head'] != provenance['source_head']:raise SystemExit('Rnote source differs from actual product build')
# ---------------------------------------------------------------------------
# Optional verified Chromium-native browser stage merge. When provided, every file
# listed in the stage manifest must exist and match its sha256; otherwise the whole
# package build is refused (no partial output). The private bundle is merged under
# /opt/9to1-preview/browser.
# ---------------------------------------------------------------------------
browser_stage=None;browser_deps=[];browser_sandbox_sha=None;browser_identity=None;browser_proof=None;_browser_gcc_floor=None;_browser_stdcpp_floor=None
if args.browser_stage:
    bs=args.browser_stage.resolve()
    if args.source_dir is None or args.source_pins is None:raise SystemExit('--browser-stage requires --source-dir and --source-pins (real source gate)')
    if args.browser_proof is None:raise SystemExit('--browser-stage requires --browser-proof (static ABI proof artifact)')
    # The authenticated proof inputs are MANDATORY whenever a browser stage is merged:
    # a complete PASS requires the signed InRelease + keyring + provider cache (no
    # warning+proceed / Layer-1-only path for the normal packaging recipe).
    if args.ubuntu_inrelease is None or args.ubuntu_keyring is None or args.provider_cache is None:
        raise SystemExit('--browser-stage requires --ubuntu-inrelease/--ubuntu-keyring/--provider-cache (authenticated browser-proof gate)')
    bm=validate_browser_stage(bs, args.source_dir.resolve(), args.source_pins.resolve())
    # Static ABI proof gate: PASS, NOTRUN, bound to this stage manifest + resolution
    # table and authenticated provider/version-edge coverage.
    _resol_path=Path(__file__).resolve().parent/'chromium-native/chromium-soname-resolution.json'
    if not _resol_path.is_file():raise SystemExit('missing chromium-soname-resolution.json for browser proof')
    browser_proof=validate_browser_proof(args.browser_proof, bs, _resol_path,
                                         args.ubuntu_inrelease, args.ubuntu_keyring,
                                         args.provider_cache)
    browser_stage=bs
    for e in bm.get('files',[]):
        if e.get('path')=='chromium/chrome-sandbox':
            browser_sandbox_sha=e.get('sha256');break
    notices_src=bs/'NOTICES'
    def _notice_sha(name):
        p=notices_src/name
        return hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None
    browser_identity={
        'version':bm.get('version'),
        'schema':bm.get('schema'),
        'source_gate':'passed_real_physical',
        'sources_verified':bm.get('sources_verified'),
        'files_count':len(bm.get('files',[])),
        'sandbox':{
            'mode_hex':bm.get('sandbox',{}).get('mode_hex'),
            'expected_owner':bm.get('sandbox',{}).get('expected_owner'),
            'observed_uid':bm.get('sandbox',{}).get('observed_uid'),
            'payload_sha256':browser_sandbox_sha,
        },
        'notices_sha256':{
            'chromium_copyright':_notice_sha('chromium-copyright'),
            'libjpeg62_turbo_copyright':_notice_sha('libjpeg62-turbo-copyright'),
            'ijg_acknowledgement':_notice_sha('IJG-acknowledgement.txt'),
        },
        'bundled_status':'staged_verified_not_native_accepted',
        'proof':{
            'schema':browser_proof.get('schema'),
            'verdict':browser_proof.get('verdict'),
            'acceptance':browser_proof.get('acceptance'),
            'manifest_sha256':browser_proof.get('manifest',{}).get('manifest_sha256'),
            'resolution_sha256':browser_proof.get('resolution_sha256'),
            'version_edge_count':browser_proof.get('version_edge_count'),
            'version_edge_satisfied':browser_proof.get('version_edge_satisfied'),
            'provider_count':browser_proof.get('providers',{}).get('proved_count'),
            'providers':{k:{'provider':v.get('provider'),'version':v.get('version'),
                             'sha256':v.get('sha256'),'filename':v.get('filename'),
                             'path_in_pkg':v.get('path_in_pkg'),'role':v.get('role')}
                          for k,v in browser_proof.get('providers',{}).get('proved',{}).items()},
        },
        'gtk_dlopen_policy':'GTK and other compositor/UI libraries are loaded via dlopen at runtime when present; they are not hard DT_NEEDED providers and are not forced as package dependencies. The packaged browser self-contains its EGL/GLES/Vulkan and private JPEG providers.',
    }
    # Derive the libc6 floor and the browser dependency packages/version floors from
    # the actual verified staged-ELF version sets and provider identities (no
    # hardcoded unversioned floors).
    browser_deps=derive_browser_deps(browser_proof, json.loads(_resol_path.read_text()))
    _browser_floor=derive_libc_floor(browser_proof)
    # libgcc-s1 / libstdc++6 floors derived from the verified provider versions that
    # satisfy the GCC_14.0/libstdc++ version needs (not a hardcoded ">= 11").
    _browser_gcc_floor=derive_provider_version(browser_proof,'libgcc_s.so.1')
    _browser_stdcpp_floor=derive_provider_version(browser_proof,'libstdc++.so.6')
verifier=Path(__file__).with_name('verify-arm64-publish.READONLY.py');check=subprocess.run([sys.executable,str(verifier),str(src)],capture_output=True,text=True);inspection=json.loads(check.stdout)
if check.returncode:print(check.stdout);raise SystemExit('ARM64 publish shape rejected; no package created')
if provenance.get('published_tree_manifest_sha256')!=inspection['tree_manifest_sha256']:raise SystemExit('Frozen published file manifest differs from actual baseline; refuse stale provenance')
app_glibc=tuple(map(int,inspection['effective_max_glibc'].split('.')))
# When the Chromium-native browser stage is merged, the staged ELF closure requires
# GLIBC_2.39 (chrome_crashpad_handler) and the Ubuntu26 provider closure resolves up
# to GLIBC_2.43. The Depends floor must therefore be >= 2.39, not the app-only
# 2.34/2.36 floor.
_base_floor=(2,36) if args.inference_stage else (2,34)
if args.browser_stage:
    # libc6 floor derived from the ACTUAL verified staged-ELF required versions
    # (e.g. crashpad requires GLIBC_2.39); never a hardcoded guess.
    _base_floor=max(_base_floor,_browser_floor)
libc_floor='.'.join(map(str,max(app_glibc,_base_floor)))
# The shipped browser-preflight.py helper is a python3 script; declare python3
# whenever the helper is installed (it always is, so Python3 is always required).
python_dep=', python3'
# Fail early for the mandatory preflight helper BEFORE any package output/staging is
# created, so a missing helper never leaves partial package output behind.
_preflight=Path(__file__).with_name('browser-preflight.py')
if not _preflight.is_file():raise SystemExit('Missing browser-preflight.py; cannot ship a doc-referenced preflight helper')
inference_summary=None
out.mkdir(mode=0o700);(out/'publish-architecture-inspection.json').write_text(check.stdout)
stage=out/'stage';runtime=stage/'opt/9to1-preview';runtime.parent.mkdir(parents=True);shutil.copytree(src,runtime,symlinks=True)
if browser_stage:
    shutil.copytree(browser_stage,runtime/'browser',symlinks=True)
for d in [stage/'DEBIAN',stage/'usr/bin',stage/'usr/share/applications',stage/'usr/share/doc/9to1-preview']:d.mkdir(parents=True,exist_ok=True)
doc=stage/'usr/share/doc/9to1-preview';shutil.copy2(args.readme,doc/'PREVIEW-README.md');shutil.copy2(args.source_licence,doc/'LICENSE');shutil.copy2(args.build_provenance,doc/'source-and-build.json');shutil.copytree(args.notices,doc/'third-party-notices')
if rnote_receipt:
 shutil.copyfile(args.rnote_native_receipt,doc/'rnote-native-stage.json')
 shutil.copyfile(rnote_receipt['licence']['path'],doc/'rnote-LICENSE-GPL-3.0-or-later.txt')
 shutil.copyfile(rnote_receipt['provenance']['path'],doc/'rnote-PROVENANCE.md')
# Ship the (already existence-checked) read-only browser readiness preflight as
# dependency guidance. It never installs/launches anything and reports
# presence/classification only (runtime NOTRUN). It runs under the python3
# interpreter declared in Depends.
shutil.copy2(_preflight,doc/'browser-preflight.py')
if args.inference_stage:
 from inference_stage import merge_reviewed_stage
 inference_summary=merge_reviewed_stage(args.inference_stage,stage,Path(__file__).resolve().parents[3])
# Avoid duplicating dependency packages already guaranteed by the base Depends.
_base_dep_names={'libc6','libssl3t64','libssl3','libicu78','libicu76','libicu74','libicu72','libicu70','libfontconfig1','libfreetype6','libx11-6','libxext6','libxrandr2','libxi6','libxcursor1','libice6','libsm6','libgl1','libegl1','libdbus-1-3','libgomp1','libasound2t64','libasound2','libgcc-s1','libstdc++6'}
_browser_deps_filtered=[d for d in browser_deps if d.split(' (')[0] not in _base_dep_names]
_rnote_deps_suffix = (', ' + ', '.join(rnote_receipt['inspection']['dependencies'])) if rnote_receipt else ''
_browser_deps_suffix = (', ' + ', '.join(_browser_deps_filtered)) if _browser_deps_filtered else ''
_gcc_dep = f"libgcc-s1 (>= {_browser_gcc_floor})" if _browser_gcc_floor else "libgcc-s1"
_stdcpp_dep = f"libstdc++6 (>= {_browser_stdcpp_floor})" if _browser_stdcpp_floor else "libstdc++6"
_browser_desc = (
    '  A verified Chromium-native browser stage is bundled (self-contained);\n'
    '  no separately installed Chromium runtime is required. GTK and other UI\n'
    '  compositor libraries are dlopen-loaded at runtime when present and are\n'
    '  intentionally not forced as package dependencies. Run python3\n'
    '  /usr/share/doc/9to1-preview/browser-preflight.py for truthful\n'
    '  presence/classification guidance; presence alone is not proof of\n'
    '  browser readiness (runtime validation is NOTRUN).\n'
) if args.browser_stage else (
    '  Browser tabs need a separately installed Chromium runtime. Run python3\n'
    '  /usr/share/doc/9to1-preview/browser-preflight.py for truthful\n'
    '  presence/classification guidance; presence alone is not proof of\n'
    '  browser readiness (runtime validation is NOTRUN).\n'
)
installed_kb=(sum(x.stat().st_size for x in stage.rglob('*') if x.is_file())+1023)//1024
control=f'''Package: 9to1-preview
Version: {args.version}
Section: devel
Priority: optional
Architecture: arm64
Maintainer: {args.maintainer}
Installed-Size: {installed_kb}
Depends: libc6 (>= {libc_floor}){python_dep}, {_gcc_dep}, {_stdcpp_dep}, zlib1g, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libfreetype6, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libice6, libsm6, libgl1, libegl1, libdbus-1-3, libgomp1, libasound2t64 | libasound2{_browser_deps_suffix}{_rnote_deps_suffix}
Recommends: fonts-dejavu-core, xdg-utils
Suggests: libespeak-ng1
Homepage: https://9to1.uk
Description: 9to1 Lesson Preview (incomplete)
 Self-contained .NET 10 ARM64 desktop preview for testing.
 Preview features are incomplete; see installed PREVIEW-README.md.
{_browser_desc}'''
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
# Preserve ONLY the exact vendor chrome-sandbox setuid file (mode 4755, payload sha
# from the validated manifest); strip any other SUID/SGID/special bit. Root ownership
# (uid/gid 0) is applied by dpkg-deb --root-owner-group at build time.
sb_path=(runtime/'browser/chromium/chrome-sandbox') if browser_stage else None
allowed={sb_path} if sb_path is not None and sb_path.exists() else set()
allowed_sha={str(sb_path): browser_sandbox_sha} if allowed and browser_sandbox_sha else None
normalize_permissions(stage, allowed, allowed_sha)
(stage/'usr/bin/9to1-preview').chmod(0o755);(runtime/'Haven').chmod(0o755)
package=out/f'9to1-preview_{args.version}_arm64.deb';subprocess.run(['dpkg-deb','--build','--root-owner-group',str(stage),str(package)],check=True)
rnote_package_proof = verify_rnote_package(package, rnote_receipt, args.rnote_native_receipt) if rnote_receipt else None
fields=subprocess.check_output(['dpkg-deb','--field',str(package),'Package','Architecture','Version'],text=True)
if 'Architecture: arm64' not in fields:raise SystemExit('Unexpected .deb architecture')
digest=hashlib.sha256(package.read_bytes()).hexdigest();(out/'SHA256SUMS').write_text(digest+'  '+package.name+'\n');(out/'package-metadata.txt').write_text(fields);(out/'package-contents.txt').write_text(subprocess.check_output(['dpkg-deb','--contents',str(package)],text=True))
receipt={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'package':str(package),'sha256':digest,'bytes':package.stat().st_size,'architecture':'arm64','publish_tree_manifest_sha256':inspection['tree_manifest_sha256'],'source_head':provenance['source_head'],'source_patch_sha256':provenance['source_patch_sha256'],'package_status':'ASSEMBLED_NOT_NATIVE_ACCEPTED','ARM_native_execution':'NOT RUN','installation':'NOT RUN','lesson_workflow':'NOT RUN','published':'NO','no_maintainer_scripts':True,'private_data_directory_default':'${XDG_CONFIG_HOME:-$HOME/.config}/9to1-preview','Windows_setup_flag_used':False,'inference':inference_summary,'effective_libc6_floor':libc_floor,'browser_static_proof':str(args.browser_proof) if args.browser_proof else None,'bundled_browser':browser_identity,'rnote_native':rnote_package_proof};(out/'package-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n');print(json.dumps(receipt,indent=2))
