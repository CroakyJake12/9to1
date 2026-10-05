#!/usr/bin/env python3
"""Ordinary original Canvas/Maps owner suites; no copied process/owner engine."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import time

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
BASE = "apps/Web/Productivity/Tests/ci/"
LEDGER = BASE + "original-canvas-maps-source-pins.json"
LEDGER_SHA = "bb470fd56b17fd14f4079e674e352f2c7bc7998861af721bf220544b6424020a"
GATE = BASE + "original-owner-acceptance.py"
GATE_SHA = "60b8056fbb1f497a30fea6efd907778f1ac4748a319f36fc3810a8baf5478dd7"
SUITES = {
 "canvas8": ("apps/Web/Productivity/Canvas/Tests/Canvas.Original.Tests.csproj", 8),
 "maps6": ("apps/Web/Productivity/Maps/Tests/Maps.OriginalAuthoring.Tests.csproj", 6),
}

def sha(path):
 h = hashlib.sha256()
 with path.open("rb") as stream:
  for block in iter(lambda: stream.read(1024 * 1024), b""): h.update(block)
 return h.hexdigest()

def write(path, value): path.write_text(json.dumps(value, indent=2) + "\n")

def source_check(root):
 ledger = root / LEDGER
 if ledger.is_symlink() or not ledger.is_file() or sha(ledger) != LEDGER_SHA: raise RuntimeError("Reviewed original owner ledger changed; explicit review/rebind required")
 rows = json.loads(ledger.read_text())["sourceFiles"]
 for relative, pin in rows.items():
  p = Path(relative); actual = root / p
  if p.is_absolute() or ".." in p.parts or actual.is_symlink() or not actual.is_file() or not actual.resolve().is_relative_to(root): raise RuntimeError("Owner source not checkout-contained regular input")
  if actual.stat().st_size != pin["bytes"] or sha(actual) != pin["sha256"]: raise RuntimeError("Reviewed original owner body changed: " + relative)
 return rows

def runtime_pins(directory):
 rows = []
 for path in sorted(directory.rglob("*")):
  if path.is_file():
   if path.is_symlink() or not path.resolve().is_relative_to(directory): raise RuntimeError("Emitted runtime body escapes actual target")
   rows.append({"path": str(path.relative_to(directory)), "bytes": path.stat().st_size, "sha256": sha(path)})
 return rows

def load_reviewed(path, expected_sha, name):
 if path.is_symlink() or not path.is_file() or sha(path) != expected_sha: raise RuntimeError("Reviewed " + name + " body changed")
 spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

def main():
 parser = argparse.ArgumentParser(); parser.add_argument("--suite", choices=SUITES, required=True); parser.add_argument("--expected-commit", required=True); parser.add_argument("--output", type=Path, required=True)
 args = parser.parse_args(); root = Path.cwd().resolve(); output = args.output; project, count = SUITES[args.suite]
 if output.is_symlink() or output.absolute() != output.resolve() or output.exists() or output.resolve().is_relative_to(root): raise RuntimeError("Require fresh canonical owned output outside checkout")
 output.mkdir(parents=True); diagnostics = output / "diagnostics"; diagnostics.mkdir(); results_dir = diagnostics / "test-results"; results_dir.mkdir(); trx = results_dir / (args.suite + ".trx")
 result = {"status": "NOT_RUN", "accepted": False, "suite": args.suite, "project": project, "expected": count, "compiler": "NOT_RUN", "native": "NOT_RUN", "sourceCommit": None,
  "scope": "Original actual local Canvas codec/Files durable provider or Maps durable atomic-store authoring only; no browser/native Rnote/CoMaps provider/Home issuer/actor/ACL/cold OS process acceptance"}
 commands = None; gate = None; before = None; runtime = None; target = None; started_ns = None; env = os.environ.copy()
 for variable, name in {"DOTNET_CLI_HOME":"cli", "NUGET_PACKAGES":"nuget", "NUGET_HTTP_CACHE_PATH":"http", "NUGET_PLUGINS_CACHE_PATH":"plugins", "TMPDIR":"tmp", "TMP":"tmp", "TEMP":"tmp", "XDG_CACHE_HOME":"cache", "HAVEN_DATA_DIR":"fixture-data"}.items():
  owned = output / name; owned.mkdir(exist_ok=True); env[variable] = str(owned)
 env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
 try:
  module = load_reviewed(root / COMMON, COMMON_SHA, "original_owner_common_commands"); gate = load_reviewed(root / GATE, GATE_SHA, "original_owner_identity_gate")
  commands = module.Commands(diagnostics, env, root)
  head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
  if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit: raise RuntimeError("Actual source differs from github.sha")
  result["sourceCommit"] = head; commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
  if commands.run("git-status-before", ["git", "status", "--porcelain", "--untracked-files=all"], 15).strip(): raise RuntimeError("Fresh source checkout required")
  commands.run("tracked-source-tree", ["git", "ls-tree", "-r", "HEAD"], 15)
  before = source_check(root); write(diagnostics / "source-before.json", before)
  if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401": raise RuntimeError("Require official SDK10.0.401")
  commands.run("dotnet-info", ["dotnet", "--info"], 30); artifacts = output / "artifacts"
  props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:UseAppHost=true", "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false", "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
  commands.run("restore", ["dotnet", "restore", project, "--artifacts-path", str(artifacts), "-r", "linux-x64", "--configfile", str(root / "NuGet.Config"), "--disable-build-servers", "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + props, 600)
  result["compiler"] = "STARTED"
  commands.run("build", ["dotnet", "build", project, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 900)
  result["compiler"] = "EXIT0"
  evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", project, "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false", "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,IsTestProject"] + props, 30))
  write(diagnostics / "target-properties.json", evaluated); target = Path(evaluated["TargetPath"]).resolve()
  if evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64" or evaluated["IsTestProject"].lower() != "true" or Path(evaluated["TargetDir"]).resolve() != target.parent or not target.is_relative_to(artifacts) or not target.is_file(): raise RuntimeError("Actual isolated target/framework/RID/test project differs")
  with target.open("rb") as stream:
   if stream.read(2) != b"MZ": raise RuntimeError("Actual managed test target absent")
  runtime = runtime_pins(target.parent); write(diagnostics / "runtime-before.json", runtime)
  common_args = ["--no-build", "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props
  discovery = commands.run("discovery", ["dotnet", "test", project] + common_args + ["--list-tests"], 90)
  discovered = gate.discovery_gate(discovery, args.suite); write(diagnostics / "discovery.json", discovered)
  if not discovered["accepted"]: raise RuntimeError("Exact original discovery differs")
  if trx.exists(): raise RuntimeError("Native TRX must be fresh")
  started_ns = time.time_ns(); result["native"] = "STARTED"; test_error = None
  try: commands.run("native-test", ["dotnet", "test", project] + common_args + ["--logger", "trx;LogFileName=" + trx.name, "--results-directory", str(results_dir)], 180)
  except Exception as error: test_error = error; result["actualNativeCommandException"] = repr(error)
  result["actualNativeCommandRecord"] = commands.records[-1]
  actual = gate.trx_gate(trx, started_ns, args.suite); result["actualTRX"] = actual; write(diagnostics / "trx-summary.json", actual)
  if test_error is not None: result["native"] = "FAIL"; raise test_error
  if not actual["accepted"]: raise RuntimeError("Actual original TRX not fully passed with exact identities")
  result.update(status="PASS", native="PASS", executed=count, passed=count, failed=0, notRun=0)
 except Exception as error: result.update(status="FAIL", error=repr(error))
 finally:
  if gate is not None and started_ns is not None and trx.is_file():
   try: result["actualTRX"] = gate.trx_gate(trx, started_ns, args.suite); write(diagnostics / "trx-summary.json", result["actualTRX"])
   except Exception as error: result.update(status="FAIL", trxReadError=repr(error))
  try:
   after = source_check(root); write(diagnostics / "source-after.json", after)
   if before is not None and before != after: raise RuntimeError("Original owner source bodies changed")
   if runtime is not None and target is not None:
    after_runtime = runtime_pins(target.parent); write(diagnostics / "runtime-after.json", after_runtime)
    if runtime != after_runtime: raise RuntimeError("Actual emitted owner/runtime bodies changed")
   if commands is not None:
    commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
    if commands.run("git-status-after", ["git", "status", "--porcelain", "--untracked-files=all"], 15).strip(): raise RuntimeError("Actual checkout gained unknown files")
  except Exception as error: result.update(status="FAIL", custodyError=repr(error))
  records = [] if commands is None else commands.records
  closed = bool(records) and all(x["error"] is None and x["exit"] == 0 and x["normalEOF"] and x["familyClosed"] and x["finalECHILD"] and not x["signals"] and x["births"] and all(b["gone"] for b in x["births"]) for x in records)
  result["allRecordedFamiliesNormalClosed"] = closed; result["accepted"] = result["status"] == "PASS" and closed
  result["custodyQualification"] = "Full source/runtime hash metadata and raw actual TRX/logs; runtime binary/package/fixture store bodies not uploaded. Original fixtures remove their task-owned temp folders as authored; this is not cold OS process or external provider/browser acceptance. Ordinary CI resources; no local cumulative-budget claim."
  write(diagnostics / "result.json", result)
 print(json.dumps(result, indent=2)); return 0 if result["accepted"] else 1

if __name__ == "__main__": raise SystemExit(main())
