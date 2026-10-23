#!/usr/bin/env python3
"""Inspect an engine-only ELF artifact or installed build prerequisites.

No third-party Python modules are required. Runtime ABI values and round trips need
separate native tests. Hosted images and apt observations are NOT immutable inputs.
Build preflight never acquires inputs, invokes Cargo builds or loads a library.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import struct
import subprocess
import tomllib

CORE = frozenset("""cake_canvas_abi_version cake_canvas_engine_new
cake_canvas_engine_free cake_canvas_engine_from_rnote cake_canvas_begin_stroke
cake_canvas_update_stroke cake_canvas_end_stroke cake_canvas_set_pen_style
cake_canvas_set_stroke_tool cake_canvas_set_eraser cake_canvas_undo cake_canvas_redo
cake_canvas_save_rnote cake_canvas_buffer_release cake_canvas_render_frame
cake_canvas_render_frame_release""".split())
MACHINES = {"x86_64-unknown-linux-gnu": 62, "aarch64-unknown-linux-gnu": 183}
CANVAS = Path("9to1 Workspace/Canvas")
MAX_SNAPSHOT_BYTES = 512 * 1024 * 1024


def read_snapshot(path: Path) -> bytes:
    """Own bounded regular-file bytes through one descriptor, not a second path read.

    NONBLOCK makes a replacement FIFO refuse after fstat rather than hanging open.
    This is a byte snapshot, not a claim that another process cannot modify a file.
    """
    descriptor = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NONBLOCK)
    try:
        metadata = os.fstat(descriptor)
        if not stat.S_ISREG(metadata.st_mode):
            raise ValueError("Inspection requires a regular file")
        if metadata.st_size > MAX_SNAPSHOT_BYTES:
            raise ValueError("File exceeds 512 MiB inspection bound")
        with os.fdopen(descriptor, "rb", closefd=False) as source:
            data = source.read(MAX_SNAPSHOT_BYTES + 1)
        if len(data) > MAX_SNAPSHOT_BYTES:
            raise ValueError("File exceeds 512 MiB inspection bound")
        return data
    finally:
        os.close(descriptor)


def receipt(path: Path, *, snapshot: bytes | None = None) -> dict:
    data = read_snapshot(path) if snapshot is None else snapshot
    if not isinstance(data, bytes):
        raise TypeError("An owned immutable byte snapshot is required")
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def inspect_elf(path: Path, *, snapshot: bytes | None = None) -> dict:
    """Parse and hash the same ELF64 LE bytes; no library loading or execution."""
    data = read_snapshot(path) if snapshot is None else snapshot
    if not isinstance(data, bytes):
        raise TypeError("An owned immutable byte snapshot is required")
    if len(data) > MAX_SNAPSHOT_BYTES:
        raise ValueError("ELF exceeds 512 MiB inspection bound")

    def unpack(fmt: str, offset: int):
        size = struct.calcsize(fmt)
        if offset < 0 or offset + size > len(data):
            raise ValueError("Truncated ELF structure")
        return struct.unpack_from(fmt, data, offset)

    if len(data) < 64 or data[:7] != b"\x7fELF\x02\x01\x01":
        raise ValueError("Expected ELF64 little-endian version 1")
    kind, machine = unpack("<HH", 16)
    if unpack("<I", 20)[0] != 1 or unpack("<H", 52)[0] != 64:
        raise ValueError("Unsupported ELF version/header size")
    if kind != 3:
        raise ValueError("Expected an ET_DYN shared object")
    section_offset = unpack("<Q", 40)[0]
    section_size, section_count = unpack("<HH", 58)
    if section_size != 64 or not 1 <= section_count <= 8192:
        raise ValueError("Unsupported/missing ELF section table")
    sections = [unpack("<IIQQQQIIQQ", section_offset + i * 64) for i in range(section_count)]

    def section_bytes(index: int) -> bytes:
        if not 0 <= index < len(sections):
            raise ValueError("ELF string-table link is out of bounds")
        section = sections[index]
        offset, size = section[4:6]
        if offset + size > len(data):
            raise ValueError("Truncated ELF section")
        return data[offset:offset + size]

    def string(table: bytes, offset: int) -> str:
        if not 0 <= offset < len(table):
            raise ValueError("ELF string offset is out of bounds")
        end = table.find(b"\0", offset)
        if end < 0:
            raise ValueError("Unterminated ELF string")
        return table[offset:end].decode("utf-8", errors="strict")

    exports, needed, search_paths = set(), [], []
    soname = None
    for index, section in enumerate(sections):
        kind, size, link, entry_size = section[1], section[5], section[6], section[9]
        if kind not in (6, 11):  # SHT_DYNAMIC, SHT_DYNSYM
            continue
        expected = 16 if kind == 6 else 24
        if entry_size != expected or size % expected or size // expected > 1_000_000:
            raise ValueError("Malformed ELF dynamic/symbol entries")
        table, entries = section_bytes(link), section_bytes(index)
        for offset in range(0, len(entries), expected):
            if kind == 6:
                tag, value = struct.unpack_from("<qQ", entries, offset)
                if tag == 0:
                    break
                if tag == 1:
                    needed.append(string(table, value))
                elif tag == 14:
                    soname = string(table, value)
                elif tag in (15, 29):
                    search_paths.append(string(table, value))
            else:
                name, info, visibility, definition, _, _ = struct.unpack_from("<IBBHQQ", entries, offset)
                if definition and info >> 4 in (1, 2) and info & 15 == 2 and visibility & 3 == 0:
                    exports.add(string(table, name))
    return {"machine": machine, "exports": sorted(exports), "needed": needed,
            "soname": soname, "rpath_runpath": search_paths, **receipt(path, snapshot=data)}


def dependency_closure(library: Path, machine: int, directories: list[Path], copy_to: Path | None = None) -> list[dict]:
    """Resolve explicit native search roots only; reject loader semantics we do not model."""
    pending, seen, result = [library.resolve(strict=True)], set(), []
    while pending:
        path = pending.pop()
        if path in seen:
            continue
        if len(seen) >= 256:
            raise ValueError("Native dependency closure exceeds 256-object bound")
        seen.add(path)
        snapshot = read_snapshot(path)
        item = inspect_elf(path, snapshot=snapshot)
        if item["machine"] != machine:
            raise ValueError(f"Wrong ELF architecture: {path}")
        if item["rpath_runpath"]:
            raise ValueError(f"Unmodelled RPATH/RUNPATH needs explicit loader acceptance: {path}")
        edges = []
        for name in item["needed"]:
            if not name or "/" in name or name in (".", ".."):
                raise ValueError("DT_NEEDED must be a bounded library name")
            target = next((directory / name for directory in directories if (directory / name).is_file()), None)
            if target is None:
                raise ValueError(f"Unresolved DT_NEEDED {name} from {path}")
            target = target.resolve(strict=True)
            edges.append({"name": name, "resolved": str(target)})
            pending.append(target)
        item["dependencies"] = edges
        if copy_to is not None:
            destination = copy_to / item["sha256"] / path.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            # Retain exactly what was parsed/hashed, even if the original path changed.
            destination.write_bytes(snapshot)
            item["retained_copy"] = str(destination)
        result.append(item)
    return sorted(result, key=lambda item: item["path"])


def source_policy(repo: Path) -> dict:
    manifest_path, lock_path = (repo / CANVAS / "rnote-poc" / name for name in ("Cargo.toml", "Cargo.lock"))
    managed, donor = repo / CANVAS / "RnoteCanvasEngine.cs", repo / "eng/donor-sources.json"
    inputs = {path: read_snapshot(path) for path in (manifest_path, lock_path, managed, donor)}
    manifest, lock = (tomllib.loads(inputs[path].decode("utf-8")) for path in (manifest_path, lock_path))
    policy = json.loads(inputs[donor])["upstreams"]["rnote"]
    selected = policy["commit"]
    if not re.fullmatch(r"[0-9a-f]{40}", selected):
        raise ValueError("Selected Rnote revision is not an immutable commit")
    pins = {}
    for name in ("rnote-engine", "rnote-compose"):
        declared = manifest["dependencies"][name]
        matches = [package for package in lock["package"] if package["name"] == name]
        if len(matches) != 1:
            raise ValueError(f"Ambiguous locked package {name}")
        source = matches[0].get("source", "")
        revision = declared.get("rev", "")
        if not re.fullmatch(r"[0-9a-f]{40}", revision) or source != f"git+{declared['git']}?rev={revision}#{revision}":
            raise ValueError(f"Manifest/lock exact source mismatch for {name}")
        pins[name] = revision
    for package in lock["package"]:
        if package.get("source", "").startswith("registry+") and not re.fullmatch(r"[0-9a-f]{64}", package.get("checksum", "")):
            raise ValueError(f"Registry checksum absent/invalid: {package['name']}")
    imports = sorted(set(re.findall(r'EntryPoint\s*=\s*"(cake_canvas_[^\"]+)"', inputs[managed].decode("utf-8"))))
    if not CORE <= set(imports):
        raise ValueError("Managed boundary no longer contains the audited core ABI")
    return {"selected_donor": selected, "locked_sources": pins,
            "selected_matches_locked": all(value == selected for value in pins.values()),
            "managed_imports": imports, "core_imports": sorted(CORE),
            "source_inputs": [receipt(path, snapshot=data) for path, data in inputs.items()],
            "runtime_abi_version_execution": "NOT RUN by artifact inspector"}


def numeric_version(value: str) -> tuple[int, int, int]:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9]+\.[0-9]+(?:\.[0-9]+)?", value):
        raise ValueError(f"Unsupported numeric version: {value!r}")
    return tuple(int(part) for part in (value.split(".") + ["0"])[:3])


def observe(command: list[str]) -> dict:
    """Only installed version-query commands; never invoke a shell or acquire inputs."""
    try:
        result = subprocess.run(command, stdin=subprocess.DEVNULL, capture_output=True,
                                text=True, timeout=10, check=False)
        return {"command": command, "exit_code": result.returncode,
                "stdout": result.stdout[:65536], "stderr": result.stderr[:65536]}
    except (OSError, UnicodeError, subprocess.SubprocessError) as error:
        return {"command": command, "exit_code": None,
                "error": {"type": type(error).__name__, "message": str(error)}}


def installed_tool(path: Path | None, tool: str) -> dict:
    # Calling a rustup proxy can install its selected toolchain. Explicit actual
    # binaries are mandatory; CI obtains these with `rustup which` after setup.
    if path is None:
        raise ValueError(f"Explicit installed {tool} binary is required; no toolchain is acquired")
    actual = path.resolve(strict=True)
    rustup = shutil.which("rustup")
    # rustup normally installs hardlink proxies in its bin directory. A caller
    # can supply that absolute proxy while PATH omits the directory entirely.
    # Compare actual inode identities in BOTH supplied/resolved directories;
    # neither the candidate's basename nor PATH absence proves a direct binary.
    proxy_identities = {path.parent / "rustup", actual.parent / "rustup"}
    if rustup:
        proxy_identities.add(Path(rustup))
    is_proxy = actual.name == "rustup"
    for candidate in proxy_identities:
        try:
            is_proxy |= os.path.samefile(actual, candidate)
        except FileNotFoundError:
            continue
        except OSError as error:
            raise ValueError(f"Cannot establish installed {tool} proxy identity: {candidate}") from error
    if is_proxy:
        raise ValueError(f"Refusing rustup proxy for {tool}; supply the actual installed toolchain binary")
    result = observe([str(actual), "-Vv"])
    result["binary"] = receipt(actual)
    if result["exit_code"] != 0:
        raise ValueError(f"Installed {tool} version query failed: {result}")
    return result


def linux_system_cfg(expression: str, target_os: str | None) -> bool:
    """Audited system-deps target_os/all/any/not subset; unknown syntax refuses."""
    if target_os != "linux" or len(expression) > 512:
        raise ValueError("Unsupported system-deps cfg target or expression")
    outer = re.fullmatch(r"cfg\((.*)\)", expression, re.DOTALL)
    if outer is None:
        raise ValueError("Unsupported system-deps cfg expression")
    source, tokens, offset = outer[1], [], 0
    while offset < len(source):
        if not source[offset:].strip():
            break
        token = re.match(r'\s*(target_os|all|any|not|[()=,]|"[A-Za-z0-9_-]+")', source[offset:])
        if token is None:
            raise ValueError("Unsupported system-deps cfg predicate")
        tokens.append(token[1])
        offset += token.end()
    position = 0

    def take(expected=None):
        nonlocal position
        if position >= len(tokens) or (expected is not None and tokens[position] != expected):
            raise ValueError("Malformed system-deps cfg expression")
        value = tokens[position]
        position += 1
        return value

    def predicate(depth=0):
        if depth > 16:
            raise ValueError("Unsupported system-deps cfg nesting")
        operator = take()
        if operator == "target_os":
            take("=")
            value = take()
            if value not in ('"linux"', '"macos"', '"windows"'):
                raise ValueError("Unsupported system-deps cfg target_os value")
            return value == '"linux"'
        if operator not in ("all", "any", "not"):
            raise ValueError("Unsupported system-deps cfg predicate")
        take("(")
        values = []
        while position < len(tokens) and tokens[position] != ")":
            values.append(predicate(depth + 1))
            if position < len(tokens) and tokens[position] == ")":
                break
            take(",")
        take(")")
        if operator == "not":
            if len(values) != 1:
                raise ValueError("Malformed system-deps cfg not predicate")
            return not values[0]
        return all(values) if operator == "all" else any(values)

    result = predicate()
    if position != len(tokens):
        raise ValueError("Malformed system-deps cfg trailing tokens")
    return result


def metadata_requirements(path: Path, lock: dict, root_manifest: Path, *, target_os=None) -> dict:
    """Inspect all locked manifests, not a guessed MSRV from Cargo.lock versions.

    Cargo metadata is a supplied observation from the existing locked acquisition
    step. Its source manifests and inherited workspace declarations are retained
    by identity. This checks declared requirements, never the exact minimum Rust
    version or arbitrary build-script/dynamic system dependency behaviour.
    """
    snapshot = read_snapshot(path)
    metadata = json.loads(snapshot)
    if not isinstance(metadata, dict) or metadata.get("version") != 1 or not isinstance(metadata.get("packages"), list):
        raise ValueError("Expected Cargo metadata format-version 1 packages")
    packages = metadata["packages"]
    if any(not isinstance(package, dict) for package in packages):
        raise ValueError("Malformed Cargo metadata package record")
    expected = {(p["name"], p["version"], p.get("source")) for p in lock["package"]}
    identities = [(p["name"], p["version"], p.get("source")) for p in packages]
    if len(expected) != len(lock["package"]) or len(set(identities)) != len(identities):
        raise ValueError("Duplicate locked or metadata package identity")
    if set(identities) != expected:
        raise ValueError(f"Metadata must cover every locked package: missing={len(expected - set(identities))}, extra={len(set(identities) - expected)}")
    resolved = metadata.get("resolve")
    nodes = resolved.get("nodes") if isinstance(resolved, dict) else None
    if not isinstance(nodes, list):
        raise ValueError("Cargo metadata resolved feature nodes are required")
    if any(not isinstance(node, dict) or not isinstance(node.get("id"), str)
           or not isinstance(node.get("features"), list)
           or any(not isinstance(feature, str) for feature in node["features"]) for node in nodes):
        raise ValueError("Malformed Cargo metadata resolved feature nodes")
    features = {node["id"]: set(node["features"]) for node in nodes}
    package_ids = [package["id"] for package in packages]
    if len(set(package_ids)) != len(package_ids) or len(features) != len(nodes) or set(features) != set(package_ids):
        raise ValueError("Resolved feature nodes must cover every package exactly once")
    records, native, unspecified, fallback_names = [], {}, [], {}
    maximum = (0, 0, 0)
    root_seen = False
    for package in packages:
        manifest_path = Path(package["manifest_path"])
        if not manifest_path.is_absolute():
            raise ValueError("Cargo package manifest paths must be absolute")
        data = read_snapshot(manifest_path)
        manifest = tomllib.loads(data.decode("utf-8"))
        declaration = manifest["package"]
        if not isinstance(declaration, dict):
            raise ValueError("Expected a Cargo package declaration")
        if declaration["name"] != package["name"]:
            raise ValueError("Cargo metadata/package manifest name mismatch")
        inherited = {}
        if any(isinstance(declaration.get(key), dict) for key in ("version", "rust-version")):
            for parent in list(manifest_path.parents)[1:9]:
                candidate = parent / "Cargo.toml"
                if candidate.is_file():
                    workspace_bytes = read_snapshot(candidate)
                    workspace = tomllib.loads(workspace_bytes.decode("utf-8")).get("workspace", {})
                    if "package" in workspace:
                        inherited = workspace["package"]
                        workspace_receipt = receipt(candidate, snapshot=workspace_bytes)
                        break
            if not inherited:
                raise ValueError("Inherited package declarations need their actual workspace manifest")

        def declared(key):
            value = declaration.get(key)
            if isinstance(value, dict):
                if value != {"workspace": True} or key not in inherited:
                    raise ValueError("Unsupported inherited Cargo package declaration")
                return inherited[key]
            return value

        if declared("version") != package["version"]:
            raise ValueError("Cargo metadata/package manifest version mismatch")
        msrv = declared("rust-version")
        if (None if msrv is None else numeric_version(msrv)) != (
                None if package.get("rust_version") is None else numeric_version(package["rust_version"])):
            raise ValueError("Cargo metadata/package manifest Rust requirement mismatch")
        if msrv is None:
            unspecified.append(package["id"])
        else:
            maximum = max(maximum, numeric_version(msrv))
        record = {"id": package["id"], "source": package.get("source"),
                  "rust_version": msrv, "manifest": receipt(manifest_path, snapshot=data)}
        if inherited:
            record["workspace_manifest"] = workspace_receipt
        records.append(record)
        if manifest_path.resolve() == root_manifest.resolve():
            root_seen = True
        if package["name"] in ("gtk4", "libadwaita", "rnote-ui"):
            raise ValueError("Headless engine metadata contains a donor UI dependency")
        # Inactive locked packages still contribute conservative declared MSRV
        # observations, but only resolved nodes contribute native feature floors.
        if package["id"] not in features:
            continue
        package_metadata = declaration.get("metadata", {})
        if not isinstance(package_metadata, dict):
            raise ValueError("Malformed Cargo package metadata")
        system = package_metadata.get("system-deps", {})
        if not isinstance(system, dict):
            raise ValueError("Malformed Cargo system-deps metadata")
        entries = []
        for key, spec in system.items():
            if key.startswith("cfg("):
                active = linux_system_cfg(key, target_os)
                if not isinstance(spec, dict) or any(key.startswith("cfg(") for key in spec):
                    raise ValueError("Unsupported nested system-deps cfg declaration")
                entries.extend((key, value, active) for key, value in spec.items())
            else:
                entries.append((key, spec, True))
        for key, spec, active in entries:
            # system-deps 7.0.8 accepts version shorthand and defaults name to key.
            if isinstance(spec, str):
                spec = {"version": spec}
            if not isinstance(spec, dict) or "version" not in spec:
                raise ValueError(f"Unsupported system-deps declaration: {package['name']}/{key}")
            name, required = spec.get("name", key), numeric_version(spec["version"])
            if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9_.+-]+", name):
                raise ValueError("Unsupported native pkg-config module name")
            if "optional" in spec and not isinstance(spec["optional"], bool):
                raise ValueError("Unsupported system-deps optional declaration")
            fallbacks = spec.get("fallback-names", [])
            if not isinstance(fallbacks, list) or any(not isinstance(name, str)
                    or not re.fullmatch(r"[A-Za-z0-9_.+-]+", name) for name in fallbacks):
                raise ValueError("Unsupported system-deps fallback-names declaration")
            selector = spec.get("feature")
            if "feature" in spec and (not isinstance(selector, str)
                                      or not re.fullmatch(r"[A-Za-z0-9_.+-]+", selector)):
                raise ValueError(f"Unsupported system-deps feature selector: {key}")
            for feature, value in spec.items():
                if feature in ("name", "version", "optional", "feature", "fallback-names"):
                    continue
                if not isinstance(value, dict) or set(value) != {"version"}:
                    raise ValueError(f"Unsupported feature-conditioned system-deps declaration: {key}/{feature}")
                floor = numeric_version(value["version"])
                if feature in features[package["id"]]:
                    required = max(required, floor)
            # system-deps 7.0.8 gates the entire module on this Cargo feature.
            # Validate every declaration above even when its selector is inactive.
            enabled = {feature.upper().replace("-", "_") for feature in features[package["id"]]}
            if not active or (selector is not None and selector.upper().replace("-", "_") not in enabled):
                continue
            # Conservatively require optional modules and the PRIMARY name even
            # if upstream could use a fallback. Never claim fallback execution.
            native[name] = max(native.get(name, (0, 0, 0)), required)
            if fallbacks:
                fallback_names[name] = sorted(set(fallback_names.get(name, []) + fallbacks))
    if not root_seen:
        raise ValueError("Cargo metadata omits the actual bridge manifest path")
    return {"metadata": receipt(path, snapshot=snapshot), "locked_packages": len(expected),
            "package_manifests": records, "maximum_declared_rust_version": list(maximum),
            "packages_without_declared_msrv": unspecified, "native_requirements": native,
            "declared_fallback_names": fallback_names,
            "scope": "Declared MSRVs and static system-deps floors only; optional modules and primary names are conservatively required, fallback execution is NOT RUN; exact MSRV and arbitrary build-script requirements remain UNVERIFIED"}


def build_preflight(args) -> tuple[dict, int]:
    report = {"schema_version": 1, "mode": "build-preflight", "target": args.target,
              "release_ready": False, "native_environment_reproducible": False,
              "native_build_and_tests": "NOT RUN", "application_acceptance": "NOT RUN",
              "exact_transitive_msrv": "UNVERIFIED; a compatible declared floor is not a proven minimum",
              "scope": "Read-only installed prerequisites and retained locked-source observations; no acquisition, build or release qualification",
              "blockers": [], "release_blockers": ["Immutable native environment and full native/application acceptance remain unqualified"]}
    blockers = report["blockers"]
    try:
        if args.target not in MACHINES:
            raise ValueError("Unsupported native preflight target")
        toolchain = numeric_version(args.rust_toolchain)
        if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", args.rust_toolchain):
            raise ValueError("Select an exact installed Rust toolchain version")
        report["rust_toolchain"] = args.rust_toolchain
        report["source"] = source_policy(args.repo)
        for item in report["source"]["source_inputs"]:
            relative = Path(item["path"]).relative_to(args.repo)
            expected = subprocess.check_output(["git", "-C", str(args.repo), "show", f"{args.revision}:{relative}"], timeout=10)
            if hashlib.sha256(expected).hexdigest() != item["sha256"]:
                raise ValueError("Preflight source bytes differ from requested immutable revision")
        manifest_path = args.repo / CANVAS / "rnote-poc/Cargo.toml"
        lock_path = args.repo / CANVAS / "rnote-poc/Cargo.lock"
        declarations = {path: read_snapshot(path) for path in (manifest_path, lock_path)}
        manifest, lock = (tomllib.loads(declarations[path].decode("utf-8")) for path in (manifest_path, lock_path))
        if lock.get("version") != 4:
            raise ValueError("Build preflight supports the audited Cargo lock format 4 only")
        for path, data in declarations.items():
            if receipt(path, snapshot=data)["sha256"] != next(x["sha256"] for x in report["source"]["source_inputs"] if x["path"] == str(path)):
                raise ValueError("Preflight declaration changed during inspection")
        if toolchain < numeric_version(manifest["package"]["rust-version"]):
            blockers.append("Selected toolchain is below the bridge declared Rust requirement")
        report["bridge_rust_version"] = manifest["package"]["rust-version"]
        report["lock_format"] = lock["version"]
        report["locked_package_count"] = len(lock["package"])
        if not report["source"]["selected_matches_locked"]:
            report["release_blockers"].append("Selected donor differs from actual locked producer; no relabelling or pin approval")
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        blockers.append(f"Source/toolchain declaration unavailable: {error}")
        lock = None
    for tool in ("rustc", "cargo"):
        try:
            result = installed_tool(getattr(args, tool), tool)
            report[tool] = result
            expected_line = f"release: {args.rust_toolchain}" if tool == "rustc" else f"cargo {args.rust_toolchain} "
            lines = result["stdout"].splitlines()
            if not any(line == expected_line if tool == "rustc" else line.startswith(expected_line) for line in lines):
                blockers.append(f"Observed {tool} does not match the exact selected toolchain")
            if tool == "rustc" and f"host: {args.target}" not in lines:
                blockers.append("Installed Rust host differs from the native target")
        except (OSError, ValueError) as error:
            blockers.append(str(error))
    native = {name: (0, 0, 0) for name in (
        "glib-2.0", "gio-2.0", "gobject-2.0", "cairo", "cairo-gobject",
        "cairo-png", "cairo-svg", "cairo-pdf", "pango", "pangocairo", "libxml-2.0")}
    native["cairo"] = (1, 18, 0)  # actual pinned upstream cairo-rs v1_18 feature
    try:
        if args.cargo_metadata is None or lock is None:
            raise ValueError("All locked package manifests require retained cargo metadata; lockfile alone cannot establish transitive MSRV")
        # Both admitted native targets are Linux. Do not infer another OS from
        # the cloud inspector's host or treat unsupported cfg predicates as false.
        requirements = metadata_requirements(args.cargo_metadata, lock, manifest_path, target_os="linux")
        report["declared_requirements"] = requirements
        root_identity = next(record["manifest"] for record in requirements["package_manifests"]
                             if Path(record["manifest"]["path"]).resolve() == manifest_path.resolve())
        expected_identity = next(record for record in report["source"]["source_inputs"] if record["path"] == str(manifest_path))
        if root_identity != expected_identity:
            raise ValueError("Bridge manifest changed between source and metadata inspection")
        if numeric_version(args.rust_toolchain) < tuple(requirements["maximum_declared_rust_version"]):
            blockers.append("Locked dependency declares a Rust requirement above the selected toolchain")
        for name, version in requirements["native_requirements"].items():
            native[name] = max(native.get(name, (0, 0, 0)), version)
    except (OSError, ValueError, KeyError, TypeError) as error:
        blockers.append(f"Locked manifest requirements UNVERIFIED: {error}")
    report["native_modules"] = []
    for name, required in sorted(native.items()):
        result = observe(["pkg-config", "--modversion", name])
        result.update({"module": name, "minimum_observed_floor": list(required)})
        try:
            if result["exit_code"] != 0 or numeric_version(result.get("stdout", "").strip()) < required:
                raise ValueError("missing, failed version query or below declared floor")
        except ValueError as error:
            blockers.append(f"Native module {name}: {error}")
        report["native_modules"].append(result)
    report["native_tools"] = {tool: observe([tool, "--version"]) for tool in ("cc", "pkg-config", "cmake")}
    for tool in ("cc", "pkg-config"):
        if report["native_tools"][tool]["exit_code"] != 0:
            blockers.append(f"Required native tool unavailable: {tool}")
    report["native_packages"] = observe(["dpkg-query", "-W", "-f=${binary:Package}\t${Version}\t${Architecture}\n",
                                         "libglib2.0-dev", "libcairo2-dev", "libpango1.0-dev", "libxml2-dev"])
    report["preflight_status"] = "BLOCKED" if blockers else "DECLARED_PREREQUISITES_OBSERVED"
    return report, 1 if blockers else 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--library", type=Path)
    parser.add_argument("--target", choices=MACHINES, required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--search-dir", type=Path, action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--copy-closure", type=Path)
    parser.add_argument("--require-release-ready", action="store_true")
    parser.add_argument("--build-preflight", action="store_true")
    parser.add_argument("--rust-toolchain")
    parser.add_argument("--rustc", type=Path)
    parser.add_argument("--cargo", type=Path)
    parser.add_argument("--cargo-metadata", type=Path)
    args = parser.parse_args(argv)
    if args.build_preflight and (args.library or args.copy_closure or args.search_dir or args.require_release_ready):
        parser.error("Build preflight does not inspect a library or qualify release readiness")
    if not args.build_preflight and args.library is None:
        parser.error("Artifact inspection requires --library")
    report = {"schema_version": 1, "target": args.target, "release_ready": False,
              "application_acceptance": "NOT RUN; engine-only source/binary inspection",
              "closure_scope": "Static DT_NEEDED closure in explicit roots; not a dynamic-loader trace or optional dlopen proof.",
              "licence_review": "NOT RUN; CI inventories are evidence, not licence qualification",
              "native_environment_reproducible": False,
              "environment_blocker": "Hosted runner image and apt packages lack a reviewed immutable image/snapshot; captured versions do not pin future builds."}
    code = 1
    try:
        actual = subprocess.check_output(["git", "-C", str(args.repo), "rev-parse", "HEAD"], text=True).strip()
        if actual != args.revision:
            raise ValueError("Checkout revision differs from requested immutable candidate")
        subprocess.check_output(["git", "-C", str(args.repo), "diff", "--exit-code", args.revision, "--",
                                 str(CANVAS / "rnote-poc"), str(CANVAS / "RnoteCanvasEngine.cs"),
                                 "eng/donor-sources.json", ".github/workflows/canvas-rnote-poc.yml"], text=True)
        report["revision"] = actual
        if args.build_preflight:
            report, code = build_preflight(args)
            report["revision"] = actual
            return write_report(args.output, report, code)
        report["source"] = source_policy(args.repo)
        report["closure"] = dependency_closure(args.library, MACHINES[args.target], args.search_dir, args.copy_closure)
        artifact = next(item for item in report["closure"] if Path(item["path"]) == args.library.resolve())
        exports = set(artifact["exports"])
        report["missing_core"] = sorted(CORE - exports)
        report["missing_full"] = sorted(set(report["source"]["managed_imports"]) - exports)
        report["core_binary_contract"] = "VERIFIED EXPORTS ONLY" if not report["missing_core"] else "BLOCKED"
        report["release_blockers"] = [report["environment_blocker"]]
        if not report["source"]["selected_matches_locked"]:
            report["release_blockers"].append("Selected donor differs from actual locked Rust producer; do not relabel the old engine.")
        if report["missing_full"]:
            report["release_blockers"].append("Managed full API imports missing from actual native exports.")
        code = 1 if report["missing_core"] else (2 if args.require_release_ready else 0)
    except (OSError, ValueError, KeyError, struct.error, subprocess.SubprocessError) as error:
        report["inspection_failure"] = {"type": type(error).__name__, "message": str(error)}
    return write_report(args.output, report, code)


def write_report(output: Path, report: dict, code: int) -> int:
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(output.suffix + ".tmp")
    temporary.write_text(json.dumps(report, indent=2) + "\n")
    os.replace(temporary, output)
    print(json.dumps({"report": str(output), "exit_code": code, "release_ready": False}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
