"""Build/stage the existing Rnote bridge on an admitted native ARM host.

No upload, installation, library loading or application execution. Static inspection
and a corresponding-source archive do not establish drawing/runtime acceptance.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import struct
import subprocess
import tarfile
import tempfile
import tomllib

CRATE = Path("9to1 Workspace/Canvas/rnote-poc")
MANAGED = Path("9to1 Workspace/Canvas/RnoteCanvasEngine.cs")
RASTERIZER = Path("9to1 Workspace/Canvas/CanvasSvgRasterizer.cs")
LIBRARY = "libcakeos_canvas_rnote_poc.so"
SCHEMA = "9to1.rnote-native-stage/v1"
DLOPEN = ("librsvg-2.so.2", "libgobject-2.0.so.0", "libglib-2.0.so.0", "libcairo.so.2")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def identity(path):
    path = Path(path)
    require(not path.is_symlink() and path.is_file(), "Expected regular input: " + str(path))
    with path.open("rb") as stream:
        before = os.fstat(stream.fileno())
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        after = os.fstat(stream.fileno())
    require((before.st_ino, before.st_size, before.st_mtime_ns) ==
            (after.st_ino, after.st_size, after.st_mtime_ns), "Input changed during hashing")
    return {"bytes": after.st_size, "sha256": digest}


def command(arguments, cwd=None):
    result = subprocess.run(arguments, cwd=cwd, capture_output=True, text=True,
                            timeout=120, check=True)
    return result.stdout.strip()


def source_contract(root):
    root = Path(root)
    manifest = tomllib.loads((root / CRATE / "Cargo.toml").read_text())
    require(manifest["package"]["name"] == "cakeos-canvas-rnote-poc" and
            "cdylib" in manifest["lib"]["crate-type"], "Wrong native bridge crate")
    require((root / CRATE / "Cargo.lock").is_file(), "Committed Cargo.lock required")
    # Pin all actual crate/build/header inputs, rather than a hand-maintained list.
    names = sorted(p.relative_to(root).as_posix() for p in (root / CRATE).rglob("*") if p.is_file())
    require(not any("target" in Path(name).relative_to(CRATE).parts for name in names),
            "Crate tree contains generated targets; use an external target directory")
    names += [MANAGED.as_posix(), RASTERIZER.as_posix()]
    inputs = {name: identity(root / name) for name in names}
    managed = (root / MANAGED).read_text()
    symbols = sorted(set(re.findall(r'EntryPoint\s*=\s*"(cake_canvas_[a-z0-9_]+)"', managed)))
    rust = (root / CRATE / "src/ffi.rs").read_text()
    exported = set(re.findall(r'pub\s+(?:unsafe\s+)?extern\s+"C"\s+fn\s+(cake_canvas_\w+)', rust))
    require(symbols and set(symbols) <= exported, "Managed Rnote ABI is absent from current native source: " + ", ".join(sorted(set(symbols) - exported)))
    abi = re.search(r'Native\.AbiVersion\(\)\s*!=\s*(\d+)', managed)
    header = (root / CRATE / "include/cakeos_canvas.h").read_text()
    require(abi and re.search(r'CAKE_CANVAS_ABI_VERSION\s*:\s*u32\s*=\s*' + abi[1] + r'\s*;', rust)
            and re.search(r'#define\s+CAKE_CANVAS_ABI_VERSION\s+' + abi[1] + r'\b', header),
            "Managed/header/native ABI version mismatch")
    raster = (root / RASTERIZER).read_text()
    require(all('"' + name + '"' in raster for name in DLOPEN), "Rasterizer dlopen contract changed")
    return {"source_inputs": inputs, "required_symbols": symbols, "abi_version": int(abi[1]),
            "crate_license": manifest["package"]["license"],
            "minimum_rust": manifest["package"]["rust-version"]}


def elf_arm64(path):
    with Path(path).open("rb") as stream:
        header = stream.read(20)
    require(len(header) == 20 and header[:6] == b"\x7fELF\x02\x01" and
            struct.unpack_from("<HH", header, 16) == (3, 183),
            "Expected little-endian ARM64 shared object: " + str(path))


def inspect_library(library, required_symbols, execute=command):
    elf_arm64(library)
    symbols = set()
    for line in execute(["readelf", "--dyn-syms", "--wide", str(library)]).splitlines():
        fields = line.split()
        if len(fields) >= 8 and fields[3] == "FUNC" and fields[4] in ("GLOBAL", "WEAK") and fields[5] in ("DEFAULT", "PROTECTED") and fields[6] != "UND":
            symbols.add(fields[7].split("@")[0])
    require(set(required_symbols) <= symbols, "Native bridge missing defined public managed ABI symbols: " +
            ", ".join(sorted(set(required_symbols) - symbols)))
    dynamic = execute(["readelf", "--dynamic", "--wide", str(library)])
    require(not re.search(r'\((?:RPATH|RUNPATH)\)', dynamic), "Unreviewed Rnote runtime search path")
    needed = sorted(set(re.findall(r'\(NEEDED\).*?\[([^]]+)\]', dynamic)))
    require(needed, "Native bridge has no observed dynamic dependency graph")
    require(all(re.fullmatch(r'[A-Za-z0-9_+.-]+', name) for name in needed), "Unsafe DT_NEEDED name")
    lookup = {}
    for line in execute(["ldconfig", "-p"]).splitlines():
        match = re.fullmatch(r'\s*(\S+)\s+\([^)]*\)\s+=>\s+(/\S+)', line)
        if match:
            lookup.setdefault(match[1], []).append(match[2])
    providers = []
    for soname in sorted(set(needed) | set(DLOPEN)):
        candidates = [Path(value) for value in lookup.get(soname, [])]
        require(candidates, "Missing native runtime provider: " + soname)
        valid = {}
        for candidate in candidates:
            try:
                elf_arm64(candidate)
                valid.setdefault(candidate.resolve(strict=True), candidate)
            except (OSError, ValueError):
                continue
        require(len(valid) == 1, "Missing or ambiguous ARM64 provider: " + soname)
        path = next(iter(valid.values()))
        # Query the loader path and its canonical equivalent (usr-merge differs).
        owners = set()
        for query in dict.fromkeys((str(path), str(path.resolve(strict=True)))):
            try:
                lines = execute(["dpkg-query", "-S", query]).splitlines()
            except subprocess.CalledProcessError:
                continue
            for line in lines:
                owner, separator, owned_path = line.partition(": ")
                if separator and owned_path == query:
                    owners.update(owner.split(", "))
        require(len(owners) == 1, "Unowned or ambiguous runtime provider: " + soname)
        owner = next(iter(owners))
        row = execute(["dpkg-query", "-W", "-f=${binary:Package}\t${Version}\t${Architecture}", owner]).split("\t")
        require(len(row) == 3 and row[2] == "arm64", "Runtime package is not ARM64: " + owner)
        package, version = row[0].removesuffix(":arm64"), row[1]
        require(re.fullmatch(r'[a-z0-9][a-z0-9+.-]+', package) and
                re.fullmatch(r'[0-9][A-Za-z0-9.+:~_-]*', version), "Malformed runtime package/version")
        providers.append({"soname": soname, "path": str(path.resolve(strict=True)),
                          "package": package, "version": version, **identity(path.resolve(strict=True))})
    # Conservative version floors use the actual installed ARM provider, never an
    # unversioned guessed dependency or a host-independent receipt claim.
    dependencies = sorted({p["package"] + " (>= " + p["version"] + ")" for p in providers})
    return {"library": identity(library), "required_symbols": sorted(required_symbols),
            "defined_required_symbols": sorted(set(required_symbols) & symbols),
            "dt_needed": needed, "dlopen": list(DLOPEN), "providers": providers,
            "dependencies": dependencies, "runtime_execution": "NOT RUN"}


def validate_stage(root, publish, receipt_path, execute=command):
    receipt_path = Path(receipt_path)
    require(identity(receipt_path)["bytes"] <= 2 * 1024 * 1024, "Oversized native receipt")
    value = json.loads(receipt_path.read_text())
    contract = source_contract(root)
    require(value.get("schema") == SCHEMA and value.get("status") == "STAGED_STATICALLY_VERIFIED_NOT_RUNTIME_ACCEPTED",
            "Missing native staging receipt")
    require(value.get("source_head") == execute(["git", "-C", str(root), "rev-parse", "HEAD"]), "Wrong Rnote source revision")
    require(value.get("contract") == contract, "Rnote source/ABI changed after build")
    require(value.get("inspection") == inspect_library(Path(publish) / LIBRARY, contract["required_symbols"], execute),
            "Native library/dependency graph differs from staging receipt")
    for label, name in (("licence", CRATE / "patches/LICENSE-GPL-3.0-or-later.txt"), ("provenance", CRATE / "PROVENANCE.md")):
        item = value.get(label, {})
        require(Path(item.get("path", "")).resolve() == (Path(root) / name).resolve()
                and item.get("identity") == contract["source_inputs"][name.as_posix()]
                and item.get("identity") == identity(item.get("path", "")), "Missing or changed Rnote " + label)
    source = value.get("corresponding_source", {})
    require(source.get("identity") == identity(source.get("path", "")), "Missing or changed native corresponding source")
    return value


def verify_package(package, receipt, receipt_path, execute=command):
    """Stream the genuine DEB payload; never extract/execute or trust a stage listing."""
    expected = {"opt/9to1-preview/" + LIBRARY: receipt["inspection"]["library"],
                "usr/share/doc/9to1-preview/rnote-native-stage.json": identity(receipt_path),
                "usr/share/doc/9to1-preview/rnote-LICENSE-GPL-3.0-or-later.txt": receipt["licence"]["identity"],
                "usr/share/doc/9to1-preview/rnote-PROVENANCE.md": receipt["provenance"]["identity"]}
    found = {}
    process = subprocess.Popen(["dpkg-deb", "--fsys-tarfile", str(package)], stdout=subprocess.PIPE,
                               stderr=subprocess.DEVNULL)
    try:
        with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
            for member in archive:
                name = member.name.removeprefix("./")
                if name not in expected:
                    continue
                require(name not in found and member.isfile() and member.size == expected[name]["bytes"],
                        "Missing/duplicate/nonregular/wrong-size native package member")
                stream = archive.extractfile(member)
                found[name] = {"bytes": member.size, "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}
        require(process.wait(timeout=30) == 0, "Could not inspect actual DEB payload")
    finally:
        process.stdout.close()
        if process.poll() is None:
            process.kill()
            process.wait()
    require(found == expected, "Assembled DEB omitted or changed the native library/receipt")
    depends = execute(["dpkg-deb", "--field", str(package), "Depends"])
    declared = {value.strip() for value in depends.split(",")}
    require(set(receipt["inspection"]["dependencies"]) <= declared, "Assembled package omitted native runtime dependency floors")
    return {"status": "PACKAGE_NATIVE_BYTES_AND_DEPENDENCIES_VERIFIED_NOT_EXECUTED", "members": found,
            "dependencies": receipt["inspection"]["dependencies"], "native_runtime": "NOT RUN"}


def build(root, publish, target, receipt_path, source_archive):
    require(platform.machine() == "aarch64" and command(["dpkg", "--print-architecture"]) == "arm64",
            "Actual native ARM64 build host required")
    contract = source_contract(root)
    toolchain = command(["rustc", "-vV"])
    version = re.search(r'^release: (\d+)\.(\d+)\.(\d+)', toolchain, re.M)
    require(version and tuple(map(int, version.groups())) >= tuple(map(int, (contract["minimum_rust"] + ".0").split(".")))
            and re.search(r'^host: aarch64-unknown-linux-gnu$', toolchain, re.M), "Native Rust toolchain does not meet crate requirements")
    require(not Path(target).exists() and not Path(receipt_path).exists() and not Path(source_archive).exists()
            and not (Path(publish) / LIBRARY).exists(), "Fresh Rnote output paths required")
    Path(target).mkdir(mode=0o700)
    for name in ("cairo", "pango", "pangocairo", "glib-2.0", "gio-2.0", "gobject-2.0", "librsvg-2.0"):
        command(["pkg-config", "--modversion", name])
    with tempfile.TemporaryDirectory(prefix="rnote-source-", dir=target) as scratch:
        scratch = Path(scratch)
        vendor = scratch / "vendor"
        vendor_command = ["cargo", "vendor", "--locked", "--versioned-dirs", "--manifest-path", str(Path(root) / CRATE / "Cargo.toml"), str(vendor)]
        vendor_result = subprocess.run(vendor_command, stdout=subprocess.PIPE, text=True, check=True, timeout=1800)
        config = vendor_result.stdout.strip()
        from rnote_source_patches import apply_vendor_patches
        derivative = apply_vendor_patches(root, vendor)
        require(config, "Cargo vendor returned no locked source configuration")
        config_path = scratch / "vendor-config.toml"
        config_path.write_text(config)
        build_command = ["cargo", "build", "--frozen", "--release", "--jobs", "1", "--manifest-path", str(Path(root) / CRATE / "Cargo.toml"), "--target-dir", str(target), "--target", "aarch64-unknown-linux-gnu", "--config", str(config_path)]
        subprocess.run(build_command, check=True, timeout=3600)
        library = Path(target) / "aarch64-unknown-linux-gnu/release" / LIBRARY
        inspection = inspect_library(library, contract["required_symbols"])
        require(source_contract(root) == contract, "Rnote inputs/lock changed during build")
        # Retain full locked dependency source/licences, not just Cargo.lock or a URL.
        with tarfile.open(source_archive, "x:gz", dereference=False) as archive:
            for path in sorted(vendor.rglob("*")):
                require(not path.is_symlink(), "Unexpected vendor source symlink")
                if path.is_file():
                    archive.add(path, arcname="rnote-source/vendor/" + path.relative_to(vendor).as_posix(), recursive=False)
            for name in contract["source_inputs"]:
                archive.add(Path(root) / name, arcname="rnote-source/repository/" + name, recursive=False)
            portable = scratch / "vendor-config.portable.toml"
            portable.write_text(config.replace(str(vendor), "vendor"))
            archive.add(portable, arcname="rnote-source/vendor-config.toml", recursive=False)
        shutil.copyfile(library, Path(publish) / LIBRARY)
        (Path(publish) / LIBRARY).chmod(0o755)
    value = {"schema": SCHEMA, "status": "STAGED_STATICALLY_VERIFIED_NOT_RUNTIME_ACCEPTED",
             "source_head": command(["git", "-C", str(root), "rev-parse", "HEAD"]), "contract": contract,
             "toolchain": toolchain, "commands": [vendor_command, build_command], "inspection": inspection,
             "donor_source_patches": derivative,
             "corresponding_source": {"path": str(Path(source_archive).resolve()), "identity": identity(source_archive),
                                      "content": "Exact crate/managed inputs and Cargo-vendored locked dependencies including licences"},
             "licence": {"path": str(Path(root) / CRATE / "patches/LICENSE-GPL-3.0-or-later.txt"),
                         "identity": identity(Path(root) / CRATE / "patches/LICENSE-GPL-3.0-or-later.txt")},
             "provenance": {"path": str(Path(root) / CRATE / "PROVENANCE.md"), "identity": identity(Path(root) / CRATE / "PROVENANCE.md")},
             "donor_licence_review": "GPL-3.0-or-later text/source retained; independent full dependency licence review NOT RUN",
             "ARM_drawing": "NOT RUN", "release_ready": False}
    with Path(receipt_path).open("x") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--verify-source-only", action="store_true")
    for option in ("publish", "target", "receipt", "source-archive"):
        parser.add_argument("--" + option, type=Path)
    args = parser.parse_args()
    if args.verify_source_only:
        contract = source_contract(args.root.resolve())
        print(json.dumps({"status": "SOURCE_ABI_CONTRACT_CHECKED_NOT_BUILT", "contract": contract, "native_execution": "NOT RUN"}))
    else:
        require(all((args.publish, args.target, args.receipt, args.source_archive)), "Build needs publish/target/receipt/source-archive")
        build(args.root.resolve(), args.publish.resolve(), args.target.resolve(), args.receipt.resolve(), args.source_archive.resolve())


if __name__ == "__main__":
    main()
