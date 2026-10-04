#!/usr/bin/env python3
"""Thin real-source publisher consumer; maintained actual Commands owns processes.
Three structural browser groups + real Chrome zoom; rendered PNG visual review
remains explicit. No mock provider, alternate CUI/engine or deployment.
"""
import argparse, hashlib, importlib.util, json, os, re, shutil
from pathlib import Path
COMMON_SHA="a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
LEDGER="apps/Web/Tests/WaveResponsiveBrowser/source-pins.json"
LEDGER_SHA="f4023e967997016df8673ff59f70455d6bcb76b658b3d8ce0c0936c4d1b3968a"
CAP=192*1024*1024
FLOOR=256*1024*1024
NAMES=["real-populated-390-canvas-fit","real-390-keyboard-and-touch","genuine-chrome-200-percent-fit"]
def sha(p):
 h=hashlib.sha256()
 with p.open("rb") as f:
  for chunk in iter(lambda:f.read(1024*1024),b""):h.update(chunk)
 return h.hexdigest()
def write(p,x):p.write_text(json.dumps(x,indent=2)+"\n")
def inventory(p):
 rows=[]
 for f in sorted(p.rglob("*")):
  if f.is_symlink():raise RuntimeError("Symlink in owned tree")
  if f.is_file():rows.append({"path":f.relative_to(p).as_posix(),"bytes":f.stat().st_size,"sha256":sha(f)})
 return rows
def source_check(root):
 ledger=root/LEDGER
 if sha(ledger)!=LEDGER_SHA:raise RuntimeError("Reviewed source ledger changed")
 rows=json.loads(ledger.read_text())["sourceFiles"]
 for rel,pin in rows.items():
  f=root/rel
  if Path(rel).is_absolute() or ".." in Path(rel).parts or f.is_symlink() or not f.is_file() or not f.resolve().is_relative_to(root):raise RuntimeError("Invalid source path")
  if f.stat().st_size!=pin["bytes"] or sha(f)!=pin["sha256"]:raise RuntimeError("Actual source body differs: "+rel)
 return rows
def main():
 a=argparse.ArgumentParser();a.add_argument("--expected-commit",required=True);a.add_argument("--publication",required=True,type=Path);a.add_argument("--output",required=True,type=Path);args=a.parse_args()
 root=Path.cwd().resolve();publication=args.publication.resolve();out=args.output.resolve()
 if not re.fullmatch(r"[0-9a-f]{40}",args.expected_commit) or out.exists() or out.is_relative_to(root) or not publication.is_dir() or publication.is_relative_to(root):raise RuntimeError("Exact commit/completed publisher/fresh external output required")
 out.mkdir(parents=True);d=out/"diagnostics";d.mkdir();cmd=None;before=None;assets=None;publication_pins=None;tool_pins=None
 result={"state":"NOT_RUN","sourceCommit":args.expected_commit,"scope":"NEW production Wave responsive structural browser3, genuine zoom2; native glyph CI and PNG visual review separate; no full parity/AT/hardware/provider acceptance","resourceScope":"192MiB final positive output+tmp/cache sample, not transient/deleted-peak hard quota","original14":"Immutable original14 runs serially before additional3; independent counts never folded into responsive criteria"}
 env=os.environ.copy()
 for var,name in {"TMPDIR":"tmp","TMP":"tmp","TEMP":"tmp","XDG_CACHE_HOME":"cache"}.items():p=out/name;p.mkdir(exist_ok=True);env[var]=str(p)
 try:
  common=root/"apps/Web/Tests/ci/run-ordinary-native.py"
  if sha(common)!=COMMON_SHA:raise RuntimeError("Maintained a57 Commands changed")
  spec=importlib.util.spec_from_file_location("wave_responsive_real_commands",common);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
  cmd=module.Commands(d,env,root)
  if cmd.run("git-head",["git","rev-parse","HEAD"],15).strip()!=args.expected_commit:raise RuntimeError("Actual checkout differs")
  cmd.run("clean-before",["git","diff","--exit-code","HEAD","--"],15)
  if cmd.run("status-before",["git","status","--porcelain","--untracked-files=all"],15).strip():raise RuntimeError("Fresh source graph required")
  cmd.run("tree",["git","ls-tree","-r","HEAD"],15);before=source_check(root);write(d/"source-before.json",before)
  receipt=publication/"public-artifact/publish-manifest.json";seal=publication/"public-artifact/seal.json";bundle=publication/"sdk-publish/wwwroot"
  rv=json.loads(receipt.read_text());sv=json.loads(seal.read_text());pr=json.loads((publication/"diagnostics/result.json").read_text())
  publication_pins={str(x.relative_to(publication)):sha(x) for x in [receipt,seal,publication/"diagnostics/result.json",publication/"diagnostics/source-before.json",publication/"diagnostics/source-after.json"]}
  if pr["status"]!="PASS" or rv["sourceCommit"]!=args.expected_commit or rv["sourceCommitAfter"]!=args.expected_commit or rv["exitCode"]!=0 or sv["sourceCommit"]!=args.expected_commit or sv["receiptSha256"]!=sha(receipt):raise RuntimeError("Prior actual source publisher/seal incomplete")
  assets=inventory(bundle);expected=sorted([{k:x[k] for k in ("path","bytes","sha256")} for x in rv["publishFiles"]],key=lambda x:x["path"])
  if assets!=expected or len(assets)!=rv["fileCount"] or len({x["path"] for x in expected})!=len(expected):raise RuntimeError("Actual full public inventory differs")
  write(d/"assets-before.json",assets);write(d/"publication-binding.json",{"receiptSha256":sha(receipt),"sealSha256":sha(seal),"fileCount":len(assets),"sourceBeforeSha256":sha(publication/"diagnostics/source-before.json"),"sourceAfterSha256":sha(publication/"diagnostics/source-after.json")})
  version=cmd.run("playwright-version",["node","-e","console.log(require(require('node:path').join(process.env.PLAYWRIGHT_MODULE,'package.json')).version)"],15).strip()
  if version!="1.62.0":raise RuntimeError("Pinned Playwright1.62.0 required")
  executable=cmd.run("chromium-path",["node","-e","console.log(require(process.env.PLAYWRIGHT_MODULE).chromium.executablePath())"],15).strip()
  if not Path(executable).is_file():raise RuntimeError("Actual maintained Chromium absent")
  env["CHROMIUM_EXECUTABLE"]=executable
  module_path=Path(env["PLAYWRIGHT_MODULE"]).resolve()
  tool_pins={str(x):{"bytes":x.stat().st_size,"sha256":sha(x)} for x in [Path(executable),module_path/"package.json",module_path.parent/"playwright-core/browsers.json"]}
  write(d/"tool-bodies-before.json",tool_pins)
  if shutil.disk_usage(out).free<384*1024*1024:raise RuntimeError("Prelaunch384MiB reserve absent")
  # Preserve all fourteen original assertions/body exactly. Real local separate
  # profile/origin runs first; this is a regression on the new source, not narrow QA.
  env.update(B3_CANDIDATE_MANIFEST=str(receipt),B3_CANDIDATE_MANIFEST_SHA256=sha(receipt),B3_PORT="18769")
  old_runner=root/"apps/Web/Tests/ci/sealed-browser-replay/run-wave-browser-cold-quota.cjs"
  cmd.run("unchanged-original-wave14",["node",str(old_runner),str(bundle),str(out/"original14")],300)
  old=json.loads((out/"original14/results.json").read_text())
  if old["counts"]!={"discovered":14,"executed":14,"passed":14,"failed":0,"notRun":0} or old["exit"]!=0 or old.get("finalizationFailures") or sum(old["diagnostics"]["counts"].values()):raise RuntimeError("Original unchanged fourteen regression incomplete")
  result["original14Counts"]=old["counts"];result["original14RunnerSha256"]=sha(old_runner)
  runner=root/"apps/Web/Tests/WaveResponsiveBrowser/run-wave-responsive-browser.cjs";extension=runner.parent/"zoom-extension"
  cmd.run("wave-responsive-real-browser",["node",str(runner),str(bundle),str(receipt),sha(receipt),args.expected_commit,str(out/"raw"),str(extension)],240)
  raw=json.loads((out/"raw/results.json").read_text())
  if raw["counts"]!={"discovered":3,"executed":3,"passed":3,"failed":0,"notRun":0} or list(raw["outcomes"])!=NAMES or raw["acceptance"]!="PASS_SCOPED_THREE_PENDING_SCREENSHOT_VISUAL_REVIEW" or sum(raw["errors"].values()) or raw["cleanup"] or not raw["portRebind"] or raw["zoomCapability"]!="ACTUAL_CHROME_TABS_ZOOM2" or raw["localWaveformPan"]["changedRealPixels"]<20:raise RuntimeError("Actual full structural responsive groups incomplete")
  result.update(state="PASS_STRUCTURAL_THREE_PENDING_VISUAL_REVIEW",counts=raw["counts"],browserVersion=raw["browserVersion"],genuineZoom=raw["zoomCapability"],visualReviewRequired=True)
 except Exception as e:result.update(state="FAIL_OR_INCOMPLETE",error=repr(e))
 finally:
  try:
   after=source_check(root);write(d/"source-after.json",after)
   if before is not None and before!=after:raise RuntimeError("Actual source changed")
   if assets is not None:
    after_assets=inventory(publication/"sdk-publish/wwwroot");write(d/"assets-after.json",after_assets)
    if after_assets!=assets:raise RuntimeError("Actual public assets changed")
   if publication_pins is not None and any(sha(publication/rel)!=value for rel,value in publication_pins.items()):raise RuntimeError("Publisher proof body changed")
   if tool_pins is not None:
    after_tools={rel:{"bytes":Path(rel).stat().st_size,"sha256":sha(Path(rel))} for rel in tool_pins};write(d/"tool-bodies-after.json",after_tools)
    if after_tools!=tool_pins:raise RuntimeError("Actual Chromium/module tool bodies changed")
   if cmd:
    if cmd.run("head-after",["git","rev-parse","HEAD"],15).strip()!=args.expected_commit:raise RuntimeError("Actual source HEAD changed")
    cmd.run("clean-after",["git","diff","--exit-code","HEAD","--"],15)
    if cmd.run("status-after",["git","status","--porcelain","--untracked-files=all"],15).strip():raise RuntimeError("Source graph gained unknown inputs")
   records=[] if cmd is None else cmd.records
   closed=bool(records) and all(x["error"] is None and x["exit"]==0 and x["normalEOF"] and x["familyClosed"] and x["finalECHILD"] and not x["signals"] and all(b["gone"] for b in x["births"]) for x in records)
   result["allCommandFamiliesNormalClosed"]=closed
   if not closed:result["state"]="FAIL_OR_INCOMPLETE"
   write(d/"output-body-custody.json",inventory(out));payload=(json.dumps(result,indent=2)+"\n").encode();total=sum(x.stat().st_size for x in out.rglob("*") if x.is_file())
   if len(payload)>65536 or total+len(payload)>CAP or shutil.disk_usage(out).free-len(payload)<FLOOR:raise RuntimeError("Prospective bounded final receipt cap/reserve failed")
  except Exception as e:result.update(state="FAIL_OR_INCOMPLETE",finalizationError=repr(e))
  write(d/"result.json",result)
  # Sample after the provisional receipt; retain this actual observation and
  # any final resource failure in the durable receipt before stdout/exit.
  # A full64KiB conservative rewrite reserve covers this finite self-receipt.
  total=sum(x.stat().st_size for x in out.rglob("*") if x.is_file());free=shutil.disk_usage(out).free
  result["actualPostReceiptSample"]={"bytes":total,"free":free,"finalRewriteReserveBytes":65536,"qualification":"After provisional receipt; before bounded final rewrite and later stdout capture. Conservative64KiB rewrite reserve, not hardpeak/quota proof; final actual CI exit/output needed"}
  if total+65536>CAP or free-65536<FLOOR:result["state"]="FAIL_OR_INCOMPLETE"
  final_payload=(json.dumps(result,indent=2)+"\n").encode()
  if len(final_payload)>65536:raise RuntimeError("Final self-receipt exceeds reviewed64KiB bound")
  (d/"result.json").write_bytes(final_payload)
 print(json.dumps(result));return 0 if result["state"]=="PASS_STRUCTURAL_THREE_PENDING_VISUAL_REVIEW" else 1
if __name__=="__main__":raise SystemExit(main())
