#!/usr/bin/env python3
"""Verify/extract the exact public donor and build the bundled fork. No models or tests.
Preparation is default; --build requires the actual CUDA toolkit and never ships a CPU stub.
"""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tarfile


def verify_linux_x64_elf(body):
    # ELF OSABI 0 is the normal System V encoding used by Linux executables.
    # CMake separately proves the configured target system is Linux; inspect actual output too.
    if (len(body) < 64 or body[:4] != b"\x7fELF" or body[4:7] != b"\x02\x01\x01"
            or body[7] not in (0, 3) or int.from_bytes(body[16:18], "little") not in (2, 3)
            or int.from_bytes(body[18:20], "little") != 62 or int.from_bytes(body[20:24], "little") != 1):
        raise ValueError("Actual worker must be an ELF64 little-endian x86_64 Linux target")
    return {"elfClass": 64, "endianness": "little", "machine": 62, "targetSystem": "Linux"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=pathlib.Path, required=True)
    parser.add_argument("--work", type=pathlib.Path, required=True)
    parser.add_argument("--install", type=pathlib.Path)
    parser.add_argument("--cmake", default="cmake")
    parser.add_argument("--nvcc", default="nvcc")
    parser.add_argument("--build", action="store_true")
    args = parser.parse_args()
    overlay = pathlib.Path(__file__).resolve().parent
    lock = json.loads((overlay / "strata-source-lock.json").read_text())
    archive = args.archive.read_bytes()
    if len(archive) != lock["archive"]["bytes"] or hashlib.sha256(archive).hexdigest() != lock["archive"]["sha256"]:
        raise ValueError("Exact donor archive mismatch")
    if args.work.exists():
        raise ValueError("A new empty work path is required; existing source is never overwritten")
    expected = {row["path"]: row for row in lock["rows"]}
    verified = {}
    with tarfile.open(args.archive, "r:gz") as source:
        roots = set()
        for member in source.getmembers():
            name = pathlib.PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts or not name.parts:
                raise ValueError("Unsafe archive path")
            roots.add(name.parts[0])
            if member.isdir():
                continue
            if not member.isfile() or len(name.parts) < 2:
                raise ValueError("Only the pinned regular source files are admitted")
            relative = pathlib.PurePosixPath(*name.parts[1:]).as_posix()
            row = expected.get(relative)
            if row is None or relative in verified or member.size != row["bytes"]:
                raise ValueError("Unexpected source member")
            original = source.extractfile(member)
            if original is None:
                raise ValueError("Missing original source body")
            with original:
                body = original.read(row["bytes"] + 1)
            blob = hashlib.sha1(b"blob " + str(len(body)).encode() + b"\0" + body).hexdigest()
            if len(body) != row["bytes"] or blob != row["gitBlobSha"] or hashlib.sha256(body).hexdigest() != row["sha256"]:
                raise ValueError("Pinned source body mismatch: " + relative)
            verified[relative] = body
        if len(roots) != 1 or set(verified) != set(expected) or len(verified) != lock["blobCount"]:
            raise ValueError("Incomplete immutable donor closure")
    args.work.mkdir(parents=True)
    upstream = args.work / "upstream"
    for relative, body in verified.items():
        destination = upstream / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(body)
    fork = args.work / "dulche-fork"
    fork.mkdir()
    for filename in ["CMakeLists.txt", "dulche_strata.h", "dulche_strata.cpp", "dulche_strata_worker.cpp"]:
        shutil.copyfile(overlay / filename, fork / filename)
    record = {"origin": lock["origin"], "commit": lock["commit"], "tree": lock["gitCommitTree"],
              "archiveSha256": lock["archive"]["sha256"], "verifiedFiles": len(verified),
              "qualification": "Verified source; no compilation, CUDA availability or model execution inferred."}
    (args.work / "source-verification.json").write_text(json.dumps(record, indent=2) + "\n")
    if not args.build:
        return
    if args.install is None or args.install.exists():
        raise ValueError("Build requires a new install directory")
    nvcc = shutil.which(args.nvcc)
    if nvcc is None:
        raise ValueError("CUDA toolkit is required; no CPU stub is shipped as an available Strata engine")
    version = subprocess.run([nvcc, "--version"], check=True, capture_output=True, text=True).stdout
    match = re.search(r"release (\d+)\.(\d+)", version)
    if match is None or tuple(map(int, match.groups())) < (12, 8):
        raise ValueError("Pinned donor requires CUDA 12.8 or newer")
    build = args.work / "build"
    subprocess.run([args.cmake, "-S", str(fork), "-B", str(build),
                    "-DDULCHE_STRATA_UPSTREAM=" + str(upstream), "-DCMAKE_BUILD_TYPE=Release",
                    "-DCMAKE_CUDA_COMPILER=" + nvcc, "-DSTRATA_ENABLE_NCCL=OFF",
                    "-DCMAKE_INSTALL_PREFIX=" + str(args.install)], check=True)
    subprocess.run([args.cmake, "--build", str(build), "--target", "dulche-strata-worker", "--parallel", "1"], check=True)
    built = build / "dulche-strata-worker"
    record["target"] = verify_linux_x64_elf(built.read_bytes())
    subprocess.run([args.cmake, "--install", str(build)], check=True)
    binary = args.install / "runtimes/linux-x64/native/dulche-strata-worker"
    if binary.read_bytes() != built.read_bytes():
        raise ValueError("Installed worker must be the exact inspected build output")
    verify_linux_x64_elf(binary.read_bytes())
    record["workerSha256"] = hashlib.sha256(binary.read_bytes()).hexdigest()
    record["workerBytes"] = binary.stat().st_size
    record["qualification"] = "Actual build artifact; runtime CUDA-device/model/capability acceptance is separately required."
    shutil.copyfile(overlay / "strata-source-lock.json", args.install / "strata-source-lock.json")
    (args.install / "strata-build-receipt.json").write_text(json.dumps(record, indent=2) + "\n")


if __name__ == "__main__":
    main()
