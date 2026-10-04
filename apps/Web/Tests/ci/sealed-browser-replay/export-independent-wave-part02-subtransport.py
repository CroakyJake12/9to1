#!/usr/bin/env python3
"""Readonly official existing PART02 body verification and exact two-half export.

No browser, SDK, subprocess, network client or product/model execution here.
New subtransport preserves the original transport-manifest bytes verbatim.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import stat

EXPORTER_RUN = 37213508315
PART02_ARTIFACT = 11307686670
PART02_ARTIFACT_NAME = "team-b-independent-existing-wave-member-part02-11306648191-1a8cea03534e2b2767ec806f91dd2c454de94281"
PART02_OUTER_METADATA_BYTES = 19929162
PART02_OUTER_METADATA_SHA256 = "7cae57acf2b6030bc8f06532e78b75811be6a0e67daab9e550c5f33763f7576c"
SOURCE_RUN = 37212388505
SOURCE_ARTIFACT = 11306648191
MANIFEST_BYTES = 5914
MANIFEST_SHA256 = "71e288fa29ff7a87409be9250b9c14533a50ebdd952bde620cfa47aa29f89bfd"
PAYLOAD_BYTES = 19922944
PAYLOAD_SHA256 = "4dc73ed75c558ce71480d27b195f7196b48c38f7d410965d0fd08b2074048b4b"
SUBPART_BYTES = 9961472
CONTAINER_BYTES = 45290761
CONTAINER_SHA256 = "252fa5c350f631178bba5ada9110b6816314860b2d3d6d39e4fd06e47c2674f2"
MAX_OUTPUT = 32 * 1024 * 1024
FREE_FLOOR = 256 * 1024 * 1024
MAX_METADATA = 65536
MAX_SUBARTIFACT_BODY = 10000000 - 16384


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def pin(path):
    if path.is_symlink() or not path.is_file():
        raise RuntimeError("Regular nonsymlink body required: " + path.name)
    return {"bytes": path.stat().st_size, "sha256": digest(path)}


def inventory(root):
    if root.is_symlink() or not root.is_dir():
        raise RuntimeError("Actual regular extraction directory required")
    rows = {}
    for path in sorted(root.iterdir()):
        if path.name not in ("extracted-members.zip.part", "transport-manifest.json"):
            raise RuntimeError("Unexpected existing PART02 extraction entry: " + path.name)
        rows[path.name] = pin(path)
    if set(rows) != {"extracted-members.zip.part", "transport-manifest.json"}:
        raise RuntimeError("Exact two existing PART02 bodies required")
    if rows["extracted-members.zip.part"] != {"bytes": PAYLOAD_BYTES, "sha256": PAYLOAD_SHA256}:
        raise RuntimeError("Whole existing PART02 payload identity differs")
    if rows["transport-manifest.json"] != {"bytes": MANIFEST_BYTES, "sha256": MANIFEST_SHA256}:
        raise RuntimeError("Whole original transport-manifest identity differs")
    return rows


def no_duplicate_keys(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise RuntimeError("Duplicate original manifest key")
        value[key] = item
    return value


def write_json(path, value):
    raw = (json.dumps(value, indent=2) + "\n").encode()
    if len(raw) > MAX_METADATA:
        raise RuntimeError("Bounded subtransport metadata overflow")
    path.write_bytes(raw)


def owned_bytes(root):
    total = 0
    for path in root.rglob("*"):
        if path.is_symlink():
            raise RuntimeError("Owned output symlink unsupported")
        if path.is_dir():
            continue
        if not path.is_file():
            raise RuntimeError("Owned output special entry unsupported")
        total += path.stat().st_size
    return total


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.input.is_symlink() or args.output.is_symlink():
        raise RuntimeError("Root links unsupported")
    root, output = args.input.resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(root) or root.is_relative_to(output):
        raise RuntimeError("Fresh disjoint owned output required")
    source = Path(__file__).resolve()
    expected = source.with_name("wave-independent-original-transport-manifest.json")
    source_before = pin(source)
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    receipt = {"status": "FAIL", "sourceExporterRun": EXPORTER_RUN,
               "sourcePart02Artifact": PART02_ARTIFACT, "sourcePart02ArtifactName": PART02_ARTIFACT_NAME,
               "sourcePart02OuterMetadataBytes": PART02_OUTER_METADATA_BYTES,
               "sourcePart02OuterMetadataSha256": PART02_OUTER_METADATA_SHA256,
               "sourcePart02OuterWholeArchiveReadHere": False,
               "originalSourceRun": SOURCE_RUN, "originalSourceArtifact": SOURCE_ARTIFACT,
               "originalDiagnosticOuterWholeArchiveReadHere": False,
               "scope": "Only new exact subtransport of existing official PART02 extracted bodies; no browser rerun or product/source authority. Full original26/container/member/IDB/WAV reread remains a later independent operation.",
               "before": None, "after": None, "exporterBefore": source_before, "subparts": []}
    try:
        receipt["freeBefore"] = shutil.disk_usage(output).free
        if receipt["freeBefore"] < MAX_OUTPUT + FREE_FLOOR:
            raise RuntimeError("Remote output budget plus256MiB reserve unavailable")
        expected_before = pin(expected)
        if expected_before != {"bytes": MANIFEST_BYTES, "sha256": MANIFEST_SHA256}:
            raise RuntimeError("Reviewed whole original manifest reference differs")
        receipt["expectedManifestBefore"] = expected_before
        receipt["before"] = inventory(root)
        original_manifest = (root / "transport-manifest.json").read_bytes()
        expected_raw = expected.read_bytes()
        actual = json.loads(original_manifest, object_pairs_hook=no_duplicate_keys)
        wanted = json.loads(expected_raw, object_pairs_hook=no_duplicate_keys)
        if original_manifest != expected_raw or actual != wanted:
            raise RuntimeError("Full original transport-manifest byte/object equality required")
        if (actual["sourceRun"] != SOURCE_RUN or actual["sourceArtifact"] != SOURCE_ARTIFACT
                or actual["containerBytes"] != CONTAINER_BYTES or actual["containerSha256"] != CONTAINER_SHA256
                or len(actual["members"]) != 26 or len(actual["parts"]) != 3
                or actual["parts"][1] != {"part": 2, "bytes": PAYLOAD_BYTES, "sha256": PAYLOAD_SHA256,
                                        "path": "part02/extracted-members.zip.part"}):
            raise RuntimeError("Original source/container/part02 identity tuple differs")
        receipt["fullOriginalManifestEqual"] = True
        subparts_root = output / "subparts"
        subparts_root.mkdir()
        whole = hashlib.sha256()
        with (root / "extracted-members.zip.part").open("rb") as stream:
            for index in (1, 2):
                folder = subparts_root / ("subpart%02d" % index)
                folder.mkdir()
                payload = folder / "extracted-members.zip.part02.subpart"
                value = hashlib.sha256()
                remaining = SUBPART_BYTES
                with payload.open("xb") as target:
                    while remaining:
                        data = stream.read(min(1024 * 1024, remaining))
                        if not data:
                            raise RuntimeError("Existing PART02 stream ended before exact split")
                        target.write(data)
                        value.update(data)
                        whole.update(data)
                        remaining -= len(data)
                row = {"subpart": index, "offsetWithinPart02": (index - 1) * SUBPART_BYTES,
                       "offsetWithinContainer": PAYLOAD_BYTES + (index - 1) * SUBPART_BYTES,
                       "bytes": SUBPART_BYTES, "sha256": value.hexdigest(),
                       "path": payload.relative_to(subparts_root).as_posix()}
                if pin(payload) != {"bytes": SUBPART_BYTES, "sha256": row["sha256"]}:
                    raise RuntimeError("Actual subpart full body differs after write")
                receipt["subparts"].append(row)
                # This file is verbatim; no original fields/serialization are replaced.
                (folder / "transport-manifest.json").write_bytes(original_manifest)
            if stream.read(1) or whole.hexdigest() != PAYLOAD_SHA256:
                raise RuntimeError("Exact ordered split differs from whole PART02")
        # Independently re-read both written subparts in order, rather than only
        # trusting the read-source/write hash carried above.
        recovered = hashlib.sha256()
        recovered_bytes = 0
        for row in receipt["subparts"]:
            with (subparts_root / row["path"]).open("rb") as stream:
                for data in iter(lambda: stream.read(1024 * 1024), b""):
                    recovered.update(data)
                    recovered_bytes += len(data)
        if recovered_bytes != PAYLOAD_BYTES or recovered.hexdigest() != PAYLOAD_SHA256:
            raise RuntimeError("Written ordered subparts fail original PART02 reconstruction")
        for row in receipt["subparts"]:
            folder = subparts_root / ("subpart%02d" % row["subpart"])
            value = {"scope": receipt["scope"], "sourceExporterRun": EXPORTER_RUN,
                     "sourcePart02Artifact": PART02_ARTIFACT, "sourcePart02ArtifactName": PART02_ARTIFACT_NAME,
                     "sourcePart02OuterMetadataBytes": PART02_OUTER_METADATA_BYTES,
                     "sourcePart02OuterMetadataSha256": PART02_OUTER_METADATA_SHA256,
                     "sourcePart02OuterWholeArchiveReadHere": False,
                     "originalSourceRun": SOURCE_RUN, "originalSourceArtifact": SOURCE_ARTIFACT,
                     "originalManifest": {"bytes": MANIFEST_BYTES, "sha256": MANIFEST_SHA256},
                     "originalPart02": {"bytes": PAYLOAD_BYTES, "sha256": PAYLOAD_SHA256},
                     "originalContainer": {"bytes": CONTAINER_BYTES, "sha256": CONTAINER_SHA256},
                     "thisSubpart": row["subpart"], "orderedSubparts": receipt["subparts"]}
            write_json(folder / "subpart-manifest.json", value)
            if pin(folder / "transport-manifest.json") != expected_before:
                raise RuntimeError("Verbatim original manifest changed in subartifact")
            if owned_bytes(folder) >= MAX_SUBARTIFACT_BODY:
                raise RuntimeError("10MB subartifact upload overhead reserve exceeded")
        receipt["orderedWrittenSubpartsReconstructOriginalPart02"] = True
        receipt["status"] = "PASS"
    except Exception as error:
        receipt.update(status="FAIL", error=repr(error))
    finally:
        try:
            receipt["after"] = inventory(root)
            receipt["exporterAfter"] = pin(source)
            receipt["expectedManifestAfter"] = pin(expected)
            if (receipt["before"] != receipt["after"] or receipt["exporterBefore"] != receipt["exporterAfter"]
                    or receipt.get("expectedManifestBefore") != receipt["expectedManifestAfter"]):
                raise RuntimeError("Original two bodies/exporter/reference beforeafter changed")
            receipt["ownedLogicalBytesBeforeReceipt"] = owned_bytes(output)
            receipt["freeAfterBeforeReceipt"] = shutil.disk_usage(output).free
            if receipt["ownedLogicalBytesBeforeReceipt"] > MAX_OUTPUT or receipt["freeAfterBeforeReceipt"] < FREE_FLOOR:
                raise RuntimeError("Finite output/freefloor exceeded before receipt")
        except Exception as error:
            receipt.update(status="FAIL", finalCustodyError=repr(error))
        write_json(diagnostics / "subtransport-receipt.json", receipt)
        # Receipt is charged. Any postwrite violation exitsFAIL so uploads cannot
        # be labeled complete; prior receipt remains available as partial evidence.
        total, free = owned_bytes(output), shutil.disk_usage(output).free
        if total > MAX_OUTPUT or free < FREE_FLOOR:
            print(json.dumps({"status": "FAIL", "postReceiptOwnedLogicalBytes": total,
                              "postReceiptFreeBytes": free, "reason": "postwrite resource gate"}))
            return 1
    print(json.dumps({"status": receipt["status"], "postReceiptOwnedLogicalBytes": total,
                      "postReceiptFreeBytes": free, "sourcePart02Artifact": PART02_ARTIFACT,
                      "originalTwoBodiesUnchanged": receipt["before"] == receipt["after"],
                      "subparts": receipt["subparts"], "noBrowserOrSdkRerun": True}))
    return 0 if receipt["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
