#!/usr/bin/env python3
"""Pure, testable validation helpers for the root-only package merge.

These functions are imported by ``package-arm64-deb.ROOT-ONLY.py`` and exercised by
the focused Python tests without running the packager (which is root-only). They
enforce the browser-stage merge gate: a nonempty, exact-version manifest whose listed
files exist with matching sha256 and sizes, whose symlinks stay inside the stage, whose
sandbox is the vendor 4755 root-owned chrome-sandbox, and which records that the
corresponding signed sources were verified. Any unlisted file or symlink escape is a
hard failure.
"""
from __future__ import annotations

import os
import stat
from pathlib import Path

from chromium_native_common import (
    BROWSER_PROOF_SCHEMA,
    MANIFEST_REL,
    SANDBOX_EXPECTED_GID,
    SANDBOX_EXPECTED_UID,
    SANDBOX_MODE,
    load_json,
    parse_glibc_tuple,
    sha256_file,
    staged_elfs,
    validate_source_build_graph,
    verify_stage_manifest_entries,
)

STAGE_MANIFEST_SCHEMA = "9to1.chromium-native.stage-manifest/v1"
PINNED_BUNDLE_VERSION = "155.0.8059.79-1~deb13u1"
# Providers that must be proven (with full authenticated identity) in the static
# ABI proof for a browser stage merge to be accepted.
CRITICAL_PROVIDERS = ("libc6", "libgcc-s1", "zlib1g", "libnss3", "libstdc++6")


def validate_path_contained(stage: Path, rel: str) -> Path:
    """Return the resolved path for ``rel`` under ``stage``, or raise if it escapes.

    ``rel`` must be a relative POSIX path and, after resolution, must stay within
    the stage root. Symlink escapes are rejected.
    """
    root_real = stage.resolve()
    candidate = stage / rel
    resolved = candidate.resolve()
    try:
        resolved.relative_to(root_real)
    except ValueError:
        raise SystemExit(f"browser stage path escapes: {rel} -> {resolved}")
    return resolved


def required_files_nonempty(manifest: dict) -> None:
    files = manifest.get("files")
    if not isinstance(files, list) or len(files) == 0:
        raise SystemExit("browser stage manifest has no files (empty stage)")


def validate_manifest_identity(manifest: dict) -> None:
    if manifest.get("schema") != STAGE_MANIFEST_SCHEMA:
        raise SystemExit("browser stage manifest schema mismatch")
    if manifest.get("version") != PINNED_BUNDLE_VERSION:
        raise SystemExit(f"browser stage version mismatch: expected "
                         f"{PINNED_BUNDLE_VERSION}, got {manifest.get('version')}")


def validate_sandbox_spec(manifest: dict, stage: Path) -> None:
    sb = manifest.get("sandbox")
    if not isinstance(sb, dict):
        raise SystemExit("browser stage manifest missing sandbox record")
    if sb.get("mode_hex") != oct(SANDBOX_MODE):
        raise SystemExit("browser stage sandbox mode not 4755")
    expected = sb.get("expected_owner")
    if not isinstance(expected, dict) or expected.get("uid") != SANDBOX_EXPECTED_UID \
            or expected.get("gid") != SANDBOX_EXPECTED_GID:
        raise SystemExit("browser stage sandbox expected owner not root:root(0/0)")
    # On-disk mode must actually be 4755 (the staged file).
    sb_path = stage / "chromium/chrome-sandbox"
    if not sb_path.is_file():
        raise SystemExit("chrome-sandbox missing from browser stage")
    if stat.S_IMODE(sb_path.stat().st_mode) != SANDBOX_MODE:
        raise SystemExit("chrome-sandbox on-disk mode is not 4755")


def validate_source_gate(manifest: dict, source_dir: Path | None = None,
                         source_pins: dict | None = None) -> None:
    """Executable source gate. The stage merge requires the real verified physical
    source inputs (``source_dir``) and the pinned authenticated source manifest
    (``source_pins``). It reconstructs the receipt from the manifest's embedded
    build graph and runs the full gate: the complete expected Chromium+JPEG input
    set must match exactly, every file must exist with matching bytes+streamSHA,
    and source versions must match. There is no self-asserted bool/nonempty-list
    acceptance — a fabricated graph fails here."""
    if source_dir is None or source_pins is None:
        raise SystemExit("browser stage merge requires verified physical source "
                         "inputs (--source-dir and --source-pins); refusing a "
                         "self-asserted source gate")
    build_graph = manifest.get("source_pins", {}).get("build_graph")
    receipt = {"source_graph": build_graph}
    res = validate_source_build_graph(source_dir, source_pins, receipt)
    if not res["ok"]:
        raise SystemExit("browser stage source gate failed: "
                         + "; ".join(res["problems"][:8]))


def validate_browser_stage(stage: Path, source_dir: Path | None = None,
                           source_pins_path: Path | None = None) -> dict:
    """Full gate. Returns the validated manifest. Raises SystemExit on any failure.
    When ``source_dir`` and ``source_pins_path`` are provided (root package merge),
    the real physical source gate is enforced before any output."""
    stage = stage.resolve()
    bmanifest = stage / MANIFEST_REL
    if not stage.is_dir() or not bmanifest.is_file():
        raise SystemExit("missing browser stage/manifest")
    manifest = load_json(bmanifest)
    validate_manifest_identity(manifest)
    required_files_nonempty(manifest)
    # Containment + size/hash cross-check for every manifest entry.
    problems = verify_stage_manifest_entries(stage, manifest)
    if problems:
        raise SystemExit("browser stage manifest/disk mismatch: " + "; ".join(problems[:8]))
    # Reject unlisted files (no orphaned payload). The stage manifest itself is the
    # one expected non-payload file at the stage root.
    listed = {str(e.get("path")) for e in manifest.get("files", [])}
    for p in stage.rglob("*"):
        if p.is_dir():
            continue
        rel = p.relative_to(stage)
        if str(rel) == MANIFEST_REL:
            continue
        if str(rel) not in listed:
            raise SystemExit(f"browser stage contains unlisted file: {rel}")
    validate_sandbox_spec(manifest, stage)
    source_pins = None
    if source_pins_path is not None:
        source_pins = load_json(source_pins_path)
    validate_source_gate(manifest, source_dir, source_pins)
    return manifest


# --------------------------------------------------------------------------- #
# Static ABI proof gate (required for browser-stage package merge)
# --------------------------------------------------------------------------- #
def _require(cond, msg):
    if not cond:
        raise SystemExit("browser proof: " + msg)


_LOADER_SONAME = "ld-linux-aarch64.so.1"
_INTERPRETER_PATH = "/lib/ld-linux-aarch64.so.1"
_PRIVATE_JPEG_SONAME = "libjpeg.so.62"
_PRIVATE_JPEG_REL = "chromium/lib/libjpeg.so.62.3.0"
_PROVIDER_IDENTITY_FIELDS = (
    "provider", "version", "architecture", "sha256", "filename",
    "path_in_pkg", "soname", "defined_versions", "needed",
)


def _load_inspector():
    """Load the genuine static ABI inspector (verify-chromium-bundle.py) to recompute
    expected stage facts and (optionally) cross-check provider archive identities."""
    import importlib.util
    here = Path(__file__).resolve().parent
    mod_path = here / "verify-chromium-bundle.py"
    spec = importlib.util.spec_from_file_location("verify_chromium_bundle", mod_path)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _validate_proof_layer1(proof, stage_root, resolution_path):
    """Layer 1 — genuine fact recompute. Always runs. Rejects any self-asserted or
    fabricated proof: no elfs, no authenticated provider identity, no loader/private
    staged proof, missing/unbound stage payloads, arbitrary satisfied booleans, or a
    forged version floor all fail here."""
    _require(proof.get("schema") == BROWSER_PROOF_SCHEMA,
             f"schema mismatch: expected {BROWSER_PROOF_SCHEMA}")
    _require(proof.get("verdict") == "PASS", "proof verdict is not PASS")
    _require(proof.get("acceptance") == "NOT RUN",
             "proof must not claim native acceptance (expected NOT RUN)")

    manifest_path = stage_root / MANIFEST_REL
    _require(manifest_path.is_file(), "browser stage manifest missing")
    _require(proof.get("manifest", {}).get("manifest_sha256") == sha256_file(manifest_path),
             "proof manifest_sha256 does not match the stage manifest on disk")
    _require(proof.get("resolution_sha256") == sha256_file(resolution_path),
             "proof resolution_sha256 does not match the resolution table on disk")

    inspector = _load_inspector()

    # ---- Complete actual stage ELF set + payload binding + genuine fact recompute ----
    elfs = proof.get("elfs")
    _require(isinstance(elfs, list) and elfs, "proof has no staged ELF records (empty elfs)")
    staged = {p.relative_to(stage_root).as_posix(): p for p in inspector.staged_elfs(stage_root)}
    _require(len(staged) > 0, "stage has no ELF files")
    proof_by_rel = {}
    for e in elfs:
        rel = e.get("rel") or e.get("path")
        _require(rel and rel not in proof_by_rel, f"ELF record missing/dup rel: {rel!r}")
        proof_by_rel[rel] = e
    _require(set(proof_by_rel) == set(staged),
             "ELF set mismatch: on-disk %s vs proof %s"
             % (sorted(set(staged) - set(proof_by_rel))[:5],
                sorted(set(proof_by_rel) - set(staged))[:5]))
    for rel, p in staged.items():
        e = proof_by_rel[rel]
        _require(e.get("size") == p.stat().st_size,
                 f"payload size mismatch: {rel}")
        _require(e.get("sha256") == sha256_file(p),
                 f"payload sha256 mismatch: {rel}")
        facts = inspector.inspect_elf(p)  # raises -> fail closed
        _require(facts["machine"] == "AArch64" and facts["class"] == "ELF64",
                 f"staged ELF not AArch64 ELF64: {rel}")
        _require(e.get("machine") == "AArch64" and e.get("class") == "ELF64",
                 f"proof ELF machine/class wrong: {rel}")
        _require(e.get("needed") == facts["needed"],
                 f"proof needed mismatch vs real ELF: {rel}")
        _require(e.get("version_needs") == facts["version_needs"],
                 f"proof version_needs mismatch vs real ELF: {rel}")
        _require(set(e.get("defined_versions", [])) == set(facts["defined_versions"]),
                 f"proof defined_versions mismatch vs real ELF: {rel}")
        _require(e.get("interpreter") == facts.get("interpreter"),
                 f"proof interpreter mismatch vs real ELF: {rel}")

    # ---- Provider records: full identity, no blocked, critical coverage --------------
    proved = proof.get("providers", {}).get("proved", {})
    _require(isinstance(proved, dict) and proved, "no verified providers in proof")
    _require(not proof.get("providers", {}).get("blocked"),
             "provider proof has blocked edges: "
             + "; ".join(list(proof.get("providers", {}).get("blocked", {}).keys())[:8]))
    for son, rec in proved.items():
        for fld in _PROVIDER_IDENTITY_FIELDS:
            _require(fld in rec, f"provider {son} record missing field {fld}")
        _require(rec.get("role") in ("provider", "loader", "private_staged"),
                 f"provider {son} has unknown role {rec.get('role')!r}")
    providers_pkgs = {v.get("provider") for v in proved.values() if v.get("provider")}
    for crit in CRITICAL_PROVIDERS:
        _require(crit in providers_pkgs, f"critical provider {crit} not proven in ABI proof")

    # ---- Loader root chain + private staged identity -------------------------------
    loader_recs = [r for r in proved.values() if r.get("role") == "loader"]
    _require(loader_recs, "no loader proof (must prove ld-linux-aarch64.so.1)")
    lr = loader_recs[0]
    _require(lr.get("interpreter_path") == _INTERPRETER_PATH,
             f"loader interpreter_path {lr.get('interpreter_path')!r} != {_INTERPRETER_PATH}")
    _require(lr.get("soname") == _LOADER_SONAME,
             f"loader soname {lr.get('soname')!r} != {_LOADER_SONAME}")
    priv = [r for r in proved.values()
            if r.get("role") == "private_staged" and r.get("soname") == _PRIVATE_JPEG_SONAME]
    _require(priv, "no private staged JPEG proof")
    _require(any(r.get("path_in_pkg") == _PRIVATE_JPEG_REL for r in priv),
             f"private JPEG path must be {_PRIVATE_JPEG_REL}")
    # The private JPEG staged ELF itself must be part of the verified stage ELF set
    # (so its actual version definitions are genuinely recomputed and bound).
    _require(_PRIVATE_JPEG_REL in proof_by_rel,
             f"private JPEG staged ELF {_PRIVATE_JPEG_REL} missing from elfs")

    # ---- Edge satisfaction recomputed from real facts (no self-asserted boolean) ----
    defined_by_son = {son: set(rec.get("defined_versions", [])) for son, rec in proved.items()}
    consumers = []
    for e in elfs:
        consumers.append({"rel": e.get("rel") or e.get("path"),
                          "needed": e.get("needed", []),
                          "version_needs": e.get("version_needs", {})})
    for son, rec in proved.items():
        if rec.get("version_needs"):
            consumers.append({"rel": rec.get("path_in_pkg") or son,
                              "needed": rec.get("needed", []),
                              "version_needs": rec.get("version_needs", {})})
    for c in consumers:
        for son in c["needed"]:
            _require(son in proved, f"unresolved external dependency {son} for {c['rel']}")
            req = c["version_needs"].get(son, [])
            if not req:
                continue
            defined = defined_by_son.get(son, set())
            missing = sorted(v for v in req if v not in defined)
            _require(not missing,
                     f"{c['rel']} -> {son}: provider missing versions {missing}")

    # Every recorded edge must correspond to a real consumer requirement and be
    # satisfied on recompute; and the edge set must EXACTLY equal the real consumer
    # requirements (no fabricated/orphan edges, no dropped actual edges).
    edges = proof.get("version_edges", [])
    _require(isinstance(edges, list), "no version_edges list in proof")
    real_pairs = {(c["rel"], son) for c in consumers for son in c["needed"]}
    req_triples = set()
    for c in consumers:
        for son, vlist in c["version_needs"].items():
            for v in vlist:
                req_triples.add((c["rel"], son, v))
    edge_triples = set()
    for e in edges:
        pair = (e.get("consumer_rel"), e.get("soname"))
        _require(pair in real_pairs, f"orphan/arbitrary version edge {pair}")
        defined = defined_by_son.get(pair[1], set())
        for v in e.get("required", []):
            _require(v in defined,
                     f"edge {pair} required version {v} not defined by provider")
            edge_triples.add((pair[0], pair[1], v))
    _require(edge_triples == req_triples,
             "version edges do not match actual consumer requirements exactly")

    # The main binary's real interpreter/needed requirements (when non-empty) must be
    # covered exactly (already guaranteed by exactness above); presence of the main
    # binary among consumers is required.
    _require(any(c["rel"] == "chromium/chromium" for c in consumers),
             "main binary chromium/chromium missing from consumer set")

    return proof


def _validate_proof_layer2(proof, stage_root, resolution_path,
                           ubuntu_inrelease, ubuntu_keyring, provider_cache):
    """Layer 2 — offline authenticated cross-check (always run by the public gate).
    Re-derives the genuine provider closure from the authenticated index/cache and
    compares the EXACT provider SONAME set and ALL relevant recomputed fields (archive
    identity, ELF facts, loader chain, private payload) against the proof. The proof's
    auth record must positively assert verification and match the authenticated
    InRelease signature FPR + index SHA/bytes; contradictory false-auth records are
    rejected."""
    inspector = _load_inspector()
    inrelease_text = ubuntu_inrelease.read_text(errors="replace")
    sig = inspector.verify_inrelease_strict(ubuntu_keyring, ubuntu_inrelease,
                                            inspector.UBUNTU_ARCHIVE_FPR)
    _require(sig["verified"], "Ubuntu InRelease signature did not verify against key")

    # Proof auth must positively assert verified + pinned FPR + authenticated index ids.
    proof_inrel = proof.get("ubuntu_inrelease", {})
    _require(proof_inrel.get("verified") is True,
             "proof does not positively assert authenticated InRelease verification")
    _require(proof_inrel.get("key_fingerprint") == inspector.UBUNTU_ARCHIVE_FPR,
             "proof key fingerprint mismatch (must equal pinned Ubuntu key FPR)")
    _require(proof_inrel.get("sha256") == sha256_file(ubuntu_inrelease),
             "proof InRelease sha256 mismatch vs authenticated InRelease")
    primary_fprs = proof_inrel.get("primary_fprs", [])
    _require(inspector.UBUNTU_ARCHIVE_FPR in primary_fprs,
             "pinned Ubuntu key FPR absent from proof primary_fprs")
    indexes = proof.get("indexes")
    _require(isinstance(indexes, list) and indexes, "proof has no index identity")
    for comp in ("main", "universe"):
        idx = next((x for x in indexes if x.get("component") == comp), None)
        _require(idx is not None, f"proof missing {comp} index identity")
        sha, size = inspector.find_index_sha256_text(
            inrelease_text, f"{comp}/binary-arm64/Packages.xz")
        _require(sha, f"{comp} index entry missing from authenticated InRelease")
        _require(idx.get("sha256") == sha,
                 f"proof {comp} index SHA mismatch vs authenticated InRelease")
        _require(idx.get("bytes") == size,
                 f"proof {comp} index bytes mismatch vs authenticated InRelease")

    main_index = inspector.ensure_ubuntu_index("main", inrelease_text, provider_cache, offline=True)
    universe_index = inspector.ensure_ubuntu_index("universe", inrelease_text, provider_cache, offline=True)
    staged_by_soname = {}
    external_needed = set()
    for p in inspector.staged_elfs(stage_root):
        info = inspector.inspect_elf(p)
        info["rel"] = p.relative_to(stage_root).as_posix()
        info["sha256"] = sha256_file(p)
        info["size"] = p.stat().st_size
        if info.get("soname"):
            staged_by_soname.setdefault(info["soname"], info)
        for n in info["needed"]:
            external_needed.add(n)
    root_sonames = {n for n in external_needed if n != inspector.AARCH64_INTERPRETER}
    resolution = load_json(resolution_path)
    proven, blocked = inspector.prove_provider_closure(
        root_sonames, resolution, main_index, universe_index,
        provider_cache, True, staged_by_soname)
    _require(not blocked,
             "offline provider closure has blocked providers: "
             + "; ".join(f"{k}: {v}" for k, v in list(blocked.items())[:8]))

    proof_proved = proof.get("providers", {}).get("proved", {})
    # Exact provider SONAME set parity (order-independent).
    _require(set(proven) == set(proof_proved),
             "offline proven SONAME set does not exactly match proof providers")

    # Full recomputed-field comparison per provider (role-aware), canonical where order
    # is irrelevant. A deepcopy that changes transitive needed/version_needs/defined
    # facts is rejected even when the archive identity is unchanged.
    for son, rec in proven.items():
        pc = proof_proved[son]
        _require(pc.get("role") == rec.get("role"), f"{son} role mismatch")
        _require(pc.get("provider") == rec.get("provider"), f"{son} provider mismatch")
        _require(pc.get("version") == rec.get("version"), f"{son} version mismatch")
        _require(pc.get("architecture") == rec.get("architecture"), f"{son} arch mismatch")
        _require(pc.get("soname") == rec.get("soname"), f"{son} SONAME mismatch")
        _require(pc.get("path_in_pkg") == rec.get("path_in_pkg"), f"{son} ELF path mismatch")
        if rec.get("role") == "loader":
            _require(pc.get("interpreter_path") == rec.get("interpreter_path"),
                     "loader interpreter_path mismatch")
        if rec.get("role") == "provider":
            _require(pc.get("sha256") == rec.get("sha256"), f"{son} debSHA mismatch")
            _require(pc.get("bytes") == rec.get("bytes"), f"{son} archive bytes mismatch")
            _require(pc.get("filename") == rec.get("filename"), f"{son} Filename mismatch")
        else:  # loader / private_staged payload identity
            _require(pc.get("sha256") == rec.get("sha256"), f"{son} payload SHA mismatch")
            _require(pc.get("bytes") == rec.get("bytes"), f"{son} payload bytes mismatch")
        _require(sorted(pc.get("needed", [])) == sorted(rec.get("needed", [])),
                 f"{son} needed set mismatch")
        _require(pc.get("version_needs") == rec.get("version_needs"),
                 f"{son} version_needs mismatch")
        _require(sorted(pc.get("defined_versions", [])) == sorted(rec.get("defined_versions", [])),
                 f"{son} defined_versions mismatch")
    return proof


def _validate_browser_proof_layer1(proof_path: Path, stage: Path, resolution_path: Path) -> dict:
    """INTERNAL Layer-1-only gate. Used by focused unit fixtures. It never claims an
    authenticated PASS (auth is added by the public gate's Layer 2)."""
    proof_path = proof_path.resolve()
    _require(proof_path.is_file(), "missing static ABI proof file")
    proof = load_json(proof_path)
    stage = stage.resolve()
    stage_root = (stage / "opt/9to1-preview/browser") if (stage / "opt").is_dir() else stage
    return _validate_proof_layer1(proof, stage_root, resolution_path)


def validate_browser_proof(proof_path: Path, stage: Path, resolution_path: Path,
                           ubuntu_inrelease: Path, ubuntu_keyring: Path,
                           provider_cache: Path) -> dict:
    """Validate the static ABI proof artifact produced by verify-chromium-bundle.py.

    This is the COMPLETE packaging gate. It REQUIRES the authenticated inputs (signed
    InRelease + keyring + provider cache) — without them a complete PASS cannot be
    claimed, so they are mandatory. The gate:
      * Layer 1 recomputes the expected stage facts with the genuine static inspector
        and recomputes edge satisfaction from real facts (a fabricated/self-asserted
        proof is refused);
      * Layer 2 runs the genuine offline provider closure against the authenticated
        index/cache and cross-checks the EXACT provider SONAME set and all recomputed
        provider/loader/private fields, plus the auth record's signature FPR and index
        SHA/bytes.
    """
    if ubuntu_inrelease is None or ubuntu_keyring is None or provider_cache is None:
        _require(False, "authenticated proof inputs are mandatory for a complete PASS: "
                        "--ubuntu-inrelease/--ubuntu-keyring/--provider-cache")
    proof_path = proof_path.resolve()
    _require(proof_path.is_file(), "missing static ABI proof file")
    proof = load_json(proof_path)
    stage = stage.resolve()
    stage_root = (stage / "opt/9to1-preview/browser") if (stage / "opt").is_dir() else stage
    _validate_proof_layer1(proof, stage_root, resolution_path)
    _validate_proof_layer2(proof, stage_root, resolution_path,
                           ubuntu_inrelease, ubuntu_keyring, provider_cache)
    return proof


def derive_libc_floor(proof: dict) -> tuple:
    """Derive the libc6 version floor from the verified staged-ELF required versions."""
    best = (2, 0)
    for e in proof.get("elfs", []):
        for vs in e.get("version_needs", {}).values():
            for v in vs:
                t = parse_glibc_tuple(v)
                if t and t > best:
                    best = t
    return best


def derive_browser_deps(proof: dict, resolution: dict) -> list[str]:
    """Derive direct browser dependency packages + version floors from the verified
    provider set. Uses the actual staged-ELF DT_NEEDED edges (direct/normal
    dependencies) and the authenticated provider versions; never hardcodes an
    unversioned floor."""
    sonames = resolution.get("sonames", {})
    proved = proof.get("providers", {}).get("proved", {})
    pkg_ver: dict = {}
    for e in proof.get("elfs", []):
        for son in e.get("needed", []):
            rec = proved.get(son)
            if not rec:
                continue
            if rec.get("role") in ("private_staged", "loader"):
                continue
            row = sonames.get(son)
            if not row:
                continue
            pkg = row.get("debian_pkg")
            if not pkg or pkg == "libc6":
                continue
            ver = rec.get("version")
            if ver and pkg not in pkg_ver:
                pkg_ver[pkg] = ver
    out = []
    for pkg in sorted(pkg_ver):
        ver = pkg_ver[pkg]
        out.append(f"{pkg} (>= {ver})" if ver else pkg)
    return out


def derive_provider_version(proof: dict, soname: str):
    """Return the authenticated provider version for ``soname`` from the proof (or None)."""
    rec = proof.get("providers", {}).get("proved", {}).get(soname)
    return rec.get("version") if rec else None
