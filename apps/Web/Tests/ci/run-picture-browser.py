#!/usr/bin/env python3
"""Ordinary ephemeral CI: actual sealed publisher output + unchanged browser9.

No SDK build, provider, deployment, or new process custodian is implemented here.
The preceding maintained publisher step owns source build and sealing.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil

COMMANDS_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
RUNNER_SHA = "9d9547e8df62b78dfb0646c50a1c8bde986fa79743c73f4269b65b3ad2f535f3"
ORACLE_SHA = "9c78229fc21b55e68c32a0f77fd9b8431866e5895a258906ac895d0059552909"
PNG_SHA = "a17e304d0a333065202cf77c656d7bc86abdd37237b407a0a5cb870d33870297"
CAP = 192000000
MINIMUM = 256000000


def sha(file):
    digest = hashlib.sha256()
    with file.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write(file, data):
    file.write_text(json.dumps(data, indent=2) + "\n")


def inventory(root):
    rows = []
    for file in sorted(root.rglob("*")):
        if file.is_symlink():
            raise RuntimeError("Owned CI tree contains a symlink")
        if file.is_file():
            rows.append({"path": str(file.relative_to(root)), "bytes": file.stat().st_size, "sha256": sha(file)})
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--publication", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    output, publication = args.output.resolve(), args.publication.resolve()
    if (not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or output.exists()
            or output.is_relative_to(root) or not publication.is_dir() or publication.is_relative_to(root)):
        raise RuntimeError("Exact commit, completed real publisher and fresh external output required")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"state": "NOT_RUN", "sourceCommit": args.expected_commit,
              "scope": "Nine actual local Picture browser groups; not full Picture/provider/AT/hardware acceptance",
              "resourceScope": "192MB actual final output+temp+cache; not transient/deleted-byte peak proof"}
    commands = None
    before = []
    try:
        custodian = root / "apps/Web/Tests/ci/run-ordinary-native.py"
        if sha(custodian) != COMMANDS_SHA:
            raise RuntimeError("Reviewed maintained Commands source changed")
        spec = importlib.util.spec_from_file_location("maintained_b1_commands", custodian)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        runner = root / "apps/Web/Tests/PictureBrowser/run-picture-browser.cjs"
        oracle = runner.with_name("png-oracle.cjs")
        png = root / "apps/Web/Tests/PictureAdmission/Fixtures/Picture8x6.png"
        if (sha(runner), sha(oracle), sha(png)) != (RUNNER_SHA, ORACLE_SHA, PNG_SHA):
            raise RuntimeError("Actual procedure, independent oracle or approved PNG changed")
        receipt = publication / "public-artifact/publish-manifest.json"
        seal = publication / "public-artifact/seal.json"
        source_before = publication / "diagnostics/source-before.json"
        source_after = publication / "diagnostics/source-after.json"
        bundle = publication / "sdk-publish/wwwroot"
        receipt_value, seal_value = json.loads(receipt.read_text()), json.loads(seal.read_text())
        if (receipt_value["sourceCommit"] != args.expected_commit or receipt_value["sourceCommitAfter"] != args.expected_commit
                or receipt_value["exitCode"] != 0 or seal_value["sourceCommit"] != args.expected_commit
                or seal_value["receiptSha256"] != sha(receipt)):
            raise RuntimeError("Real maintained publisher receipt/commit/seal differs")
        publisher_result = json.loads((publication / "diagnostics/result.json").read_text())
        if publisher_result["status"] != "PASS":
            raise RuntimeError("Prior actual publisher did not pass")
        before = inventory(bundle)
        expected = sorted([{k: row[k] for k in ("path", "bytes", "sha256")} for row in receipt_value["publishFiles"]], key=lambda row: row["path"])
        if before != expected:
            raise RuntimeError("Actual sealed bundle member bytes differ before browser launch")
        env = os.environ.copy()
        for variable, name in {"TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp", "XDG_CACHE_HOME": "cache"}.items():
            owned = output / name
            owned.mkdir(exist_ok=True)
            env[variable] = str(owned)
        env.update(B3_PICTURE_GUI_GRANTED="granted", B3_PORT="18761", B3_PICTURE_CANDIDATE_MANIFEST=str(receipt),
                   B3_PICTURE_CANDIDATE_MANIFEST_SHA256=sha(receipt), B3_PICTURE_SOURCE_BEFORE=str(source_before),
                   B3_PICTURE_SOURCE_BEFORE_SHA256=sha(source_before), B3_PICTURE_SOURCE_AFTER=str(source_after),
                   B3_PICTURE_SOURCE_AFTER_SHA256=sha(source_after), PYTHONDONTWRITEBYTECODE="1")
        if shutil.disk_usage(output).free < 384000000:
            raise RuntimeError("CI prelaunch reserve below384MB")
        commands = module.Commands(diagnostics, env, root)
        if commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip() != args.expected_commit:
            raise RuntimeError("Actual checkout differs from github.sha")
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        version = commands.run("playwright-version", ["node", "-e",
            "console.log(require(require('node:path').join(process.env.PLAYWRIGHT_MODULE,'package.json')).version)"], 15).strip()
        if version != "1.62.0":
            raise RuntimeError("Requires actual maintained Playwright1.62.0")
        executable = commands.run("chromium-path", ["node", "-e",
            "console.log(require(process.env.PLAYWRIGHT_MODULE).chromium.executablePath())"], 15).strip()
        if not Path(executable).is_file():
            raise RuntimeError("Actual Playwright maintained Chromium missing")
        env["CHROMIUM_EXECUTABLE"] = executable
        # The exact same dict is retained by maintained Commands, not a new owner.
        commands.run("picture-browser-nine", ["node", str(runner), str(bundle), str(output / "raw"), str(png)], 240)
        record = commands.records[-1]
        if (record["error"] is not None or record["exit"] != 0 or not record["normalEOF"] or not record["familyClosed"]
                or not record["finalECHILD"] or record["signals"] or not all(row["gone"] for row in record["births"])):
            raise RuntimeError("Actual browser command/family cleanup was not normal")
        raw = json.loads((output / "raw/results.json").read_text())
        if (raw["counts"] != {"discovered": 9, "executed": 9, "passed": 9, "failed": 0, "notRun": 0}
                or raw["exit"] != 0 or raw["acceptance"] != "PASS_SCOPED_NINE" or raw["integrity"]["state"] != "PASS"
                or sum(raw["diagnostics"]["counts"].values()) != 0 or not raw["portRebind"]
                or len(raw["launches"]) != 2 or len(raw["closedLaunches"]) != 2):
            raise RuntimeError("Actual nine/native restart/runtime/port results incomplete")
        result.update(state="PASS_SCOPED_NINE", counts=raw["counts"], actualChromiumVersion=raw["launches"][0]["browserVersion"])
    except Exception as error:
        result.update(state="FAIL_OR_INCOMPLETE", error=repr(error))
    finally:
        try:
            if before and inventory(publication / "sdk-publish/wwwroot") != before:
                raise RuntimeError("Sealed bundle changed after actual browser")
            if commands:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                if commands.run("git-head-after", ["git", "rev-parse", "HEAD"], 15).strip() != args.expected_commit:
                    raise RuntimeError("Actual source commit changed")
            rows = inventory(output)
            write(diagnostics / "output-custody-before-final-receipt.json", rows)
            result["outputBytesBeforeFinalReceipt"] = sum(file.stat().st_size for file in output.rglob("*") if file.is_file())
        except Exception as error:
            result.update(state="FAIL_OR_INCOMPLETE", finalizationError=repr(error))
        # The finite durable receipt precedes the final actual capacity sample.
        # stdout + actual process exit are authoritative for the postwrite sample;
        # no internal receipt pretends to prove its own later write/capture bytes.
        payload = (json.dumps(result, indent=2) + "\n").encode()
        if len(payload) > 65536:
            raise RuntimeError("Bounded CI final receipt overflow")
        current = sum(file.stat().st_size for file in output.rglob("*") if file.is_file())
        if current + len(payload) > CAP or shutil.disk_usage(output).free - len(payload) < MINIMUM:
            result.update(state="FAIL_OR_INCOMPLETE", finalizationError="Prospective final receipt cap/reserve failed")
            payload = (json.dumps(result, indent=2) + "\n").encode()
        (diagnostics / "result.json").write_bytes(payload)
        final_bytes = sum(file.stat().st_size for file in output.rglob("*") if file.is_file())
        final_free = shutil.disk_usage(output).free
        result["postReceiptActualSample"] = {"outputBytes": final_bytes, "freeBytes": final_free,
                                            "scope": "Before stdout capture; root CI actual exit/stdout required"}
        if final_bytes > CAP or final_free < MINIMUM:
            result.update(state="FAIL_OR_INCOMPLETE", postReceiptError="Actual final output cap/reserve failed")
    print(json.dumps(result))
    return 0 if result["state"] == "PASS_SCOPED_NINE" else 1


if __name__ == "__main__":
    raise SystemExit(main())
