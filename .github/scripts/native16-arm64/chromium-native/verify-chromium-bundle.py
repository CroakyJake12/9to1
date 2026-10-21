#!/usr/bin/env python3
"""Verify a staged Chromium-native ARM64 bundle (static checks only).

Checks (never launches a browser, never installs anything, never executes an ARM64
binary — the host is x86_64):

  * The staged integrity manifest matches disk for every regular file (sha256 + size)
    and symlink (target + containment); no unlisted/orphaned payload is tolerated.
  * The main binary is an AArch64 ELF using /lib/ld-linux-aarch64.so.1.
  * EVERY staged ELF (binary, crashpad, sandbox, EGL/GLES/Vulkan, private JPEG) is
    inspected with ``file`` and ``readelf``; a non-zero tool exit, parse failure, or
    truncated ELF is a hard FAIL (fail-closed — an empty/NEEDED read never passes).
  * The ABI provider proof compares the ACTUAL consumer-required symbol-version sets
    (readelf .gnu.version_r per needed-file) against the ACTUAL provider version
    definitions (readelf .gnu.version_d) for every transitive edge. A provider must
    define every version its consumer requires (e.g. LIBJPEG_6.2, GCC_14.0,
    NSS_3.30, ZLIB_1.2.9). We never reduce this to comparing max-GLIBC only.
  * The dynamic loader ``ld-linux-aarch64.so.1`` is proven by actually downloading
    and inspecting the authenticated Ubuntu libc6 arm64 .deb ELF at the exact
    interpreter path (not by name/table alone).
  * The private staged JPEG (``libjpeg.so.62``) is proven from the ACTUAL staged
    ELF (SONAME / AArch64 / dependencies / version definitions), not from a table
    string.
  * External DT_NEEDED SONAMEs are resolved against the private staged lib or against
    Ubuntu ARM64 **official provider packages proven from the signed Ubuntu InRelease**
    (the suite — noble for this ARM CI job, resolute retained where previously valid —
    is read from the authenticated InRelease Codename): the index is authenticated from
    the signed InRelease, each provider .deb is downloaded and verified against the
    authenticated index (size/SHA256/arm64), extracted, and the actual ELF file whose
    SONAME matches is inspected (AArch64 class + SONAME + version definitions + its own
    transitive DT_NEEDED closure). Providers that cannot be proven this way are recorded
    as BLOCKED and the verdict is not PASS.

Static verification never claims native runtime readiness (acceptance is NOTRUN).
"""
from __future__ import annotations

import argparse
import json
import lzma
import os
import re
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from chromium_native_common import (  # noqa: E402
    BINARY_REL,
    BROWSER_PROOF_SCHEMA,
    MANIFEST_REL,
    PRIVATE_LIB_REL,
    SANDBOX_EXPECTED_UID,
    SANDBOX_MODE,
    SANDBOX_REL,
    download_atomic,
    ensure_input,
    load_json,
    max_glibc_from_lines,
    parse_version_info,
    sha256_file,
    staged_elfs,
    verify_inrelease_strict,
    verify_stage_manifest_entries,
)

AARCH64_INTERPRETER = "/lib/ld-linux-aarch64.so.1"
UBUNTU_BASE = "https://ports.ubuntu.com/ubuntu-ports"
UBUNTU_SUITE = "resolute"
# Suites allowed for the real provider ABI proof. The actual authenticated InRelease
# file determines which suite is used (Codename identity), so the same file drives
# both the proof generator and the packager's Layer 2 offline recompute.
UBUNTU_ALLOWED_SUITES = {"noble", "resolute"}
# Ubuntu Archive Automatic Signing Key (2018) primary fingerprint.
UBUNTU_ARCHIVE_FPR = "F6ECB3762474EDA9D21B7022871920D1991BC93C"
LD_LINUX = "ld-linux-aarch64.so.1"
PRIVATE_ROLE = "PRIVATE_COMPAT_STAGED"
LOADER_ROLE = "loader"


_TOOL_ENV = {**os.environ, "LC_ALL": "C"}


def _run_tool(cmd: list[str]) -> str:
    """Run a read-only tool; a non-zero exit is a hard failure (fail-closed)."""
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=180,
                              env=_TOOL_ENV)
    except subprocess.TimeoutExpired:
        raise RuntimeError("tool timed out: " + " ".join(cmd))
    if proc.returncode != 0:
        raise RuntimeError(f"tool failed ({proc.returncode}): " + " ".join(cmd))
    return proc.stdout


_DYN_ENTRY_RE = re.compile(r"^\s*0x[0-9a-f]+ \(([A-Z0-9_]+)\)(.*)$")
_DYN_HDR_RE = re.compile(r"^Dynamic section at offset .* contains (\d+) entries:\s*$")
_DYN_NONE = "There is no dynamic section in this file."
_DYN_PAYLOAD_TAGS = ("NEEDED", "SONAME", "RPATH", "RUNPATH")


def _parse_dynamic_table(dyn: str, phdr: str) -> tuple:
    """Strictly parse ``readelf -d`` stdout.

    Returns ``(needed, soname, rpath_runpath)``. Raises ``RuntimeError`` on any
    malformed, unrecognized, or header-only dynamic output. The single acceptable
    "no dynamic section" case is a genuinely static ELF, verified by the program
    headers carrying no ``PT_DYNAMIC`` segment; arbitrary garbage or a claim that
    contradicts the program headers fails. Any NEEDED/SONAME/RPATH/RUNPATH tag with
    an unparseable payload also fails.
    """
    if _DYN_NONE in dyn:
        # Genuinely static: program headers must carry no PT_DYNAMIC segment.
        if re.search(r"(?m)^\s*DYNAMIC\s", phdr):
            raise RuntimeError("readelf -d: reports no dynamic section but program "
                               "headers declare a PT_DYNAMIC segment")
        return [], "", []
    mh = None
    for line in dyn.splitlines():
        mh = _DYN_HDR_RE.search(line)
        if mh:
            break
    if not mh:
        raise RuntimeError("readelf -d: missing recognized dynamic section marker")
    declared = int(mh.group(1))
    needed: list[str] = []
    soname = ""
    rpath_runpath: list[str] = []
    seen_entries = 0
    for line in dyn.splitlines():
        s = line.strip()
        if not s or s.startswith("Tag") or _DYN_HDR_RE.search(line):
            continue
        em = _DYN_ENTRY_RE.match(s)
        if not em:
            raise RuntimeError(f"readelf -d: unexpected/unrecognized line {line!r}")
        seen_entries += 1
        tag = em.group(1)
        rest = em.group(2)
        if tag in _DYN_PAYLOAD_TAGS:
            pm = re.search(r"\[([^\]]+)\]", rest)
            if not pm:
                raise RuntimeError(f"readelf -d: tag {tag} has unparseable payload "
                                   f"{line!r}")
            val = pm.group(1)
            if tag == "NEEDED":
                needed.append(val)
            elif tag == "SONAME":
                soname = val
            else:
                rpath_runpath.append(val)
    if seen_entries != declared:
        raise RuntimeError(f"readelf -d: declared {declared} entries but parsed "
                           f"{seen_entries}")
    return needed, soname, rpath_runpath


def inspect_elf(path: Path) -> dict:
    """Inspect one ELF. Raises RuntimeError on non-ELF/tool failure/truncation."""
    file_out = _run_tool(["file", str(path)])
    if "ELF" not in file_out:
        raise RuntimeError(f"not an ELF: {path}")
    hdr = _run_tool(["readelf", "-h", str(path)])
    m = re.search(r"Class:\s*(\S+)", hdr)
    cls = m.group(1) if m else ""
    if cls != "ELF64":
        raise RuntimeError(f"unexpected ELF class {cls!r}: {path}")
    m = re.search(r"Machine:\s*(\S+)", hdr)
    machine = m.group(1) if m else ""
    if machine != "AArch64":
        raise RuntimeError(f"unexpected machine {machine!r}: {path}")

    phdr = _run_tool(["readelf", "-l", str(path)])
    interp = ""
    m = re.search(r"Requesting program interpreter:\s*(\S+)\]?", phdr)
    if m:
        interp = m.group(1).rstrip("]")

    dyn = _run_tool(["readelf", "-d", str(path)])
    needed, soname, rpath_runpath = _parse_dynamic_table(dyn, phdr)

    # Full symbol-version proof: parse .gnu.version_d (definitions) and
    # .gnu.version_r (per-file requirements) from one readelf run.
    vi = parse_version_info(_run_tool(["readelf", "--version-info", "--wide", str(path)]).splitlines())
    max_glibc = max_glibc_from_lines(
        list(vi["definitions"]) + [v for vs in vi["needs"].values() for v in vs])

    # Fail-closed: an ELF that declares DT_NEEDED must present real version data
    # (or be a genuine versionless provider that defines versions). A truncated or
    # malformed successful readelf that silently yields "no versions" must NOT pass.
    if needed and not vi["saw_any_section"]:
        raise RuntimeError(f"readelf --version-info produced no version sections "
                           f"despite DT_NEEDED: {path}")

    return {
        "path": str(path),
        "class": cls,
        "machine": machine,
        "interpreter": interp,
        "needed": sorted(set(needed)),
        "soname": soname,
        "rpath_runpath": list(dict.fromkeys(rpath_runpath)),
        "max_glibc_symbol": "none" if max_glibc is None
        else f"GLIBC_{max_glibc[0]}.{max_glibc[1]}",
        "defined_versions": list(vi["definitions"]),
        "version_needs": vi["needs"],
    }


def load_ubuntu_index(path: Path) -> dict[str, dict]:
    """Return {package: stanza} from a Packages text/xz index."""
    if str(path).endswith(".xz"):
        text = lzma.decompress(path.read_bytes()).decode("utf-8", errors="replace")
    else:
        text = path.read_text(encoding="utf-8", errors="replace")
    stanzas = {}
    for block in text.split("\n\n"):
        m = re.search(r"^Package:\s*(\S+)\s*$", block, re.MULTILINE)
        if not m:
            continue
        stanza = {}
        cur = None
        for line in block.splitlines():
            if re.match(r"^\S", line):
                k, _, v = line.partition(":")
                stanza[k] = v.strip()
                cur = k
            elif line.startswith(" ") and cur:
                stanza[cur] += "\n" + line.strip()
        stanzas[m.group(1)] = stanza
    return stanzas


def find_index_sha256_text(inrelease_text: str, suffix: str) -> tuple:
    in_sha256 = False
    for line in inrelease_text.splitlines():
        if line.startswith("SHA256:"):
            in_sha256 = True
            continue
        if in_sha256:
            if not line.startswith(" "):
                in_sha256 = False
                continue
            parts = line.split()
            if len(parts) == 3 and parts[2].endswith(suffix):
                return parts[0], int(parts[1])
    return None, None


def _inrelease_field(inrelease_text: str, key: str) -> str | None:
    prefix = key + ":"
    for line in inrelease_text.splitlines():
        if line.startswith(prefix):
            return line.split(":", 1)[1].strip()
    return None


def inrelease_suite(inrelease_text: str) -> str:
    """Return the validated signed InRelease Codename with coherent identity.

    The suite is NOT guessed from the filename or a hardcoded constant; it is read
    from the authenticated InRelease so the proof generator and the packager's
    Layer 2 offline recompute always use the exact same validated suite/cache. We
    require a coherent Suite + Codename + Architectures identity (both Suite and
    Codename present, equal, and on the explicit allowlist; arm64 listed). Missing,
    mismatched, or unapproved identity fails closed.
    """
    codename = _inrelease_field(inrelease_text, "Codename")
    suite = _inrelease_field(inrelease_text, "Suite")
    architectures = _inrelease_field(inrelease_text, "Architectures") or ""
    if not codename:
        raise RuntimeError("Ubuntu InRelease lacks a Codename identity")
    cn = codename.lower()
    if cn not in UBUNTU_ALLOWED_SUITES:
        raise RuntimeError(
            f"Ubuntu InRelease Codename {codename!r} not allowed; "
            f"must be one of {sorted(UBUNTU_ALLOWED_SUITES)}")
    if suite is None:
        raise RuntimeError("Ubuntu InRelease lacks a Suite identity")
    su = suite.lower()
    if su not in UBUNTU_ALLOWED_SUITES:
        raise RuntimeError(
            f"Ubuntu InRelease Suite {suite!r} not allowed; "
            f"must be one of {sorted(UBUNTU_ALLOWED_SUITES)}")
    if cn != su:
        raise RuntimeError(
            f"Ubuntu InRelease Suite/Codename mismatch: Suite={suite!r} "
            f"Codename={codename!r}")
    arch_list = [a.strip().lower() for a in architectures.split()]
    if "arm64" not in arch_list:
        raise RuntimeError(
            f"Ubuntu InRelease does not list arm64 in Architectures: "
            f"{architectures!r}")
    return cn


def ensure_ubuntu_index(component: str, inrelease_text: str, index_dir: Path,
                        offline: bool) -> dict[str, dict]:
    suite = inrelease_suite(inrelease_text)
    suffix = f"{component}/binary-arm64/Packages.xz"
    sha, size = find_index_sha256_text(inrelease_text, suffix)
    if not sha:
        raise RuntimeError(f"Ubuntu {suite} {suffix} not present in signed InRelease")
    dest = index_dir / f"{suite}-{component}-binary-arm64-Packages.xz"
    url = f"{UBUNTU_BASE}/dists/{suite}/{component}/binary-arm64/Packages.xz"
    if not ensure_input(offline, dest, sha, size, url):
        raise RuntimeError(f"Ubuntu index download/verify failed: {component}")
    return load_ubuntu_index(dest)


def _provider_deb(row: dict, index: dict, provider_cache: Path, offline: bool):
    """Verify a provider .deb against the authenticated index; return (deb, stanza)."""
    pkg = row.get("ubuntu26_pkg")
    if not pkg:
        return None, ("no provider package mapped in resolution table", None)
    stanza = index.get(pkg)
    if not stanza:
        return None, (f"{pkg} not present in authenticated Ubuntu index", None)
    arch_ok = "arm64" in (stanza.get("Architecture") or "").split()
    if not arch_ok:
        return None, (f"{pkg} architecture {stanza.get('Architecture')} is not arm64", None)
    filename = stanza.get("Filename")
    if not filename:
        return None, (f"{pkg} has no Filename in index", None)
    sha = stanza.get("SHA256")
    size = int(stanza.get("Size", 0) or 0)
    url = f"{UBUNTU_BASE}/{filename}"
    deb = provider_cache / Path(filename).name
    try:
        if not ensure_input(offline, deb, sha, size, url):
            return None, (f"{pkg} could not be verified", None)
    except RuntimeError as e:
        return None, (f"provider download error: {e}", None)
    return deb, (None, stanza)


def prove_provider(soname: str, row: dict, index: dict[str, dict],
                   provider_cache: Path, offline: bool) -> dict:
    """Actually prove one provider: authenticated index -> download+verify deb ->
    extract -> inspect the ELF whose SONAME matches (AArch64). Returns a dict with
    'proved': True or 'blocked': reason. Full identity (archive sha/bytes/arch/
    Filename/path + version definitions + transitive needed) is retained."""
    deb, err = _provider_deb(row, index, provider_cache, offline)
    if deb is None:
        return {"blocked": f"{soname}: {err[0]}"}
    stanza = err[1]
    pkg = row.get("ubuntu26_pkg")
    filename = stanza.get("Filename")
    sha = stanza.get("SHA256")
    size = int(stanza.get("Size", 0) or 0)

    hit = None
    try:
        with tempfile.TemporaryDirectory() as td:
            subprocess.run(["dpkg-deb", "-x", str(deb), td], check=True)
            tree = Path(td)
            for f in sorted(tree.rglob("*")):
                if not f.is_file() or f.is_symlink():
                    continue
                try:
                    with open(f, "rb") as fh:
                        head = fh.read(4)
                except OSError:
                    continue
                if head != b"\x7fELF":
                    continue
                try:
                    info = inspect_elf(f)
                except RuntimeError:
                    continue
                if info["soname"] == soname and info["machine"] == "AArch64":
                    hit = {"rel": str(f.relative_to(tree)), "elf": info}
                    break
    except (subprocess.CalledProcessError, OSError, RuntimeError) as e:
        return {"blocked": f"{soname}: provider extraction/inspection error: {e}"}

    if hit is None:
        return {"blocked": f"{soname}: provider {pkg} has no arm64 ELF with SONAME {soname}"}

    return {
        "proved": True,
        "role": "provider",
        "provider": pkg,
        "version": stanza.get("Version"),
        "architecture": stanza.get("Architecture"),
        "filename": filename,
        "sha256": sha,
        "bytes": size,
        "soname": soname,
        "path_in_pkg": hit["rel"],
        "rpath_runpath": hit["elf"].get("rpath_runpath", []),
        "max_glibc_symbol": hit["elf"]["max_glibc_symbol"],
        "needed": hit["elf"]["needed"],
        "version_needs": hit["elf"]["version_needs"],
        "defined_versions": hit["elf"]["defined_versions"],
    }


def prove_loader(row: dict, index: dict[str, dict], provider_cache: Path,
                 offline: bool) -> dict:
    """Prove the dynamic loader by inspecting the ACTUAL libc6 arm64 .deb ELF at
    the exact interpreter path. Returns full identity + defined versions.

    Under merged-usr, the runtime interpreter /lib/ld-linux-aarch64.so.1 maps to
    usr/lib/ld-linux-aarch64.so.1, which is a symlink to
    usr/lib/aarch64-linux-gnu/ld-linux-aarch64.so.1 (the real ELF). We require the
    exact chain and never pick an arbitrary same-basename ELF."""
    deb, err = _provider_deb(row, index, provider_cache, offline)
    if deb is None:
        return {"blocked": f"{LD_LINUX}: {err[0]}"}
    stanza = err[1]
    pkg = row.get("ubuntu26_pkg")
    try:
        with tempfile.TemporaryDirectory() as td:
            subprocess.run(["dpkg-deb", "-x", str(deb), td], check=True)
            tree = Path(td)
            # Real (non-symlink) ELF files named ld-linux-aarch64.so.1.
            candidates = [p for p in tree.rglob("*")
                          if p.is_file() and not p.is_symlink() and p.name == LD_LINUX]
            if not candidates:
                return {"blocked": f"{LD_LINUX}: libc6 deb contains no ELF named "
                                    f"{LD_LINUX} at the interpreter path"}
            # Canonical merged-usr real ELF: usr/lib/aarch64-linux-gnu/ld-linux-aarch64.so.1
            # (relative path inside the .deb). Reject arbitrary same-basename picks.
            canonical_rel = "usr/lib/aarch64-linux-gnu/ld-linux-aarch64.so.1"
            real = None
            for p in candidates:
                rel = p.relative_to(tree).as_posix()
                if rel == canonical_rel:
                    real = p
                    break
            if real is None:
                return {"blocked":
                        f"{LD_LINUX}: missing canonical merged-usr loader ELF "
                        f"{canonical_rel} in libc6 deb"}
            # Verify the exact symlink chain usr/lib/ld-linux-aarch64.so.1 ->
            # aarch64-linux-gnu/ld-linux-aarch64.so.1 (the /lib/... runtime path
            # under merged-usr).
            link = tree / "usr/lib/ld-linux-aarch64.so.1"
            if not link.is_symlink():
                return {"blocked":
                        f"{LD_LINUX}: usr/lib/ld-linux-aarch64.so.1 is not a symlink"}
            target = str(link.readlink())
            if target != "aarch64-linux-gnu/ld-linux-aarch64.so.1":
                return {"blocked":
                        f"{LD_LINUX}: unexpected loader symlink target {target!r}"}
            resolved = (link.parent / target).resolve()
            if resolved != real.resolve():
                return {"blocked":
                        f"{LD_LINUX}: loader symlink does not resolve to canonical ELF"}
            info = inspect_elf(real)
            if info["machine"] != "AArch64" or info["class"] != "ELF64":
                return {"blocked": f"{LD_LINUX}: interpreter ELF is not AArch64 ELF64"}
            if info.get("rpath_runpath"):
                return {"blocked":
                        f"{LD_LINUX}: loader declares RPATH/RUNPATH "
                        f"{info['rpath_runpath']}"}
    except (subprocess.CalledProcessError, OSError, RuntimeError) as e:
        return {"blocked": f"{LD_LINUX}: loader extraction/inspection error: {e}"}

    return {
        "proved": True,
        "role": LOADER_ROLE,
        "provider": pkg,
        "version": stanza.get("Version"),
        "architecture": stanza.get("Architecture"),
        "filename": stanza.get("Filename"),
        "sha256": stanza.get("SHA256"),
        "bytes": int(stanza.get("Size", 0) or 0),
        "soname": LD_LINUX,
        "path_in_pkg": canonical_rel,
        "interpreter_path": AARCH64_INTERPRETER,
        "max_glibc_symbol": info["max_glibc_symbol"],
        "needed": info["needed"],
        "version_needs": info["version_needs"],
        "defined_versions": info["defined_versions"],
    }


def prove_private_staged(soname: str, staged_elf: dict) -> dict:
    """Prove a private staged provider from the ACTUAL staged ELF (not a table string)."""
    if staged_elf.get("rpath_runpath"):
        return {"blocked":
                f"{soname}: private staged ELF declares RPATH/RUNPATH "
                f"{staged_elf['rpath_runpath']}"}
    return {
        "proved": True,
        "role": "private_staged",
        "provider": "stage",
        "version": None,
        "architecture": "arm64",
        "filename": None,
        "sha256": None,
        "bytes": None,
        "soname": soname,
        "path_in_pkg": staged_elf.get("rel") or staged_elf.get("path"),
        "sha256": staged_elf.get("sha256"),
        "bytes": staged_elf.get("size"),
        "max_glibc_symbol": staged_elf["max_glibc_symbol"],
        "needed": staged_elf["needed"],
        "version_needs": staged_elf["version_needs"],
        "defined_versions": staged_elf["defined_versions"],
    }


def prove_provider_closure(root_sonames, resolution, main_index, universe_index,
                           provider_cache, offline, staged_by_soname):
    """Resolve every needed SONAME to a verified provider.

    ``staged_by_soname`` maps SONAME -> inspect dict for staged private ELFs.
    The loader is proven by real libc6 .deb inspection; private staged providers
    by their actual staged ELF; everything else by the authenticated Ubuntu index.
    Returns (proven, blocked). Every transitive NEEDED edge is also resolved.
    """
    proven = {}
    blocked = {}
    worklist = set(root_sonames)
    sonames = resolution.get("sonames", {})
    while worklist:
        soname = worklist.pop()
        if soname in proven:
            continue
        row = sonames.get(soname)
        # Private staged provider: prove from the actual staged ELF.
        if soname in staged_by_soname:
            proven[soname] = prove_private_staged(soname, staged_by_soname[soname])
            for n in staged_by_soname[soname]["needed"]:
                if n not in proven and n not in blocked:
                    worklist.add(n)
            continue
        # Loader: real libc6 deb ELF at the exact interpreter path.
        if soname == LD_LINUX:
            if row is None:
                blocked[soname] = "loader not present in resolution table"
                continue
            source = row.get("ubuntu26_source", "main")
            index = main_index if source != "universe" else universe_index
            r = prove_loader(row, index, provider_cache, offline)
            if r.get("blocked"):
                blocked[soname] = r["blocked"]
                continue
            proven[soname] = r
            for n in r.get("needed", []):
                if n not in proven and n not in blocked:
                    worklist.add(n)
            continue
        if row is None:
            blocked[soname] = "not present in resolution table"
            continue
        verdict = row.get("verdict")
        if verdict == PRIVATE_ROLE:
            # Private staged but not found among staged ELFs: fail (no table-string proof).
            blocked[soname] = "PRIVATE_COMPAT_STAGED but no staged ELF with that SONAME"
            continue
        source = row.get("ubuntu26_source", "main")
        index = main_index if source != "universe" else universe_index
        r = prove_provider(soname, row, index, provider_cache, offline)
        if r.get("blocked"):
            blocked[soname] = r["blocked"]
            continue
        proven[soname] = r
        for n in r.get("needed", []):
            if n not in proven and n not in blocked:
                worklist.add(n)
    return proven, blocked


def check_version_edges(elf_reports, proven):
    """Enforce every consumer-required symbol version against provider definitions.

    Returns (edges, failures). Each edge maps a consumer ELF + needed SONAME to the
    required version set and the provider's defined version set; a required version
    the provider does not define is a hard failure.
    """
    edges = []
    failures = []
    for e in elf_reports:
        for son in e["needed"]:
            req = e["version_needs"].get(son, [])
            if not req:
                continue
            prov = proven.get(son)
            if prov is None:
                failures.append(f"{e['path']}: {son}: no verified provider")
                continue
            defined = set(prov.get("defined_versions", []))
            missing = sorted(v for v in req if v not in defined)
            edges.append({
                "consumer": e["path"],
                "consumer_rel": e.get("rel"),
                "soname": son,
                "required": sorted(req),
                "provider_role": prov.get("role"),
                "provider": prov.get("provider"),
                "provider_version": prov.get("version"),
                "provider_defined": sorted(defined),
                "missing": missing,
                "satisfied": not missing,
            })
            if missing:
                failures.append(f"{e['path']}: {son}: provider {prov.get('provider')} "
                                f"does not define {missing}")
    return edges, failures


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--stage", type=Path, required=True)
    ap.add_argument("--resolution", type=Path,
                    default=Path(__file__).with_name("chromium-soname-resolution.json"))
    ap.add_argument("--ubuntu-inrelease", type=Path, required=True,
                    help="Signed Ubuntu ports InRelease (Codename determines suite)")
    ap.add_argument("--ubuntu-keyring", type=Path, required=True,
                    help="ubuntu-archive-keyring.gpg")
    ap.add_argument("--provider-cache", type=Path, required=True,
                    help="Directory for verified provider .deb cache")
    ap.add_argument("--offline", action="store_true",
                    help="Only verify cached providers/indexes; never touch the network")
    args = ap.parse_args(argv)
    stage = args.stage.resolve()

    report = {"schema": BROWSER_PROOF_SCHEMA,
              "stage": str(stage), "acceptance": "NOT RUN"}
    failures = []

    # 1. Manifest integrity (files + symlinks) -----------------------------
    manifest_path = stage / MANIFEST_REL
    if not manifest_path.is_file():
        raise SystemExit("bundle manifest missing")
    manifest = load_json(manifest_path)
    problems = verify_stage_manifest_entries(stage, manifest)
    if problems:
        failures.extend(problems[:12])
    report["manifest"] = {"ok": not problems, "problems": problems[:12],
                          "entry_count": len(manifest.get("files", [])),
                          "manifest_sha256": sha256_file(manifest_path)}

    # 2. Every staged ELF ------------------------------------------------
    elf_reports = []
    external_needed = set()
    staged_by_soname = {}
    try:
        for p in staged_elfs(stage):
            info = inspect_elf(p)
            info["rel"] = p.relative_to(stage).as_posix()
            info["sha256"] = sha256_file(p)
            info["size"] = p.stat().st_size
            elf_reports.append(info)
            if info.get("rpath_runpath"):
                failures.append(
                    f"staged ELF declares RPATH/RUNPATH {info['rpath_runpath']}: "
                    f"{info['rel']}")
            if info["soname"]:
                staged_by_soname.setdefault(info["soname"], info)
            for n in info["needed"]:
                external_needed.add(n)
        if not elf_reports:
            failures.append("no staged ELF files found")
    except RuntimeError as e:
        failures.append(str(e))
    report["elfs"] = elf_reports
    report["external_needed_union"] = sorted(external_needed)

    # 3. Main binary interpreter ------------------------------------------
    main_binary = stage / BINARY_REL
    main_info = next((e for e in elf_reports if e["path"] == str(main_binary.resolve())), None)
    if main_info is None:
        failures.append("main binary not among staged ELFs")
    else:
        if main_info["interpreter"] != AARCH64_INTERPRETER:
            failures.append(f"main binary interpreter {main_info['interpreter']!r} != "
                            f"{AARCH64_INTERPRETER}")

    # 4. Sandbox -----------------------------------------------------------
    sb = stage / SANDBOX_REL
    sb_report = {"present": sb.is_file()}
    if sb.is_file():
        sb_report["mode"] = oct(stat.S_IMODE(sb.stat().st_mode))
        sb_report["vendor_mode_preserved"] = (stat.S_IMODE(sb.stat().st_mode) == SANDBOX_MODE)
        sb_report["expected_owner"] = manifest.get("sandbox", {}).get("expected_owner")
        sb_report["observed_uid"] = manifest.get("sandbox", {}).get("observed_uid")
        if sb_report["mode"] != oct(SANDBOX_MODE):
            failures.append("chrome-sandbox mode is not 4755")
        if manifest.get("sandbox", {}).get("expected_owner") != \
                {"uid": SANDBOX_EXPECTED_UID, "gid": 0}:
            failures.append("sandbox expected owner is not root:root")
    else:
        failures.append("chrome-sandbox missing")
    report["sandbox"] = sb_report

    # 5. Authenticate Ubuntu index + prove providers ------------------------
    resolution = load_json(args.resolution)
    report["resolution_sha256"] = sha256_file(args.resolution)
    inrelease_text = args.ubuntu_inrelease.read_text(errors="replace")
    sig = verify_inrelease_strict(args.ubuntu_keyring, args.ubuntu_inrelease, UBUNTU_ARCHIVE_FPR)
    _suite = inrelease_suite(inrelease_text)
    report["ubuntu_inrelease"] = {
        "path": str(args.ubuntu_inrelease),
        "suite": _suite,
        "codename": _inrelease_field(inrelease_text, "Codename"),
        "architectures": _inrelease_field(inrelease_text, "Architectures") or "",
        "verified": sig["verified"], "reason": sig["reason"], "exit": sig["exit"],
        "sha256": sha256_file(args.ubuntu_inrelease),
        "keyring": str(args.ubuntu_keyring),
        "key_fingerprint": UBUNTU_ARCHIVE_FPR,
        "validsigs": sig["status"].get("validsigs", []),
        "primary_fprs": sig["status"].get("primary_fprs", []),
    }
    if not sig["verified"]:
        failures.append("Ubuntu InRelease signature did not verify against pinned key: "
                        + str(sig["reason"]))
    else:
        try:
            main_index = ensure_ubuntu_index("main", inrelease_text, args.provider_cache, args.offline)
            universe_index = ensure_ubuntu_index("universe", inrelease_text, args.provider_cache, args.offline)
        except RuntimeError as e:
            failures.append("Ubuntu index load failed: " + str(e))
            main_index = {}
            universe_index = {}

        indexes = []
        for comp, idx in (("main", main_index), ("universe", universe_index)):
            suffix = f"{comp}/binary-arm64/Packages.xz"
            sha, size = find_index_sha256_text(inrelease_text, suffix)
            indexes.append({"component": comp, "sha256": sha, "bytes": size,
                            "matches_InRelease": bool(sha)})
        report["indexes"] = indexes

        root_sonames = {n for n in external_needed if n != AARCH64_INTERPRETER}
        proven, blocked = prove_provider_closure(
            root_sonames, resolution, main_index, universe_index,
            args.provider_cache, args.offline, staged_by_soname)
        report["providers"] = {
            "proved_count": sum(1 for v in proven.values() if v.get("proved")),
            "blocked": blocked,
            "proved": {k: v for k, v in proven.items() if v.get("proved")},
        }
        if blocked:
            failures.append("BLOCKED provider proof: "
                            + "; ".join(f"{k}: {reason}" for k, reason in list(blocked.items())[:12]))

        # 6. ABI symbol-version edge proof -----------------------------------
        # Enforce ALL transitive edges: every staged-ELF requirement AND every
        # provider's own requirement (provider -> its needed provider) must be
        # satisfied by the target provider's actual version definitions.
        provider_consumers = []
        for rec in proven.values():
            if rec.get("proved") and rec.get("version_needs"):
                provider_consumers.append({
                    "path": rec.get("path_in_pkg", "") or rec.get("soname", ""),
                    "rel": rec.get("path_in_pkg", "") or rec.get("soname", ""),
                    "needed": rec.get("needed", []),
                    "version_needs": rec.get("version_needs", {}),
                })
        edges, edge_failures = check_version_edges(elf_reports + provider_consumers, proven)
        report["version_edges"] = edges
        report["version_edge_count"] = len(edges)
        report["version_edge_satisfied"] = sum(1 for x in edges if x["satisfied"])
        if edge_failures:
            failures.extend(edge_failures[:20])

    ok = not failures
    report["verdict"] = "PASS" if ok else "FAIL"
    report["failures"] = failures
    report["static_failures"] = not ok

    print(json.dumps(report, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
