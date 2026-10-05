#!/usr/bin/env python3
"""Compare original Writer runtime sequences; never rebuild or change the probes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import textwrap
import time
from zipfile import BadZipFile, ZipFile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def identity(path):
    data = path.read_bytes()
    return {"bytes": len(data), "sha256": sha(data)}


def checked_files(root, rows):
    if sorted(p.name for p in root.iterdir()) != sorted(row["path"] for row in rows):
        raise ValueError("Original probe artifact must contain exactly the six declared files")
    actual = []
    for row in rows:
        path = root / row["path"]
        if path.is_symlink() or not path.is_file() or identity(path) != {k: row[k] for k in ("bytes", "sha256")}:
            raise ValueError("Original probe body mismatch: " + row["path"])
        actual.append({"path": row["path"], **identity(path)})
    return actual


def source_rows(repo, catalog):
    actual = []
    for row in catalog["sourcePins"]:
        path = repo / row["path"]
        if path.is_symlink() or identity(path) != {k: row[k] for k in ("bytes", "sha256")}:
            raise ValueError("Unchanged original source mismatch: " + row["path"])
        actual.append({"path": row["path"], **identity(path)})
    return actual


def original_sections(workflow):
    fixture_step = workflow.split("      - name: Prepare disposable ODT fixture and runtime directory\n", 1)[1]
    fixture_step = fixture_step.split("      - name: Run prebuilt probes in minimal Ubuntu runtime container\n", 1)[0]
    fixture = textwrap.dedent(fixture_step.split("          python3 - <<'PY'\n", 1)[1].split("          PY\n", 1)[0])
    step = workflow.split("      - name: Run prebuilt probes in minimal Ubuntu runtime container\n", 1)[1]
    step = step.split("      - name: Verify runtime-only persisted edits outside container\n", 1)[0]
    body = textwrap.dedent(step.split("            bash -lc '\n", 1)[1].rsplit("            '\n", 1)[0])
    split = "mkdir -p /tmp/haven-write-profile\n"
    setup, sequence = body.split(split, 1)
    sequence = split + sequence
    sequence = sequence.split('dpkg-query -W -f="\\${Package}\\t\\${Version}\\n"', 1)[0]
    if fixture.count("with ZipFile(destination, 'w')") != 1 or sequence.count("bash /probe/run-ipc-probe.sh") != 1:
        raise ValueError("Original fixture/command extraction is not unique")
    return fixture, setup, sequence


SNAPSHOT = r'''snapshot() {
  local phase="$1"
  {
    printf 'phase=%s\n' "$phase"
    while IFS= read -r -d '' f; do
      printf 'file=%s bytes=%s sha256=' "${f#/io/}" "$(stat -c %s "$f")"
      sha256sum "$f" | cut -d ' ' -f 1
    done < <(find "/io/ARM" -maxdepth 2 -type f ! -name 'state-hashes.log' ! -name 'commands.log' -print0 | sort -z)
  } >> "/io/ARM/state-hashes.log"
}
'''


def arm_script(sequence, arm):
    if arm not in ("reused", "fresh"):
        raise ValueError("Unknown diagnostic arm")
    # Both arms get distinct, fresh profiles and the same original source bytes.
    body = sequence.replace("/io", "/io/" + arm)
    body = body.replace("/tmp/haven-write-profile", "/tmp/" + arm + "-haven-write-profile")
    body = body.replace("/tmp/haven-write-semantic-profile", "/tmp/" + arm + "-haven-write-semantic-profile")
    body = body.replace("/tmp/haven-write-engine-ipc-poc", "/tmp/" + arm + "-haven-write-engine-ipc-poc")
    basic_end = 'test "$(stat -c %s /io/' + arm + '/tile.rgba)" -eq $((512 * 512 * 4))\n'
    semantic_end = "test -s /io/" + arm + "/semantic-roundtrip.odt\n"
    ipc_start = "ENGINE_BINARY=/probe/haven_write_engine_poc \\\n"
    if body.count(basic_end) != 1 or body.count(semantic_end) != 1 or body.count(ipc_start) != 1:
        raise ValueError("Original runtime phase boundaries changed")
    body = body.replace(basic_end, basic_end + 'snapshot after-basic\n')
    body = body.replace(semantic_end, semantic_end + 'snapshot after-semantic\n')
    if arm == "fresh":
        # The only differing operation is a byte-exact document copy immediately
        # before IPC. Keep the reused document and every sidecar untouched.
        before_ipc, ipc = body.split(ipc_start, 1)
        old_arg = "  /io/fresh/source.odt \\\n"
        if ipc.count(old_arg) != 1:
            raise ValueError("Original IPC input argument is not unique")
        ipc = ipc.replace(old_arg, "  /io/fresh/ipc-input/source.odt \\\n")
        body = before_ipc + 'mkdir -p /io/fresh/ipc-input\ncp -- /io/fresh/source.odt /io/fresh/ipc-input/source.odt\ncmp -- /io/fresh/source.odt /io/fresh/ipc-input/source.odt\nsnapshot before-ipc-fresh-copy\n' + ipc_start + ipc
    else:
        body = body.replace(ipc_start, 'snapshot before-ipc-reused-path\n' + ipc_start)
    return "#!/usr/bin/env bash\nset -euo pipefail\n" + SNAPSHOT.replace("ARM", arm) + "snapshot before-basic\n" + body + "\nsnapshot after-ipc\n"


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def command(argv, log, rows, *, env=None, timeout=720):
    start = time.time()
    try:
        with log.open("wb") as output:
            completed = subprocess.run(argv, stdout=output, stderr=subprocess.STDOUT, env=env, timeout=timeout)
    except subprocess.TimeoutExpired:
        rows.append({"argv": argv, "exitCode": None, "timedOut": True, "durationSeconds": round(time.time() - start, 3), "log": log.name})
        raise
    rows.append({"argv": argv, "exitCode": completed.returncode, "durationSeconds": round(time.time() - start, 3), "log": log.name})
    if log.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("Completed diagnostic log exceeds the declared 8 MiB retention bound")
    return completed.returncode


def verify_arm(io, arm, probes, diagnostics, rows):
    root = io / arm
    checks = {}
    try:
        with ZipFile(root / "roundtrip.odt") as archive:
            content = archive.read("content.xml").decode("utf-8")
        if "HAVEN_WRITE_LOK_EDIT_PROOF_20260907" not in content:
            raise ValueError("Original roundtrip marker absent")
        if (root / "tile.rgba").stat().st_size != 512 * 512 * 4:
            raise ValueError("Original tile byte criterion failed")
        checks["basicPersistedMarkerAndTile"] = {"pass": True}
    except (OSError, ValueError, KeyError, BadZipFile, UnicodeError) as error:
        checks["basicPersistedMarkerAndTile"] = {"pass": False, "errorType": type(error).__name__}
    for kind, file, marker in (("semantic", "semantic-roundtrip.odt", None), ("ipc", "ipc-roundtrip.odt", "HAVEN_WRITE_LOK_IPC_PROOF_20260907")):
        argv = ["python3", str(probes / "verify-semantic-odt.py"), str(root / file)]
        if marker:
            argv.append(marker)
        try:
            code = command(argv, diagnostics / (arm + "-" + kind + "-outside-verifier.log"), rows, timeout=30)
            checks[kind + "PersistedBoldAndMarker"] = {"pass": code == 0, "exitCode": code}
        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
            checks[kind + "PersistedBoldAndMarker"] = {"pass": False, "exitCode": None, "errorType": type(error).__name__}
    return checks


def classification(arms):
    reused, fresh = arms["reused"], arms["fresh"]
    if not all(arm["originalFixtureUnchanged"] for arm in (reused, fresh)):
        return "FAIL_SOURCE_DOCUMENT_STATE_DIVERGENCE", 1
    if not all(reused["independentOutsideChecks"][k]["pass"] for k in ("basicPersistedMarkerAndTile", "semanticPersistedBoldAndMarker")):
        return "FAIL_REUSED_PRIOR_SEQUENCE_OR_PERSISTED_CHECK", 1
    if not reused["reachedOriginalIPC"]:
        return "FAIL_REUSED_ORIGINAL_IPC_NOT_REACHED", 1
    fresh_pass = fresh["originalSequenceExitCode"] == 0 and fresh["reachedOriginalIPC"] and all(c["pass"] for c in fresh["independentOutsideChecks"].values()) and fresh["freshIPCCopy"] == fresh["sourceAfterPrecedingProbes"]
    if not fresh_pass:
        return "FAIL_ACTUAL_RUNTIME_SEQUENCE_OR_PERSISTED_OUTPUT", 1
    if reused["originalSequenceExitCode"] != 0:
        return "DIAGNOSIS_REUSED_FAILURE_FRESH_ALL_ORIGINAL_CHECKS_PASS", 0
    if not all(c["pass"] for c in reused["independentOutsideChecks"].values()):
        return "FAIL_ACTUAL_RUNTIME_SEQUENCE_OR_PERSISTED_OUTPUT", 1
    return "DIAGNOSIS_HISTORICAL_FAILURE_NOT_REPRODUCED_BOTH_ARMS_PASS", 0


def finish_owned_container(container, diagnostics, rows):
    """Independent bounded custody attempts; an earlier error cannot skip later cleanup."""
    errors, state, forced, removed = [], None, False, False
    try:
        state = json.loads(subprocess.check_output(["docker", "inspect", container], timeout=30))[0]["State"]
        if not isinstance(state.get("Running"), bool):
            raise ValueError("Owned container inspection lacks a typed Running state")
    except Exception as error:
        state = None
        errors.append({"stage": "inspect", "errorType": type(error).__name__, "error": str(error)})
    if state is None or state["Running"]:
        forced = True
        try:
            code = command(["docker", "stop", "--time", "5", container], diagnostics / "owned-container-stop.log", rows, timeout=30)
            if code:
                errors.append({"stage": "stop", "exitCode": code})
        except Exception as error:
            errors.append({"stage": "stop", "errorType": type(error).__name__, "error": str(error)})
    try:
        code = command(["docker", "rm", container], diagnostics / "owned-container-remove.log", rows, timeout=30)
        removed = code == 0
        if code:
            errors.append({"stage": "remove", "exitCode": code})
    except Exception as error:
        errors.append({"stage": "remove", "errorType": type(error).__name__, "error": str(error)})
    return {"forcedCleanup": forced, "ownedContainerRemoved": removed, "cleanupErrors": errors}


def observe_inputs(root, rows):
    actual, errors = [], []
    for row in rows:
        try:
            path = root / row["path"]
            if path.is_symlink() or not path.is_file():
                raise ValueError("Declared original input is no longer a regular non-symlink file")
            actual.append({"path": row["path"], **identity(path)})
        except Exception as error:
            errors.append({"path": row["path"], "errorType": type(error).__name__})
    return actual, errors


def run(args):
    repo = Path(args.repo).resolve()
    probes = Path(args.probes).resolve()
    output = Path(args.output).resolve()
    catalog = json.loads((repo / ".github/validation/team-c-write-runtime-diagnosis.json").read_text())
    if output.exists():
        raise ValueError("Task-owned output must be fresh; existing files are preserved")
    if shutil.disk_usage(output.parent).free < 8 * 1024**3:
        raise ValueError("Disposable runner requires at least 8 GiB free before preparation")
    head = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()
    if head != args.expected_source:
        raise ValueError("Checked-out source does not match immutable workflow head")
    subprocess.run(["git", "-C", str(repo), "merge-base", "--is-ancestor", catalog["sourceBasis"], head], check=True)
    changed = subprocess.check_output(["git", "-C", str(repo), "diff", "--name-only", catalog["sourceBasis"], head], text=True).splitlines()
    if sorted(changed) != sorted(catalog["diagnosticPaths"]):
        raise ValueError("Diagnostic branch has changes outside its four declared additions")
    before_source = source_rows(repo, catalog)
    before_probes = checked_files(probes, catalog["originalProbes"])
    workflow = (repo / ".github/workflows/validate-write-libreoffice-poc.yml").read_text()
    fixture, setup, sequence = original_sections(workflow)
    output.mkdir()
    diagnostics = output / "public"
    diagnostics.mkdir()
    io = output / "io"
    io.mkdir()
    recipe = output / "recipe"
    recipe.mkdir()
    rows = []
    result = {"schemaVersion": 1, "scope": "ORIGINAL_PACKAGED_WRITER_TWO_ARM_DIAGNOSIS_ONLY_UNACCEPTED", "sourceCommit": head, "originalRun": catalog["originalRun"], "originalProbeArtifact": catalog["originalProbeArtifact"], "sourceBefore": before_source, "probeBefore": before_probes, "commands": rows, "oldFailurePreserved": True, "container": None, "forcedCleanup": False, "arms": {}}
    container = None
    try:
        # Execute the unchanged original fixture creator with only its own
        # RUNNER_TEMP destination. No alternative document text/oracle is used.
        (output / "haven-write-runtime-only").mkdir()
        env = dict(os.environ, RUNNER_TEMP=str(output))
        if command(["python3", "-c", fixture], diagnostics / "original-fixture.log", rows, env=env, timeout=30):
            raise ValueError("Original ODT fixture preparation failed")
        original = output / "haven-write-runtime-only/source.odt"
        for arm in ("reused", "fresh"):
            (io / arm).mkdir()
            shutil.copyfile(original, io / arm / "source.odt")
            if identity(original) != identity(io / arm / "source.odt"):
                raise ValueError("Arm starting fixture byte mismatch")
            (recipe / (arm + ".sh")).write_text(arm_script(sequence, arm))
        result["initialFixture"] = identity(original)
        # Retain the original package/ABI/no-GTK guards. Only pin the two
        # observed runtime package versions; an unavailable version is a real
        # prerequisite failure, not a replacement runtime success.
        for package, version in catalog["runtimePackages"].items():
            setup = re.sub(r"(?m)^(\s*)" + re.escape(package) + r"(\s*(?:\\)?$)", lambda m: m.group(1) + package + "=" + version + m.group(2), setup)
        for package, version in catalog["runtimePackages"].items():
            if package + "=" + version not in setup:
                raise ValueError("Original runtime apt package entry is not unique")
        setup += '''
test ! -e /usr/include/LibreOfficeKit/LibreOfficeKit.h
if command -v c++ >/dev/null || command -v g++ >/dev/null; then
  echo 'FAIL: compiler entered the minimal runtime' >&2; exit 1
fi
dpkg-query -W -f='${Package}\\t${Version}\\n' libreoffice-core-nogui libreoffice-writer-nogui > /io/runtime-package-versions.txt
'''
        master = setup + '''
for arm in reused fresh; do
  set +e
  bash "/recipe/$arm.sh" > "/io/$arm/commands.log" 2>&1
  status=$?
  set -e
  printf '%s\\n' "$status" > "/io/$arm/original-sequence-exit-code.txt"
done
'''
        (recipe / "master.sh").write_text(master)
        if command(["docker", "pull", "ubuntu:24.04"], diagnostics / "container-pull.log", rows):
            raise ValueError("Original Ubuntu runtime prerequisite could not be fetched")
        image = json.loads(subprocess.check_output(["docker", "image", "inspect", "ubuntu:24.04"], timeout=30))[0]
        result["runtimeImage"] = {"id": image["Id"], "repoDigests": image.get("RepoDigests", [])}
        container = subprocess.check_output(["docker", "create", "--init", "--mount", "type=bind,src=" + str(probes) + ",dst=/probe,readonly", "--mount", "type=bind,src=" + str(recipe) + ",dst=/recipe,readonly", "--mount", "type=bind,src=" + str(io) + ",dst=/io", "-e", "DEBIAN_FRONTEND=noninteractive", image["Id"], "bash", "/recipe/master.sh"], text=True, timeout=30).strip()
        if not re.fullmatch("[0-9a-f]{64}", container):
            raise ValueError("No exact owned container identity returned")
        code = command(["docker", "start", "--attach", container], diagnostics / "container-runtime.log", rows)
        state = json.loads(subprocess.check_output(["docker", "inspect", container], timeout=30))[0]["State"]
        result["container"] = {k: state[k] for k in ("Running", "Pid", "ExitCode", "OOMKilled", "StartedAt", "FinishedAt")}
        if code or state["Running"] or state["Pid"] or state["ExitCode"] or state["OOMKilled"]:
            raise ValueError("Original runtime prerequisite/container completion failed")
        versions = (io / "runtime-package-versions.txt").read_text()
        actual_versions = dict(line.split("\t", 1) for line in versions.splitlines())
        if actual_versions != catalog["runtimePackages"]:
            raise ValueError("Actual LibreOffice packages differ from the historical failing runtime")
        shutil.copyfile(io / "runtime-package-versions.txt", diagnostics / "runtime-package-versions.txt")
        for arm in ("reused", "fresh"):
            root = io / arm
            arm_errors = []
            try:
                code = int((root / "original-sequence-exit-code.txt").read_text())
            except (OSError, ValueError) as error:
                code = None
                arm_errors.append({"stage": "sequence-exit", "errorType": type(error).__name__})
            # Every independent original outside verifier is attempted for each
            # arm, even if its IPC operation returned a failure.
            checks = verify_arm(io, arm, probes, diagnostics, rows)
            try:
                fixture_now = identity(root / "source.odt")
            except OSError as error:
                fixture_now = None
                arm_errors.append({"stage": "source-document", "errorType": type(error).__name__})
            try:
                snapshots = (root / "state-hashes.log").read_text() if (root / "state-hashes.log").is_file() else ""
            except (OSError, UnicodeError) as error:
                snapshots = ""
                arm_errors.append({"stage": "IPC-phase", "errorType": type(error).__name__})
            phase = "before-ipc-reused-path" if arm == "reused" else "before-ipc-fresh-copy"
            reached_ipc = "phase=" + phase in snapshots.splitlines()
            result["arms"][arm] = {"originalSequenceExitCode": code, "independentOutsideChecks": checks, "sourceAfterPrecedingProbes": fixture_now, "originalFixtureUnchanged": fixture_now == result["initialFixture"], "reachedOriginalIPC": reached_ipc, "observationErrors": arm_errors, "negativeHelperCleanup": "Original supervisor signals/waits in its EXIT trap; ignored negative cleanup statuses do not prove graceful Close/Shutdown" if code else None}
            if arm == "fresh":
                copy = root / "ipc-input/source.odt"
                try:
                    result["arms"][arm]["freshIPCCopy"] = identity(copy) if copy.is_file() else None
                except OSError as error:
                    result["arms"][arm]["freshIPCCopy"] = None
                    arm_errors.append({"stage": "fresh-copy", "errorType": type(error).__name__})
            for file in ("commands.log", "state-hashes.log", "original-sequence-exit-code.txt", "ipc-helper.log", "ipc-client.log", "ipc-failure.txt", "source.odt", "roundtrip.odt", "semantic-roundtrip.odt", "ipc-roundtrip.odt"):
                if (root / file).is_file():
                    try:
                        shutil.copyfile(root / file, diagnostics / (arm + "-" + file))
                    except OSError as error:
                        arm_errors.append({"stage": "output-retention", "file": file, "errorType": type(error).__name__})
        result["status"], result["returnCode"] = classification(result["arms"])
        if any(arm["observationErrors"] for arm in result["arms"].values()):
            result["status"], result["returnCode"] = "FAIL_ARM_OBSERVATION_OR_OUTPUT_RETENTION", 1
    except Exception as error:
        result["status"] = "FAIL_DIAGNOSTIC_OR_RUNTIME_PREREQUISITE"
        result["errorType"] = type(error).__name__
        result["error"] = str(error)
        result["returnCode"] = 1
    finally:
        result["outcomeBeforeCustody"] = result.get("status", "FAIL_INTERRUPTED")
        result.setdefault("returnCode", 1)
        if container:
            result.update(finish_owned_container(container, diagnostics, rows))
            if not result["ownedContainerRemoved"] or result["forcedCleanup"] or result["cleanupErrors"]:
                result["status"] = "FAIL_OWNED_CONTAINER_CLOSURE"
                result["returnCode"] = 1
        result["sourceAfter"], source_errors = observe_inputs(repo, catalog["sourcePins"])
        result["probeAfter"], probe_errors = observe_inputs(probes, catalog["originalProbes"])
        try:
            result["exactSixProbeFilesAfter"] = sorted(p.name for p in probes.iterdir()) == sorted(row["path"] for row in catalog["originalProbes"])
            if not result["exactSixProbeFilesAfter"]:
                probe_errors.append({"stage": "declared-six-probe-set", "errorType": "OriginalProbeSetChanged"})
        except OSError as error:
            result["exactSixProbeFilesAfter"] = False
            probe_errors.append({"stage": "declared-six-probe-set", "errorType": type(error).__name__})
        result["inputReadbackErrors"] = source_errors + probe_errors
        result["unchangedInputs"] = not result["inputReadbackErrors"] and result["sourceAfter"] == before_source and result["probeAfter"] == before_probes
        if not result["unchangedInputs"]:
            result["status"] = "FAIL_ORIGINAL_INPUT_CUSTODY"
            result["returnCode"] = 1
        result["qualification"] = "Exact original prebuilt probes on pinned system LibreOffice/Ubuntu container; not preserved donor build, approved VM/image, installed Write/Home authority, or full release acceptance. Container state/removal is not kernel family attestation; negative helper forced cleanup stays qualified."
        write_json(diagnostics / "result.json", result)
        artifacts = [{"path": p.name, **identity(p)} for p in sorted(diagnostics.iterdir()) if p.is_file()]
        write_json(diagnostics / "public-index.json", {"files": artifacts, "scope": result["scope"]})
    return result["returnCode"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True)
    parser.add_argument("--probes", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--expected-source", required=True)
    raise SystemExit(run(parser.parse_args()))
