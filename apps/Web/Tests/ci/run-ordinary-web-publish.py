#!/usr/bin/env python3
"""Normal SDK/Bun source build; sealed public wwwroot only, never deployment."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import zipfile

COMMON_SHA256 = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PUBLIC_LIMIT = 128 * 1024 * 1024
SOURCE_ROOTS = ["apps/Web", "framework/CUI", "9to1 Workspace/Home", "9to1 Workspace/shared/src",
                "9to1 Workspace/Wave", "9to1 Workspace/Write", "9to1 Workspace/Picture", "9to1 Workspace/Present", "Directory.Build.props",
                "Directory.Build.targets", "global.json", "NuGet.Config"]
SOURCE_EXT = {".cs", ".csproj", ".cui", ".axaml", ".props", ".targets", ".json", ".mjs", ".js",
              ".lock", ".ttf", ".otf", ".png", ".svg", ".woff", ".woff2", ".html", ".css",
              ".py", ".txt", ".yml", ".yaml"}
LINKED_CUTS = ["9to1 Workspace/Wave/WaveProject.cs", "9to1 Workspace/Wave/WaveProjectEdits.cs",
               "9to1 Workspace/Wave/WaveTimelineAnnotations.cs", "9to1 Workspace/Wave/WaveMediaAssetReferences.cs",
               "9to1 Workspace/Wave/Program.cs", "apps/Web/Wave/Engine/generate-owner-decoder.py",
               "9to1 Workspace/Picture/PictureDocument.cs", "9to1 Workspace/Picture/PictureCropService.cs",
               "9to1 Workspace/Picture/ImageViewportState.cs"]


def sha(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(data)
    return result.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def sdk_json(text):
    offset = text.find("{")
    if offset < 0:
        raise RuntimeError("Actual SDK JSON missing")
    value, end = json.JSONDecoder().raw_decode(text[offset:])
    if text[offset:][end:].strip():
        raise RuntimeError("Unexpected trailing SDK JSON output")
    return value


def runtime_selection(evaluated):
    properties = evaluated["Properties"]
    if properties["TargetFramework"] != "net10.0" or properties["RuntimeIdentifier"] != "browser-wasm" or properties["NETCoreSdkVersion"] != "10.0.401":
        raise RuntimeError("Actual SDK/TFM/RID differs")
    selected = []
    for item in evaluated["Items"]["ResolvedRuntimePack"]:
        directory = Path(item["PackageDirectory"])
        if directory.parent.name.lower() == "microsoft.netcore.app.runtime.mono.browser-wasm":
            selected.append((directory, item["NuGetPackageVersion"]))
    if len(selected) != 1 or selected[0][0].name != "10.0.12" or selected[0][1] != "10.0.12":
        raise RuntimeError("Actual unique selected browser runtime pack10.0.12 absent")
    directory = selected[0][0]
    if properties["_WasmRuntimePackVersion"] != "10.0.12" or Path(properties["MicrosoftNetCoreAppRuntimePackDir"]).resolve() != directory.resolve():
        raise RuntimeError("Actual WASM build-selected runtime version/path differs")
    native_directory = directory / "runtimes/browser-wasm/native"
    if Path(properties["MicrosoftNetCoreAppRuntimePackRidNativeDir"]).resolve() != native_directory.resolve():
        raise RuntimeError("Actual WASM native runtime directory differs")
    runtime_js = native_directory / "dotnet.js"
    if not runtime_js.is_file():
        raise RuntimeError("Actual selected runtime dotnet.js absent")
    native = []
    for item in evaluated["Items"]["NativeFileReference"]:
        path = Path(item["FullPath"])
        if path.stem in ("libSkiaSharp", "libHarfBuzzSharp"):
            if not path.is_file():
                raise RuntimeError("Resolved native input absent")
            native.append({"path": str(path), "bytes": path.stat().st_size, "sha256": sha(path)})
    if not {"libSkiaSharp", "libHarfBuzzSharp"}.issubset(Path(x["path"]).stem for x in native):
        raise RuntimeError("Genuine source/native library closure missing")
    return {"runtimePack": str(directory), "runtimeVersion": selected[0][1],
            "runtimeLoader": {"path": str(runtime_js), "bytes": runtime_js.stat().st_size, "sha256": sha(runtime_js)},
            "nativeLibraries": native}


def source_snapshot(root, tree_log):
    # Actual tracked git tree from the current checkout; no private profile scan.
    rows = []
    for line in tree_log.split("\0"):
        if not line:
            continue
        fields, name = line.split("\t", 1)
        if not any(name == scope or name.startswith(scope + "/") for scope in SOURCE_ROOTS):
            continue
        if Path(name).suffix not in SOURCE_EXT:
            continue
        mode, kind, blob = fields.split()
        if kind != "blob":
            continue
        path = root / name
        if not path.is_file() or path.is_symlink():
            raise RuntimeError("Tracked source regular file missing: " + name)
        rows.append({"path": name, "gitBlob": blob, "bytes": path.stat().st_size, "sha256": sha(path)})
    rows.sort(key=lambda x: x["path"])
    if not set(LINKED_CUTS).issubset(x["path"] for x in rows):
        raise RuntimeError("Original linked owner source custody is incomplete")
    return rows


def public_inventory(directory):
    if not directory.is_dir() or directory.is_symlink():
        raise RuntimeError("Normal SDK public wwwroot directory absent")
    rows = []
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            raise RuntimeError("Public bundle link rejected")
        if path.is_dir():
            continue
        if not path.is_file():
            raise RuntimeError("Nonregular public artifact")
        name = path.relative_to(directory).as_posix()
        lower = name.lower()
        if any(part in {".git", ".nuget", "node_modules", "obj", "fixture-data", "profiles"} for part in lower.split("/")) or Path(lower).suffix in {".nupkg", ".pfx", ".p12", ".pem", ".key"} or Path(lower).name.startswith(".env"):
            raise RuntimeError("Cache/private/package material is not a public bundle: " + name)
        rows.append({"path": name, "bytes": path.stat().st_size, "sha256": sha(path)})
    names = {x["path"] for x in rows}
    if not {"index.html", "main.js", "_framework/dotnet.js", "_framework/avalonia.js", "_framework/storage.js"}.issubset(names):
        raise RuntimeError("Actual browser bootstrap incomplete")
    if not any(name.startswith("_framework/") and "dotnet.native" in name and name.endswith(".wasm") for name in names):
        raise RuntimeError("Actual SDK native WASM output missing")
    if sum(x["bytes"] for x in rows) > PUBLIC_LIMIT:
        raise RuntimeError("Public uncompressed bundle exceeds bounded128MiB artifact")
    return rows


def create_public_zip(directory, destination, rows):
    if destination.exists():
        raise RuntimeError("Require fresh public ZIP")
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for row in rows:
            path = directory / row["path"]
            if path.stat().st_size != row["bytes"] or sha(path) != row["sha256"]:
                raise RuntimeError("Public file changed before ZIP")
            archive.write(path, row["path"])
    if destination.stat().st_size > PUBLIC_LIMIT:
        raise RuntimeError("Public ZIP exceeds bounded128MiB artifact")
    with zipfile.ZipFile(destination) as archive:
        if archive.testzip() is not None or set(archive.namelist()) != {x["path"] for x in rows} or len(archive.namelist()) != len(rows):
            raise RuntimeError("Public ZIP complete CRC/member custody failed")
        for row in rows:
            data = archive.read(row["path"])
            if len(data) != row["bytes"] or hashlib.sha256(data).hexdigest() != row["sha256"]:
                raise RuntimeError("Public ZIP body differs from actual SDK output")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--package-cache", type=Path, help="Reuse an existing owned NuGet package cache; all build outputs remain isolated")
    args = parser.parse_args()
    root, output = Path.cwd().resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(root):
        raise RuntimeError("Fresh output outside checkout required")
    common_path = root / "apps/Web/Tests/ci/run-ordinary-native.py"
    if sha(common_path) != COMMON_SHA256:
        raise RuntimeError("Reviewed current Commands source changed")
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location("team_b_native_commands", common_path)
    common = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(common)
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    env = os.environ.copy()
    for variable, directory in {"DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget", "NUGET_HTTP_CACHE_PATH": "http",
            "NUGET_PLUGINS_CACHE_PATH": "plugins", "XDG_CACHE_HOME": "font-cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp"}.items():
        path = output / directory
        path.mkdir(exist_ok=True)
        env[variable] = str(path)
    if args.package_cache is not None:
        cache = args.package_cache.absolute()
        if not cache.is_dir() or cache.is_symlink() or cache.resolve().is_relative_to(root):
            raise RuntimeError("Existing regular package cache outside checkout required")
        env["NUGET_PACKAGES"] = str(cache.resolve())
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")
    commands = common.Commands(diagnostics, env, root)
    result = {"status": "NOT_RUN", "sourceCommit": None, "packageCache": env["NUGET_PACKAGES"],
              "scope": "Normal source build/publish and public artifact custody; browser/runtime/provider/deployment/full parity NOT_RUN"}
    before, after = [], []
    try:
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Actual checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        tree = commands.run("tracked-source-tree", ["git", "ls-tree", "-rz", "HEAD"], 15)
        before = source_snapshot(root, tree)
        write_json(diagnostics / "source-before.json", before)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX", "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Exact SDK10.0.401 required")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        commands.run("wasm-tools-install", ["dotnet", "workload", "install", "wasm-tools", "--skip-manifest-update",
                     "--configfile", str(root / "NuGet.Config")], 600)
        commands.run("wasm-tools-list", ["dotnet", "workload", "list"], 30)
        tools = output / "tools"
        commands.run("bun-tool-install", ["dotnet", "tool", "install", "Bun.Unofficial.Tool", "--version", "1.3.4",
                     "--tool-path", str(tools), "--configfile", str(root / "NuGet.Config")], 180)
        bun = str(tools / "bun")
        if commands.run("bun-version", [bun, "--version"], 30).strip() != "1.3.4":
            raise RuntimeError("Original selected Bun1.3.4 tool required")
        webapp = root / "framework/CUI/vendor/Avalonia/src/Browser/Avalonia.Browser/webapp"
        commands.cwd = webapp
        try:
            commands.run("locked-assets-install", [bun, "install", "--frozen-lockfile"], 180)
            commands.run("owner-assets-build", [bun, "build.js"], 120)
        finally:
            commands.cwd = root
        static = webapp.parent / "staticwebassets"
        asset_rows = [{"path": x.relative_to(root).as_posix(), "bytes": x.stat().st_size, "sha256": sha(x)}
                      for x in sorted(static.rglob("*")) if x.is_file()]
        if not {"avalonia.js", "storage.js"}.issubset(x.name for x in static.iterdir() if x.is_file()):
            raise RuntimeError("Actual owner build.js asset exports missing")
        write_json(diagnostics / "owner-built-assets.json", asset_rows)
        project = "apps/Web/NineToOne.Web.csproj"
        artifacts = output / "artifacts"
        task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:AvaloniaBuildTasksLocation=" + str(task), "-p:UseSharedCompilation=false"]
        commands.run("restore", ["dotnet", "restore", project, "--artifacts-path", str(artifacts),
                     "--configfile", str(root / "NuGet.Config"), "--disable-build-servers",
                     "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + props, 300)
        assets = artifacts / "obj/NineToOne.Web/project.assets.json"
        libraries = json.loads(assets.read_text())["libraries"]
        if "Microsoft.NET.Sdk.WebAssembly.Pack/10.0.12" not in libraries:
            raise RuntimeError("Actual SDK WebAssembly pack must be10.0.12")
        # Query only after the genuine public Build graph has initialized the
        # selected WASM runtime; ResolveRuntimePackAssets alone was insufficient.
        # This is the single ordinary no-restore source build, not a second build
        # or a private-target/runtime-path substitute.
        evaluated = sdk_json(commands.run("build", ["dotnet", "msbuild", project,
                     "-p:Configuration=Release", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts),
                     "-target:Build", "-getItem:ResolvedRuntimePack,NativeFileReference",
                     "-getProperty:TargetFramework,RuntimeIdentifier,NETCoreSdkVersion,MicrosoftNetCoreAppRuntimePackDir,MicrosoftNetCoreAppRuntimePackRidNativeDir,_WasmRuntimePackVersion",
                     "-m:1", "-nodeReuse:false"] + props, 600))
        write_json(diagnostics / "runtime-pack-properties.json", evaluated)
        write_json(diagnostics / "actual-runtime-and-native-inputs.json", runtime_selection(evaluated))
        public_root = output / "sdk-publish"
        if public_root.exists():
            raise RuntimeError("Fresh SDK publication destination required")
        command = ["dotnet", "publish", project, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts),
                   "--output", str(public_root), "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props
        commands.run("publish", command, 600)
        rows = public_inventory(public_root / "wwwroot")
        published = {row["path"]: row for row in rows}
        for name in ("avalonia.js", "storage.js"):
            source_asset = static / name
            exported = published["_framework/" + name]
            if exported["bytes"] != source_asset.stat().st_size or exported["sha256"] != sha(source_asset):
                raise RuntimeError("Published owner browser module differs from actual build.js output: " + name)
        after = source_snapshot(root, tree)
        write_json(diagnostics / "source-after.json", after)
        commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        if before != after or commands.run("git-head-after", ["git", "rev-parse", "HEAD"], 15).strip() != head:
            raise RuntimeError("Source custody changed during genuine build/publication")
        public_artifact = output / "public-artifact"
        public_artifact.mkdir()
        receipt = {"sourceCommit": head, "sourceCommitAfter": head, "scope": result["scope"],
                   "exitCode": 0, "command": command, "sourceInputs": len(before), "sourceInputsAfter": len(after),
                   "sourceCustodyRoots": SOURCE_ROOTS, "requiredLinkedOwnerCuts": LINKED_CUTS,
                   "addedSourceInputs": [], "removedSourceInputs": [], "changedSourceInputs": [], "trackedChangesAfter": [],
                   "publishRoot": str(public_root / "wwwroot"), "publishFiles": rows, "fileCount": len(rows),
                   "totalBytes": sum(x["bytes"] for x in rows)}
        receipt_path = public_artifact / "publish-manifest.json"
        write_json(receipt_path, receipt)
        commands.run("receipt-verify", ["node", "apps/Web/Tests/verify-publish-receipt.cjs", str(receipt_path),
                     str(public_root / "wwwroot"), head, sha(receipt_path)], 30)
        zip_path = public_artifact / "wwwroot.zip"
        create_public_zip(public_root / "wwwroot", zip_path, rows)
        if public_inventory(public_root / "wwwroot") != rows:
            raise RuntimeError("Public SDK output changed during seal")
        seal = {"sourceCommit": head, "receiptSha256": sha(receipt_path), "zipSha256": sha(zip_path),
                "zipBytes": zip_path.stat().st_size, "fileCount": len(rows), "totalBytes": receipt["totalBytes"],
                "runtimeVerified": False, "deploymentVerified": False, "fullParityVerified": False}
        write_json(public_artifact / "seal.json", seal)
        result.update(status="PASS", seal=seal)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        try:
            if before:
                after = source_snapshot(root, tree)
                write_json(diagnostics / "source-after-final.json", after)
                if before != after:
                    result.update(status="FAIL", sourceCustodyError="Final tracked source snapshot changed")
            commands.run("git-clean-final", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        except Exception as error:
            result.update(status="FAIL", finalCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
