#!/usr/bin/env python3
"""Native ARM runner: authenticated Chromium browser supply for the normal CI recipe.

Runs the frozen Supplier16 pipeline in the required order:
  acquire (signed Debian source+binary graph) -> stage (real physical source gate)
  -> full provider ABI verification against the signed Ubuntu arm64 index (suite is
  read from the authenticated InRelease Codename: noble for this ARM CI job, resolute
  retained where previously valid), then exports a bounded source/provenance sidecar.
  It never launches the app/browser and never installs any system package. This
  script runs later on the native Ubuntu 24.04 arm64 runner under Root dispatch; it
  is a CI recipe driver, not a local execution of the product.

Large source archives are hashed by streaming and linked (not copied) into the
sidecar to avoid needless ~800 MiB duplication while keeping the physical source
gate and corresponding-source artifact complete.

Failure evidence is preserved: on any failing phase the raw (bounded) stdout/stderr
receipts, phase/exit/error records, and signed suite identity are written into the
evidence dir and the process exits nonzero. Acquired source archives and provider
caches are never copied into bounded evidence.
"""
from pathlib import Path
import argparse, datetime, hashlib, json, os, shutil, subprocess, sys

SCH = "9to1.chromium-native.supply-ci/v1"
_CHUNK = 1 << 20
_MAX_LOG = 1 << 20  # bounded diagnostic capture


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(_CHUNK), b""):
            h.update(block)
    return h.hexdigest()


def _link_or_copy_file(src: Path, dst: Path) -> None:
    try:
        os.link(src, dst)  # same-filesystem hardlink avoids a second large copy
    except OSError:
        shutil.copy2(src, dst)


def _link_or_copy_dir(src: Path, dst: Path) -> None:
    dst.mkdir(parents=True, mode=0o700)
    for p in sorted(src.iterdir()):
        if p.is_file():
            _link_or_copy_file(p, dst / p.name)
        elif p.is_dir():
            _link_or_copy_dir(p, dst / p.name)


def inrelease_diag(inrelease: Path) -> dict:
    """Best-effort signed-suite identity for failure diagnostics.

    This is diagnostic only; the authoritative suite validation lives in the
    verifier. We parse the same signed InRelease fields so failure evidence records
    the signed Suite/Codename/Architectures identity.
    """
    out: dict = {}
    try:
        text = inrelease.read_text(errors="replace")
        for key in ("Suite", "Codename", "Architectures"):
            for line in text.splitlines():
                if line.startswith(key + ":"):
                    out[key.lower()] = line.split(":", 1)[1].strip()
                    break
    except Exception:
        pass
    return out


class PhaseFailed(Exception):
    def __init__(self, name, rc, tail):
        super().__init__(f"phase {name} failed (exit {rc}): {tail}")
        self.name = name
        self.rc = rc
        self.tail = tail


def _write_failed(ev: Path, phases: list, diag: dict, last: PhaseFailed | None):
    payload = {
        "schema": "9to1.chromium-native.supply-ci.failed/v1",
        "at": now(),
        "exit": 1,
        "ubuntu_inrelease_identity": diag,
        "phases": phases,
        "failure": None if last is None else {
            "phase": last.name, "exit": last.rc, "stderr_tail": last.tail[:4096]},
    }
    (ev / "browser-supply-failed.json").write_text(json.dumps(payload, indent=2) + "\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--evidence-dir", type=Path, required=True,
                    help="Acquisition/verification evidence dir (holds source/ and receipts)")
    ap.add_argument("--stage-out", type=Path, required=True,
                    help="New stage parent; browser root is <stage-out>/opt/9to1-preview/browser")
    ap.add_argument("--provider-cache", type=Path, required=True,
                    help="Directory where authenticated provider .debs/indexes are cached")
    ap.add_argument("--inrelease", type=Path, required=True,
                    help="Signed Ubuntu ports InRelease (Codename determines suite)")
    ap.add_argument("--keyring", type=Path, required=True,
                    help="ubuntu-archive-keyring.gpg (must contain pinned archive FPR)")
    ap.add_argument("--sidecar", type=Path, required=True,
                    help="Output dir for the bounded source/provenance sidecar")
    ap.add_argument("--env-file", type=Path, required=True,
                    help="GITHUB_ENV file to append BROWSER_* exports to")
    args = ap.parse_args(argv)

    scripts = Path(__file__).resolve().parent
    W = scripts / "chromium-native"
    pinned = W / "chromium-pinned-manifest.json"
    source_pins = W / "source-pins.json"
    resolution = W / "chromium-soname-resolution.json"
    notices = W / "notices"
    for p in (pinned, source_pins, resolution):
        if not p.is_file():
            raise SystemExit(f"missing chromium-native input: {p}")

    ev = args.evidence_dir.resolve()
    ev.mkdir(parents=True, exist_ok=True)
    stage_parent = args.stage_out.resolve()
    if stage_parent.exists():
        raise SystemExit("refuse existing stage output: choose a new dir")
    cache = args.provider_cache.resolve()
    cache.mkdir(parents=True, exist_ok=True)
    inrel = args.inrelease.resolve()
    keyring = args.keyring.resolve()
    if not inrel.is_file() or not keyring.is_file():
        raise SystemExit("missing authenticated Ubuntu InRelease/keyring")

    diag = inrelease_diag(inrel)
    phases: list = []
    last_failure: PhaseFailed | None = None

    def run_phase(name: str, argv: list, stdout_to: Path | None = None):
        nonlocal last_failure
        phase = {"phase": name, "at": now(), "exit": None, "error": None}
        try:
            if stdout_to is not None:
                with stdout_to.open("w") as fh:
                    proc = subprocess.run(argv, stdout=fh,
                                          stderr=subprocess.PIPE, text=True)
            else:
                proc = subprocess.run(argv, capture_output=True, text=True)
            phase["exit"] = proc.returncode
            out = (stdout_to.read_text(errors="replace") if stdout_to is not None
                   else proc.stdout)
            phase["stdout_bytes"] = len(out)
            (ev / f"{name}.stdout.log").write_text(out[:_MAX_LOG], errors="replace")
            (ev / f"{name}.stderr.log").write_text(proc.stderr[:_MAX_LOG], errors="replace")
            if proc.returncode != 0:
                last_failure = PhaseFailed(name, proc.returncode, proc.stderr[-800:])
                phase["error"] = last_failure.tail
                phases.append(phase)
                _write_failed(ev, phases, diag, last_failure)
                raise last_failure
        except PhaseFailed:
            raise
        except Exception as e:
            last_failure = PhaseFailed(name, 1, repr(e))
            phase["error"] = repr(e)
            phases.append(phase)
            _write_failed(ev, phases, diag, last_failure)
            raise last_failure
        phases.append(phase)
        (ev / "browser-supply-phases.json").write_text(
            json.dumps({"schema": "9to1.chromium-native.supply-ci.phases/v1",
                        "at": now(), "phases": phases}, indent=2) + "\n")
        return proc

    try:
        # 1. Acquire + authenticate signed Debian graph (network on the runner).
        run_phase("acquire", [sys.executable, str(W / "acquire-chromium-debian.py"),
                               "--evidence-dir", str(ev),
                               "--pinned-manifest", str(pinned),
                               "--source-pins", str(source_pins)])

        # 2. Stage the private bundle (real physical source gate before any output).
        run_phase("stage", [sys.executable, str(W / "stage-chromium-bundle.py"),
                            "--debs-dir", str(ev / "chromium-debs"),
                            "--output-stage", str(stage_parent),
                            "--pinned-manifest", str(pinned),
                            "--acquire-receipt", str(ev / "chromium-acquire-receipt.json"),
                            "--notices", str(notices),
                            "--source-dir", str(ev / "source"),
                            "--source-pins", str(source_pins)])

        browser_root = stage_parent / "opt/9to1-preview/browser"
        if not (browser_root / "manifest.json").is_file():
            raise SystemExit("browser stage manifest missing after staging")

        # 3. Full actual provider ABI verification (downloads/verifies providers).
        proof_path = ev / "browser-proof.json"
        run_phase("proof", [sys.executable, str(W / "verify-chromium-bundle.py"),
                            "--stage", str(browser_root),
                            "--resolution", str(resolution),
                            "--ubuntu-inrelease", str(inrel),
                            "--ubuntu-keyring", str(keyring),
                            "--provider-cache", str(cache)],
                  stdout_to=proof_path)
        proof = json.loads(proof_path.read_text())
        if proof.get("verdict") != "PASS":
            last_failure = PhaseFailed("proof", 1,
                                       "browser ABI proof verdict != PASS")
            phases.append({"phase": "proof-verdict", "at": now(), "exit": 1,
                           "error": last_failure.tail})
            _write_failed(ev, phases, diag, last_failure)
            raise last_failure

        # 4. Bounded source/provenance sidecar (never inside installed app or git repo).
        sc = args.sidecar.resolve()
        if sc.exists():
            raise SystemExit("refuse existing sidecar dir")
        sc.mkdir(parents=True, mode=0o700)
        _link_or_copy_dir(ev / "source", sc / "source")
        for name in ("chromium-acquire-receipt.json", "browser-proof.json"):
            if (ev / name).is_file():
                shutil.copy2(ev / name, sc / name)
        shutil.copy2(pinned, sc / "chromium-pinned-manifest.json")
        shutil.copy2(source_pins, sc / "source-pins.json")
        shutil.copy2(browser_root / "manifest.json", sc / "browser-stage-manifest.json")
        _link_or_copy_dir(browser_root / "NOTICES", sc / "NOTICES")
        index = []
        for p in sorted(sc.rglob("*")):
            if p.is_file():
                index.append({"rel": str(p.relative_to(sc)), "bytes": p.stat().st_size,
                              "sha256": sha256_file(p)})
        (sc / "sidecar.sha256.json").write_text(json.dumps(
            {"schema": SCH, "at": now(),
             "files": index, "proof_verdict": proof.get("verdict")}, indent=2) + "\n")

        exports = {
            "BROWSER_STAGE": str(browser_root),
            "BROWSER_PROOF": str(proof_path),
            "BROWSER_SOURCE_DIR": str(ev / "source"),
            "BROWSER_SOURCE_PINS": str(source_pins),
            "BROWSER_UBUNTU_INRELEASE": str(inrel),
            "BROWSER_UBUNTU_KEYRING": str(keyring),
            "BROWSER_PROVIDER_CACHE": str(cache),
            "BROWSER_SIDECAR": str(sc),
        }
        with args.env_file.open("a") as fh:
            for key, val in exports.items():
                val = val.replace("\n", "")
                fh.write(f"{key}={val}\n")
        return 0
    except PhaseFailed:
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
