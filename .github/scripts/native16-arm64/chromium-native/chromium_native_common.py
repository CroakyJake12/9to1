#!/usr/bin/env python3
"""Shared helpers for the Chromium-native Debian acquisition/staging/verify pipeline.

All functions are pure (no network, no root, no engine execution). They are shared by
``acquire-chromium-debian.py``, ``stage-chromium-bundle.py``,
``verify-chromium-bundle.py`` and the focused Python tests.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import tempfile
import urllib.request
from pathlib import Path

# --------------------------------------------------------------------------- #
# Hashing / JSON helpers
# --------------------------------------------------------------------------- #
def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_json(path: Path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def dump_json(obj, path: Path) -> None:
    path.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8")


def parse_size(s: str) -> int:
    return int(str(s).strip())


# --------------------------------------------------------------------------- #
# Capacity guard
# --------------------------------------------------------------------------- #
def df_free_bytes(path: str = "/tmp") -> int:
    """Return free bytes on the filesystem containing ``path`` (Linux statvfs)."""
    try:
        st = os.statvfs(path)
        return st.f_bavail * st.f_frsize
    except OSError:
        return 0


def ensure_capacity(needed_bytes: int, reserve_bytes: int = 256 * 1024 * 1024,
                    path: str = "/tmp") -> None:
    """Raise RuntimeError unless ``needed_bytes`` + ``reserve_bytes`` fit in free space."""
    free = df_free_bytes(path)
    if free < (needed_bytes + reserve_bytes):
        raise RuntimeError(
            f"insufficient {path} capacity: need {needed_bytes + reserve_bytes} "
            f"(needed {needed_bytes} + reserve {reserve_bytes}), have {free}")


# --------------------------------------------------------------------------- #
# Debian index helpers
# --------------------------------------------------------------------------- #
def parse_package_stanza(packages_text: str, package_name: str):
    """Return the stanza dict for ``package_name`` from Packages (or Sources) text."""
    for block in packages_text.split("\n\n"):
        m = re.search(r"^Package:\s*(\S+)\s*$", block, re.MULTILINE)
        if not m or m.group(1) != package_name:
            continue
        return _parse_stanza_block(block)
    return None


def parse_all_package_stanzas(packages_text: str) -> list[dict]:
    """Return every package stanza in Packages (or Sources) text as a dict list."""
    out = []
    for block in packages_text.split("\n\n"):
        m = re.search(r"^Package:\s*(\S+)\s*$", block, re.MULTILINE)
        if not m:
            continue
        out.append(_parse_stanza_block(block))
    return out


def _parse_stanza_block(block: str) -> dict:
    stanza: dict = {}
    cur = None
    for line in block.splitlines():
        if re.match(r"^\S", line):
            key, _, value = line.partition(":")
            stanza[key] = value.strip()
            cur = key
        elif line.startswith(" ") and cur is not None:
            stanza[cur] += "\n" + line.strip()
    return stanza


def parse_sources_sha256(stanza: dict) -> list[dict]:
    """Parse a Sources stanza's ``Checksums-Sha256`` block.

    Returns a list of {name, size, sha256} entries.
    """
    block = stanza.get("Checksums-Sha256", "")
    out = []
    for line in block.splitlines():
        parts = line.split()
        if len(parts) == 3:
            out.append({"sha256": parts[0], "size": int(parts[1]), "name": parts[2]})
    return out


def find_sha256_by_name(sources_entries: list[dict], name: str):
    for e in sources_entries:
        if e["name"] == name:
            return e
    return None


def find_index_sha256(inrelease_text: str, path_suffix: str) -> tuple:
    """Return (sha256, bytes) for a file path listed in a Signed InRelease body."""
    body = inrelease_text
    in_sha256 = False
    for line in body.splitlines():
        if line.startswith("SHA256:"):
            in_sha256 = True
            continue
        if in_sha256:
            if not line.startswith(" "):
                in_sha256 = False
                continue
            parts = line.split()
            if len(parts) == 3 and parts[2].endswith(path_suffix):
                return parts[0], int(parts[1])
    return None, None


# --------------------------------------------------------------------------- #
# GPG STATUS parsing (machine-readable, fail-closed)
# --------------------------------------------------------------------------- #
_VALID_SIG_FIELDS = 10


def parse_gpg_status(status_text: str) -> dict:
    """Parse gpg --status-fd output.

    Returns a dict with:
      * ``valididsigs``: list of fingerprint strings (signing key) whose VALIDSIG
        was observed.
      * ``primary_fprs``: list of the PRIMARY key fingerprints from those VALIDSIGs.
      * ``rejected``: list of status tags that must cause rejection (BAD, REVKEY,
        REVKEYSIG, EXPSIG, EXPKEYSIG).
      * ``unknown_sigs``: list of fingerprints that could not be checked (NO_PUBKEY
        / ERRSIG / NO_SECKEY etc.), which are permitted only when a pinned VALIDSIG
        also exists.
    """
    validsigs = []
    primary = []
    rejected = []
    unknown = []
    for line in status_text.splitlines():
        line = line.strip()
        if not line.startswith("[GNUPG:]"):
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        tag = parts[1]
        if tag == "VALIDSIG":
            # VALIDSIG <fpr> <sigdate> <sig-expire> <version> <r> <algo> <halgo>
            #           <class> <primary-fpr>
            fpr = parts[2] if len(parts) > 2 else ""
            primary_fpr = parts[-1] if len(parts) >= _VALID_SIG_FIELDS + 2 else fpr
            validsigs.append(fpr)
            primary.append(primary_fpr)
        elif tag in ("BADSIG", "REVKEYSIG", "REVKEY", "EXPSIG", "EXPKEYSIG",
                     "KEYREVOKED", "KEYEXPIRED", "SIGEXPIRED"):
            rejected.append(tag)
        elif tag in ("NO_PUBKEY", "ERRSIG", "NO_SECKEY"):
            fpr = parts[2] if len(parts) > 2 else ""
            unknown.append((tag, fpr))
    return {
        "validsigs": validsigs,
        "primary_fprs": primary,
        "rejected": rejected,
        "unknown_sigs": unknown,
    }


def verify_inrelease_strict(keyring: Path, inrelease: Path,
                            required_fpr: str) -> dict:
    """Verify an InRelease against a keyring, requiring a pinned fingerprint.

    Verification succeeds only when:
      * gpg ran and emitted at least one VALIDSIG, and
      * that VALIDSIG's signing key fingerprint OR primary fingerprint equals the
        pinned ``required_fpr``, and
      * no revoked/expired/bad signature status was reported for a key we trust.
    Unknown co-signing keys (NO_PUBKEY/ERRSIG) are permitted only when the pinned
    VALIDSIG is present. The return includes ``verified`` (bool) and the parsed
    status, so callers can fail closed.
    """
    cmd = [
        "gpg", "--no-default-keyring", "--keyring", str(keyring),
        "--status-fd", "1", "--verify", str(inrelease),
    ]
    proc = None
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    except subprocess.TimeoutExpired:
        return {"verified": False, "reason": "gpg timed out", "exit": None,
                "status": {}}
    status = parse_gpg_status(proc.stdout or "")
    fingerprint_match = any(
        f == required_fpr for f in status["validsigs"] + status["primary_fprs"])
    verified = bool(status["validsigs"]) and fingerprint_match and not status["rejected"]
    return {
        "verified": verified,
        "exit": proc.returncode,
        "validsig": bool(status["validsigs"]),
        "fingerprint_match": fingerprint_match,
        "required_fpr": required_fpr,
        "status": status,
        "reason": None if verified else (
            "no VALIDSIG" if not status["validsigs"] else
            "pinned fingerprint not matched" if not fingerprint_match else
            "revoked/expired/bad signature status present"),
    }


# --------------------------------------------------------------------------- #
# Atomic download with hash/size verification
# --------------------------------------------------------------------------- #
def download_atomic(url: str, dest: Path, expected_sha256: str | None = None,
                    expected_size: int | None = None,
                    reserve_bytes: int = 256 * 1024 * 1024) -> Path:
    """Download ``url`` to a temp file, verify SHA256 (and size) when given, then
    atomically rename into place. Never leaves a partial file at the destination."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    ensure_capacity(expected_size or 1, reserve_bytes=reserve_bytes)
    part = dest.with_name(dest.name + ".part")
    req = urllib.request.Request(url, headers={"User-Agent": "9to1-chromium-native/1"})
    with urllib.request.urlopen(req, timeout=180) as resp, open(part, "wb") as out:
        while True:
            chunk = resp.read(1024 * 1024)
            if not chunk:
                break
            out.write(chunk)
    actual_sha = sha256_file(part)
    actual_size = part.stat().st_size
    if expected_sha256 is not None and actual_sha != expected_sha256:
        part.unlink(missing_ok=True)
        raise RuntimeError(f"SHA256 mismatch for {url}: got {actual_sha}, want {expected_sha256}")
    if expected_size is not None and actual_size != expected_size:
        part.unlink(missing_ok=True)
        raise RuntimeError(f"size mismatch for {url}: got {actual_size}, want {expected_size}")
    os.replace(part, dest)
    return dest


def ensure_input(offline: bool, local: Path, expected_sha: str, expected_size: int,
                 url: str, reserve_bytes: int = 256 * 1024 * 1024) -> bool:
    """Return True if the input is present and verified on disk.

    In offline mode this is the ONLY path; network mode falls back to downloading
    when the cache is missing/mismatched. Never returns True for unverified bytes.
    """
    if local.exists():
        if sha256_file(local) == expected_sha and \
                (expected_size is None or local.stat().st_size == expected_size):
            return True
    if offline:
        raise RuntimeError(f"--offline: cached input missing/mismatched: {local}")
    download_atomic(url, local, expected_sha, expected_size, reserve_bytes=reserve_bytes)
    return True


# --------------------------------------------------------------------------- #
# Stage path constants
# --------------------------------------------------------------------------- #
BROWSER_STAGE_REL = "opt/9to1-preview/browser"
BROWSER_PROOF_SCHEMA = "9to1.chromium-native.browser-proof/v1"

BINARY_REL = "chromium/chromium"
SANDBOX_REL = "chromium/chrome-sandbox"
PRIVATE_LIB_REL = "chromium/lib"
LAUNCHER_REL = "launcher"
MANIFEST_REL = "manifest.json"

SANDBOX_MODE = 0o4755
# Expected vendor-normal ownership of chrome-sandbox (root:root).
SANDBOX_EXPECTED_UID = 0
SANDBOX_EXPECTED_GID = 0


# --------------------------------------------------------------------------- #
# Stage manifest helpers (regular files + symlinks)
# --------------------------------------------------------------------------- #
def iter_stage_entries(stage_root: Path):
    """Yield (rel_path, entry) for every regular file and symlink under stage_root_path    Symlink targets are stored under 'link_target'. Regular files carry sha256, size
    and mode. Directories are skipped.
    """
    for p in sorted(stage_root.rglob("*")):
        rel = p.relative_to(stage_root)
        if p.is_dir():
            continue
        if p.is_symlink():
            yield rel, {
                "path": str(rel),
                "link_target": os.readlink(p),
                "mode": oct(stat.S_IMODE(lstat_mode(p))),
            }
        else:
            st = p.stat()
            yield rel, {
                "path": str(rel),
                "bytes": st.st_size,
                "sha256": sha256_file(p),
                "mode": oct(stat.S_IMODE(st.st_mode)),
            }


def lstat_mode(p: Path) -> int:
    return os.lstat(p).st_mode


def staged_elfs(stage: Path) -> list[Path]:
    """All staged regular files that look like ELF (magic 7f 45 4c 46)."""
    out = []
    for p in stage.rglob("*"):
        if p.is_symlink() or not p.is_file():
            continue
        try:
            with open(p, "rb") as fh:
                head = fh.read(4)
        except OSError:
            continue
        if head == b"\x7fELF":
            out.append(p)
    return out


def _readelf_d_soname(dyn_text: str) -> str | None:
    """Parse the DT_SONAME token out of ``readelf -d`` text (fail-closed)."""
    for line in dyn_text.splitlines():
        if "(SONAME)" in line and "Library soname:" in line:
            m = re.search(r"\[([^\]]+)\]", line)
            if m:
                return m.group(1)
    return None


def find_soname_elf(root: Path, soname: str, timeout: int = 30) -> Path | None:
    """Locate the real (non-symlink) AArch64 ELF under ``root`` whose SONAME matches.

    This is used by the stage to place private Debian provider libraries (flac /
    openh264) without inventing their real on-disk filenames: the file is found by
    its actual ELF SONAME. Returns None when no matching ELF is found.
    """
    libdir = root / "usr/lib/aarch64-linux-gnu"
    if not libdir.is_dir():
        return None
    for f in sorted(libdir.rglob("*")):
        if not f.is_file() or f.is_symlink():
            continue
        try:
            with open(f, "rb") as fh:
                if fh.read(4) != b"\x7fELF":
                    continue
        except OSError:
            continue
        try:
            r = subprocess.run(["readelf", "-d", str(f)],
                               capture_output=True, text=True, timeout=timeout)
        except (OSError, subprocess.SubprocessError):
            continue
        if r.returncode == 0 and _readelf_d_soname(r.stdout) == soname:
            return f
    return None


def verify_stage_manifest_entries(stage_root: Path, manifest: dict) -> list[str]:
    """Cross-check a stage manifest's file/symlink entries against disk.

    Returns a list of problem strings (empty means the manifest matches disk).
    For regular files this checks existence, size and sha256. For symlinks it
    checks that the link exists and its target matches, and that the link's target
    stays inside the stage (containment).
    """
    problems = []
    root_real = stage_root.resolve()
    seen = set()
    for entry in manifest.get("files", []):
        rel = entry.get("path")
        if not rel:
            problems.append("manifest entry without path")
            continue
        seen.add(str(rel))
        p = stage_root / rel
        if "link_target" in entry:
            if not p.is_symlink():
                problems.append(f"expected symlink {rel}")
                continue
            actual_target = os.readlink(p)
            if actual_target != entry["link_target"]:
                problems.append(f"symlink target mismatch {rel}: got {actual_target}, "
                                f"want {entry['link_target']}")
            resolved = p.resolve()
            try:
                resolved.relative_to(root_real)
            except ValueError:
                problems.append(f"symlink escapes stage: {rel} -> {resolved}")
            continue
        if not p.is_file():
            problems.append(f"missing file {rel}")
            continue
        if p.stat().st_size != entry.get("bytes"):
            problems.append(f"size mismatch {rel}")
        if sha256_file(p) != entry.get("sha256"):
            problems.append(f"sha256 mismatch {rel}")
    return problems


# --------------------------------------------------------------------------- #
# GLIBC version parsing (numeric tuple comparison)
# --------------------------------------------------------------------------- #
_GLIBC_VER_RE = re.compile(r"GLIBC_([0-9]+)\.([0-9]+)")


def parse_glibc_tuple(s: str):
    m = _GLIBC_VER_RE.search(str(s))
    if not m:
        return None
    return (int(m.group(1)), int(m.group(2)))


def max_glibc_from_lines(lines) -> tuple | None:
    """Return the largest numeric GLIBC_x.y tuple found across ``lines``."""
    best = None
    for line in lines:
        m = _GLIBC_VER_RE.search(str(line))
        if m:
            t = (int(m.group(1)), int(m.group(2)))
            if best is None or t > best:
                best = t
    return best


# --------------------------------------------------------------------------- #
# Symbol-version proof (readelf --version-info --wide)
# --------------------------------------------------------------------------- #
# Real readelf ``--version-info --wide`` output shapes (captured from staged
# ARM64 ELFs and official Ubuntu provider packages):
#
#   Version definition section '.gnu.version_d' contains 3 entries:
#     000000: Rev: 1  Flags: BASE  Index: 1  Cnt: 1  Name: libjpeg.so.62
#     0x001c: Rev: 1  Flags: none  Index: 2  Cnt: 1  Name: LIBJPEG_6.2
#     0x0038: Rev: 1  Flags: none  Index: 3  Cnt: 1  Name: LIBJPEGTURBO_6.2
#     (entries with Cnt > 1 emit continuation lines "Parent <idx>: <ver>" which
#      MUST be ignored when collecting defined version names)
#
#   Version needs section '.gnu.version_r' contains 14 entries:
#     000000: Version: 1  File: libnss3.so  Cnt: 11
#     0x00e0:   Name: NSS_3.2  Flags: none  Version: 27
#     0x00f0:   Name: NSS_3.3  Flags: none  Version: 26
#
# ``parse_version_info`` returns the defined version names and the per-file
# required version names, plus which section markers were observed (so a caller
# can fail closed when a needed ELF carries no version data at all).
_VERSION_DEF_RE = re.compile(
    r"Rev:\s+\d+\s+Flags:\s+\S+\s+Index:\s+\d+\s+Cnt:\s+\d+\s+Name:\s+(\S+)")
_VERSION_NEED_FILE_RE = re.compile(
    r"Version:\s+\d+\s+File:\s+(\S+)\s+Cnt:\s+(\d+)")
_VERSION_NEED_NAME_RE = re.compile(
    r"Name:\s+(\S+)\s+Flags:\s+\S+\s+Version:\s+\d+")
_VERSION_DEF_SECTION_RE = re.compile(
    r"Version definition section .* contains\s+(\d+)\s+entr(?:y|ies):")
_VERSION_NEED_SECTION_RE = re.compile(
    r"Version needs section .* contains\s+(\d+)\s+entr(?:y|ies):")


def parse_version_info(lines) -> dict:
    """Parse ``readelf --version-info --wide`` lines into symbol-version data.

    Returns::

        {
          "definitions": [defined version names from .gnu.version_d],
          "needs": {needed-file-soname: [required version names]},
          "saw_def_section": bool,   # .gnu.version_d header observed
          "saw_need_section": bool,   # .gnu.version_r header observed
          "saw_any_section": bool,
        }

    This is a strict parser and never fabricates entries:

      * If a ``Version definition section`` header declares N entries, exactly N
        definition rows must be present (a header-only section fails).
      * If a ``Version needs section`` header declares M entries, exactly M
        per-file blocks must be present, and each block's collected ``Name`` rows
        must equal that block's declared ``Cnt`` (complete per-file needs).
      * ``Parent`` continuation lines in the definition section are naturally
        skipped (they do not match the ``Rev:`` definition pattern).

    Malformed/header-only/unknown section stdout raises ``RuntimeError`` rather
    than silently yielding an empty (max-none) result. Genuine versionless output
    (no version section headers at all) still returns empty sets so a caller can
    treat it as a legitimate no-version case.
    """
    definitions: list[str] = []
    needs: dict = {}
    need_expected: dict = {}
    mode = None  # "def" | "needs"
    cur_file = None
    saw_def = False
    saw_need = False
    def_declared = None
    need_declared = None
    def_rows = 0
    need_blocks = 0
    for line in lines:
        md = _VERSION_DEF_SECTION_RE.search(line)
        if md:
            mode = "def"
            saw_def = True
            def_declared = int(md.group(1))
            def_rows = 0
            continue
        mn = _VERSION_NEED_SECTION_RE.search(line)
        if mn:
            mode = "needs"
            saw_need = True
            need_declared = int(mn.group(1))
            need_blocks = 0
            cur_file = None
            continue
        if mode == "def":
            m = _VERSION_DEF_RE.search(line)
            if m:
                definitions.append(m.group(1))
                def_rows += 1
        elif mode == "needs":
            m = _VERSION_NEED_FILE_RE.search(line)
            if m:
                need_blocks += 1
                cur_file = m.group(1)
                needs.setdefault(cur_file, [])
                need_expected[cur_file] = int(m.group(2))
                continue
            m = _VERSION_NEED_NAME_RE.search(line)
            if m and cur_file is not None:
                needs[cur_file].append(m.group(1))

    if saw_def and def_declared is not None and def_rows != def_declared:
        raise RuntimeError(
            f"version definition section declared {def_declared} entries but "
            f"parsed {def_rows}")
    if saw_need and need_declared is not None and need_blocks != need_declared:
        raise RuntimeError(
            f"version needs section declared {need_declared} entry blocks but "
            f"parsed {need_blocks}")
    # Complete per-file needs: each block's Cnt must equal its collected Name rows.
    for fname, expected in need_expected.items():
        if len(needs.get(fname, [])) != expected:
            raise RuntimeError(
                f"version needs for {fname} incomplete: declared Cnt {expected} "
                f"but parsed {len(needs.get(fname, []))}")

    return {
        "definitions": definitions,
        "needs": needs,
        "saw_def_section": saw_def,
        "saw_need_section": saw_need,
        "saw_any_section": saw_def or saw_need,
    }


# --------------------------------------------------------------------------- #
# Real source build-graph gate (shared by stage creation and root package merge)
# --------------------------------------------------------------------------- #
def assert_source_file(source_dir: Path, name: str,
                       expected_bytes: int, expected_sha: str) -> str | None:
    """Return a problem description if ``source_dir/name`` is missing or does not
    match the expected bytes + SHA256; else None."""
    p = source_dir / name
    if not p.is_file():
        return f"missing source file {name}"
    if p.stat().st_size != expected_bytes:
        return f"source size mismatch {name}: got {p.stat().st_size}, want {expected_bytes}"
    if sha256_file(p) != expected_sha:
        return f"source sha256 mismatch {name}"
    return None


def validate_source_build_graph(source_dir: Path, source_pins: dict,
                                receipt: dict) -> dict:
    """Executable gate over the complete expected Chromium + JPEG + provider input set.

    ``source_pins`` is the pinned, authenticated source manifest (independent of the
    receipt). It must list the complete expected input set (names + bytes + SHA256)
    for chromium, libjpeg62-turbo, and the two private Debian provider sources
    (libflac14 / libopenh264-8). Every input must exist as a physical file
    under ``source_dir`` whose bytes and stream-SHA256 match exactly, and the
    receipt's source graph must carry the exact same complete set and source
    versions. Any extra, missing, replaced, corrupt or wrong-version source input
    fails the gate. There is no self-asserted bool/nonempty-list acceptance.
    """
    problems = []
    sg = receipt.get("source_graph")
    if not isinstance(sg, dict):
        return {"ok": False, "problems": ["receipt has no source graph"]}

    # --- Chromium ---------------------------------------------------------
    exp_cr_inputs = source_pins.get("inputs")
    if not isinstance(exp_cr_inputs, list) or not exp_cr_inputs:
        problems.append("source_pins chromium inputs missing/empty")
    exp_cr_version = source_pins.get("source_version")
    act_cr = sg.get("chromium") or {}
    if act_cr.get("version") != exp_cr_version:
        problems.append(f"chromium source version mismatch: "
                        f"{act_cr.get('version')!r} != {exp_cr_version!r}")
    act_cr_inputs = act_cr.get("inputs") or []
    exp_cr_names = {i["name"] for i in exp_cr_inputs} if isinstance(exp_cr_inputs, list) else set()
    act_cr_names = {i["name"] for i in act_cr_inputs}
    if exp_cr_names != act_cr_names:
        problems.append("chromium input set mismatch: missing="
                        + ",".join(sorted(exp_cr_names - act_cr_names))
                        + " extra=" + ",".join(sorted(act_cr_names - exp_cr_names)))
    for exp in exp_cr_inputs if isinstance(exp_cr_inputs, list) else []:
        name = exp["name"]
        act = next((i for i in act_cr_inputs if i.get("name") == name), {})
        if act.get("bytes") != exp["bytes"] or act.get("sha256") != exp["sha256"]:
            problems.append(f"chromium receipt hash mismatch {name}")
        e = assert_source_file(source_dir, name, exp["bytes"], exp["sha256"])
        if e:
            problems.append("chromium " + e)

    # --- JPEG -------------------------------------------------------------
    jpeg_pins = source_pins.get("libjpeg62-turbo") or {}
    exp_jp_inputs = jpeg_pins.get("inputs")
    if not isinstance(exp_jp_inputs, list) or not exp_jp_inputs:
        problems.append("source_pins libjpeg62-turbo inputs missing/empty")
    exp_jp_version = jpeg_pins.get("version")
    act_jp = sg.get("libjpeg62-turbo") or {}
    if exp_jp_version and act_jp.get("version") != exp_jp_version:
        problems.append(f"jpeg source version mismatch: "
                        f"{act_jp.get('version')!r} != {exp_jp_version!r}")
    act_jp_inputs = act_jp.get("inputs") or []
    exp_jp_names = {i["name"] for i in exp_jp_inputs} if isinstance(exp_jp_inputs, list) else set()
    act_jp_names = {i["name"] for i in act_jp_inputs}
    if exp_jp_names != act_jp_names:
        problems.append("jpeg input set mismatch: missing="
                        + ",".join(sorted(exp_jp_names - act_jp_names))
                        + " extra=" + ",".join(sorted(act_jp_names - exp_jp_names)))
    for exp in exp_jp_inputs if isinstance(exp_jp_inputs, list) else []:
        name = exp["name"]
        act = next((i for i in act_jp_inputs if i.get("name") == name), {})
        if act.get("bytes") != exp["bytes"] or act.get("sha256") != exp["sha256"]:
            problems.append(f"jpeg receipt hash mismatch {name}")
        e = assert_source_file(source_dir, name, exp["bytes"], exp["sha256"])
        if e:
            problems.append("jpeg " + e)

    # --- Private Debian providers (flac / openh264) -----------------------
    # The two new private staged providers (libFLAC.so.14, libopenh264.so.8) are
    # sourced from exact Debian trixie providers and must carry verified matching
    # source inputs, exactly like the private JPEG provider.
    for key, label, pkg in (("libflac14", "flac", "flac"),
                             ("libopenh264-8", "openh264", "openh264")):
        pins = source_pins.get(key) or {}
        exp_inputs = pins.get("inputs")
        if not isinstance(exp_inputs, list) or not exp_inputs:
            problems.append(f"source_pins {key} inputs missing/empty")
        exp_ver = pins.get("version")
        act = sg.get(key) or {}
        if exp_ver and act.get("version") != exp_ver:
            problems.append(f"{label} source version mismatch: "
                            f"{act.get('version')!r} != {exp_ver!r}")
        act_inputs = act.get("inputs") or []
        exp_names = {i["name"] for i in exp_inputs} if isinstance(exp_inputs, list) else set()
        act_names = {i["name"] for i in act_inputs}
        if exp_names != act_names:
            problems.append(f"{label} input set mismatch: missing="
                            + ",".join(sorted(exp_names - act_names))
                            + " extra=" + ",".join(sorted(act_names - exp_names)))
        for exp in exp_inputs if isinstance(exp_inputs, list) else []:
            name = exp["name"]
            act_i = next((i for i in act_inputs if i.get("name") == name), {})
            if act_i.get("bytes") != exp["bytes"] or act_i.get("sha256") != exp["sha256"]:
                problems.append(f"{label} receipt hash mismatch {name}")
            e = assert_source_file(source_dir, name, exp["bytes"], exp["sha256"])
            if e:
                problems.append(f"{label} " + e)
        # The provider source package identity must match the pinned source package.
        if pins.get("package") and act.get("package") != pins.get("package"):
            problems.append(f"{label} source package mismatch: "
                            f"{act.get('package')!r} != {pins.get('package')!r}")

    # Every expected source input must be marked verified in the receipt: this
    # binds the physical-file gate to the authenticated acquire result (the signed
    # index identity), not merely to a name/hash list.
    for exp, act, label in ((exp_cr_inputs, act_cr_inputs, "chromium"),
                             (exp_jp_inputs, act_jp_inputs, "jpeg"),
                             ((source_pins.get("libflac14") or {}).get("inputs"),
                              (sg.get("libflac14") or {}).get("inputs"),
                              "flac"),
                             ((source_pins.get("libopenh264-8") or {}).get("inputs"),
                              (sg.get("libopenh264-8") or {}).get("inputs"),
                              "openh264")):
        for wanted in (exp if isinstance(exp, list) else []):
            name = wanted.get("name")
            r = next((i for i in act if i.get("name") == name), {})
            if not (r.get("verified") is True):
                problems.append(f"{label} input not verified in receipt: {name}")
    # The pinned authenticated source-index identity must be a well-formed sha256.
    idx_sha = source_pins.get("source_archive_sha256")
    if idx_sha is not None and (not isinstance(idx_sha, str) or len(idx_sha) != 64):
        problems.append("source_archive_sha256 index identity malformed")

    return {"ok": not problems, "problems": problems}
def normalize_permissions(stage: Path, allowed_suid: set[Path],
                          allowed_suid_sha256: dict[Path, str] | None = None,
                          allowed_mode: int = SANDBOX_MODE) -> dict:
    """Normalize file modes under ``stage``.

    * Directories -> 0755.
    * Regular files -> 0755 if any exec bit is set, else 0644 — EXCEPT exactly the
      files in ``allowed_suid`` whose sha256 matches ``allowed_suid_sha256`` (when
      provided) and whose current mode equals ``allowed_mode``, which are preserved
      (vendor setuid sandbox). Any other SUID/SGID bit anywhere is stripped.
    * Returns a summary dict {preserved_suid: [...], stripped_uid: [...],
      normalized: n, skipped_symlinks: n}.
    """
    summary = {"preserved_suid": [], "stripped_special": [], "normalized": 0,
               "skipped_symlinks": 0}
    allowed_suid_real = {p.resolve() for p in allowed_suid}
    for f in stage.rglob("*"):
        if f.is_symlink():
            summary["skipped_symlinks"] += 1
            continue
        if f.is_dir():
            f.chmod(0o755)
            continue
        st = f.stat()
        mode = stat.S_IMODE(st.st_mode)
        is_allowed = (f.resolve() in allowed_suid_real
                      and stat.S_IMODE(mode) == allowed_mode)
        if allowed_suid_sha256 is not None:
            want_sha = allowed_suid_sha256.get(str(f))
            if want_sha is not None and sha256_file(f) != want_sha:
                is_allowed = False
        if is_allowed:
            summary["preserved_suid"].append(str(f))
            continue
        if mode & (stat.S_ISUID | stat.S_ISGID | stat.S_ISVTX):
            summary["stripped_special"].append(str(f))
        new_mode = 0o755 if mode & 0o111 else 0o644
        f.chmod(new_mode)
        summary["normalized"] += 1
    return summary
