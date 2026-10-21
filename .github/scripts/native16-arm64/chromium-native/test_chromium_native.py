#!/usr/bin/env python3
"""Focused Python tests for the Chromium-native Debian acquisition/staging/verify pipeline.

These tests are pure (no network, no root, no engine execution). They cover:
  * Debian index/stanza parsing and signed-index hash extraction.
  * SHA256 helpers.
  * GPG machine-readable STATUS parsing and fail-closed InRelease signature checks
    (fake "Good signature" text, wrong fingerprint, unknown-only, rejected statuses).
  * Stage manifest integrity (nested locale hierarchy, hash mismatch, symlink escape).
  * Permission normalization (vendor setuid preservation, stripping unexpected bits).
  * Browser-stage guard gates (source gate, sandbox spec, full stage).
  * ELF inspection fail-closed on a non-ELF input.
"""
from __future__ import annotations

import json
import os
import stat
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from chromium_native_common import (  # noqa: E402
    find_index_sha256,
    max_glibc_from_lines,
    normalize_permissions,
    parse_gpg_status,
    parse_glibc_tuple,
    parse_package_stanza,
    parse_sources_sha256,
    parse_version_info,
    sha256_bytes,
    sha256_file,
    staged_elfs,
    validate_source_build_graph,
    verify_inrelease_strict,
    verify_stage_manifest_entries,
)

import importlib.util


def _load_hyphenated(name: str):
    path = HERE / f"{name}.py"
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


_verify_mod = _load_hyphenated("verify-chromium-bundle")
inspect_elf = _verify_mod.inspect_elf
check_version_edges = _verify_mod.check_version_edges
prove_private_staged = _verify_mod.prove_private_staged

_guard_mod = _load_hyphenated("browser_stage_guard")
validate_browser_stage = _guard_mod.validate_browser_stage
validate_source_gate = _guard_mod.validate_source_gate
validate_sandbox_spec = _guard_mod.validate_sandbox_spec
validate_manifest_identity = _guard_mod.validate_manifest_identity
validate_path_contained = _guard_mod.validate_path_contained
validate_browser_proof = _guard_mod.validate_browser_proof
_validate_browser_proof_layer1 = _guard_mod._validate_browser_proof_layer1
derive_libc_floor = _guard_mod.derive_libc_floor
derive_browser_deps = _guard_mod.derive_browser_deps
BROWSER_PROOF_SCHEMA = _guard_mod.BROWSER_PROOF_SCHEMA

_acquire_mod = _load_hyphenated("acquire-chromium-debian")
ensure_input = _acquire_mod.ensure_input

PINNED_MAIN_FPR = "B8E5F13176D2A7A75220028078DBA3BC47EF2265"
PINNED_SECURITY_FPR = "5E04A1E3223A19A20706E20F9904613D4CCE68C6"

# [GNUPG:] VALIDSIG <fpr> <sigdate> <sigexpire> <version> <reserved> <algo> <halgo> <class> <primary>
VALID_MAIN = "[GNUPG:] VALIDSIG " + PINNED_MAIN_FPR + " 2026-01-01 0 3 0 8 9 24 " + PINNED_MAIN_FPR
VALID_SECURITY = "[GNUPG:] VALIDSIG " + PINNED_SECURITY_FPR + " 2026-01-01 0 3 0 8 9 24 " + PINNED_SECURITY_FPR
VALID_WRONG = "[GNUPG:] VALIDSIG " + "1" * 40 + " 2026-01-01 0 3 0 8 9 24 " + "1" * 40
UNKNOWN_ONLY = "[GNUPG:] NO_PUBKEY AABBCCDD00112233445566778899"
REJECTED_LINE = "[GNUPG:] REVKEYSIG " + PINNED_MAIN_FPR + " 2026-01-01 0 3 0 8 9 24"


def _mk_completed(stdout="", returncode=0):
    p = mock.Mock()
    p.stdout = stdout
    p.returncode = returncode
    return p


PACKAGES_SAMPLE = """\
Package: chromium
Version: 155.0.8059.79-1~deb13u1
Architecture: arm64
Filename: pool/updates/main/c/chromium/chromium_155.0.8059.79-1~deb13u1_arm64.deb
SHA256: bb4334a065801d7482df4783b535ab16654fb71e6eac8a89bd49eb2e7082be27
Size: 79116104
Depends: libc6 (>= 2.38)

Package: chromium-common
Version: 155.0.8059.79-1~deb13u1
Architecture: arm64
Filename: pool/updates/main/c/chromium/chromium-common_155.0.8059.79-1~deb13u1_arm64.deb
SHA256: 2f8d97e652adc2afa8cc96f79141ffdff0ca4f11c3a35671e7b61f60d10eb2b5
"""

INRELEASE_SAMPLE = """\
-----BEGIN PGP SIGNED MESSAGE-----
Hash: SHA256

SHA256:
  1111111111111111111111111111111111111111111111111111111111111111 123 main/binary-arm64/Packages.xz
  2222222222222222222222222222222222222222222222222222222222222222 456 main/source/Sources.xz
"""


class TestDebianIndexParsing(unittest.TestCase):
    def test_parse_package_stanza(self):
        stanza = parse_package_stanza(PACKAGES_SAMPLE, "chromium")
        self.assertIsNotNone(stanza)
        self.assertEqual(stanza["Version"], "155.0.8059.79-1~deb13u1")
        self.assertEqual(stanza["SHA256"], "bb4334a065801d7482df4783b535ab16654fb71e6eac8a89bd49eb2e7082be27")

    def test_parse_package_stanza_missing(self):
        self.assertIsNone(parse_package_stanza(PACKAGES_SAMPLE, "no-such-package"))

    def test_find_index_sha256(self):
        sha, size = find_index_sha256(INRELEASE_SAMPLE, "main/binary-arm64/Packages.xz")
        self.assertEqual(sha, "1" * 64)
        self.assertEqual(size, 123)
        sha2, size2 = find_index_sha256(INRELEASE_SAMPLE, "main/source/Sources.xz")
        self.assertEqual(sha2, "2" * 64)
        self.assertEqual(size2, 456)


class TestSha256(unittest.TestCase):
    def test_sha256_bytes(self):
        self.assertEqual(sha256_bytes(b"abc"),
                         "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")

    def test_sha256_file(self):
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"abc")
            path = Path(fh.name)
        try:
            self.assertEqual(sha256_file(path), sha256_bytes(b"abc"))
        finally:
            path.unlink()


class TestGpgStatusParsing(unittest.TestCase):
    def test_fake_good_signature_text_is_not_a_validsig(self):
        # "Good signature" in human-readable stderr must NOT be treated as verified.
        status = parse_gpg_status("gpg: Good signature from \"Debian Archive\"\n")
        self.assertEqual(status["validsigs"], [])
        self.assertFalse(status["validsigs"])

    def test_valid_matching_primary(self):
        status = parse_gpg_status(VALID_MAIN + "\n")
        self.assertIn(PINNED_MAIN_FPR, status["validsigs"])
        self.assertIn(PINNED_MAIN_FPR, status["primary_fprs"])
        self.assertEqual(status["rejected"], [])

    def test_wrong_fingerprint(self):
        status = parse_gpg_status(VALID_WRONG + "\n")
        self.assertNotEqual(status["validsigs"], [])
        self.assertNotIn(PINNED_MAIN_FPR, status["validsigs"])

    def test_unknown_only(self):
        status = parse_gpg_status(UNKNOWN_ONLY + "\n")
        self.assertEqual(status["validsigs"], [])
        self.assertTrue(any(tag == "NO_PUBKEY" for tag, _ in status["unknown_sigs"]))

    def test_rejected_status_recorded(self):
        status = parse_gpg_status(VALID_MAIN + "\n" + REJECTED_LINE + "\n")
        self.assertTrue(any("REVKEY" in r for r in status["rejected"]))


class TestVerifyInreleaseStrict(unittest.TestCase):
    def _verify(self, stdout="", returncode=0, fpr=PINNED_MAIN_FPR):
        with mock.patch("chromium_native_common.subprocess.run", return_value=_mk_completed(stdout, returncode)) as m:
            res = verify_inrelease_strict(Path("/k"), Path("/inrel"), fpr)
        return res, m

    def test_good_pinned_sig_verified(self):
        res, _ = self._verify(stdout=VALID_MAIN + "\n")
        self.assertTrue(res["verified"])
        self.assertTrue(res["fingerprint_match"])
        self.assertIsNone(res["reason"])

    def test_security_key_accepted(self):
        res, _ = self._verify(stdout=VALID_SECURITY + "\n", fpr=PINNED_SECURITY_FPR)
        self.assertTrue(res["verified"])

    def test_wrong_fingerprint_fails(self):
        res, _ = self._verify(stdout=VALID_WRONG + "\n")
        self.assertFalse(res["verified"])
        self.assertEqual(res["reason"], "pinned fingerprint not matched")

    def test_unknown_only_fails(self):
        res, _ = self._verify(stdout=UNKNOWN_ONLY + "\n")
        self.assertFalse(res["verified"])
        self.assertEqual(res["reason"], "no VALIDSIG")

    def test_rejected_status_fails_even_with_validsig(self):
        res, _ = self._verify(stdout=VALID_MAIN + "\n" + REJECTED_LINE + "\n")
        self.assertFalse(res["verified"])
        self.assertEqual(res["reason"], "revoked/expired/bad signature status present")

    def test_exit2_unknown_fails(self):
        res, _ = self._verify(stdout=UNKNOWN_ONLY + "\n", returncode=2)
        self.assertFalse(res["verified"])
        self.assertEqual(res["exit"], 2)

    def test_exit2_empty_fails(self):
        res, _ = self._verify(stdout="", returncode=2)
        self.assertFalse(res["verified"])
        self.assertEqual(res["reason"], "no VALIDSIG")


class TestPermissionNormalization(unittest.TestCase):
    def test_preserves_vendor_suid_and_strips_unexpected(self):
        with tempfile.TemporaryDirectory() as td:
            stage = Path(td)
            sb = stage / "chrome-sandbox"
            sb.write_bytes(b"vendor sandbox")
            os.chmod(sb, 0o4755)
            rogue = stage / "rogue"
            rogue.write_bytes(b"rogue")
            os.chmod(rogue, 0o4755)
            plain = stage / "plain"
            plain.write_bytes(b"plain")
            dirx = stage / "d"
            dirx.mkdir()
            os.chmod(dirx, 0o1777)
            link = stage / "link"
            link.symlink_to("plain")

            summary = normalize_permissions(
                stage,
                allowed_suid={sb},
                allowed_suid_sha256={str(sb): sha256_file(sb)},
            )
            self.assertIn(str(sb), summary["preserved_suid"])
            self.assertEqual(stat.S_IMODE(sb.stat().st_mode), 0o4755)
            self.assertIn(str(rogue), summary["stripped_special"])
            self.assertEqual(stat.S_IMODE(rogue.stat().st_mode) & stat.S_ISUID, 0)
            self.assertEqual(stat.S_IMODE(plain.stat().st_mode), 0o644)
            self.assertEqual(stat.S_IMODE(dirx.stat().st_mode) & stat.S_ISVTX, 0)
            self.assertEqual(summary["skipped_symlinks"], 1)

    def test_wrong_suid_hash_is_not_preserved(self):
        with tempfile.TemporaryDirectory() as td:
            stage = Path(td)
            sb = stage / "chrome-sandbox"
            sb.write_bytes(b"vendor sandbox")
            os.chmod(sb, 0o4755)
            wrong_sha = "0" * 64
            summary = normalize_permissions(stage, allowed_suid={sb},
                                           allowed_suid_sha256={str(sb): wrong_sha})
            self.assertNotIn(str(sb), summary["preserved_suid"])
            self.assertIn(str(sb), summary["stripped_special"])


class TestStageManifestEntries(unittest.TestCase):
    def _mk(self):
        root = Path(tempfile.mkdtemp(prefix="9to1-stage-"))
        (root / "chromium/locales").mkdir(parents=True)
        (root / "chromium/locales/en-US.pak").write_bytes(b"localeAAA")
        (root / "chromium/lib").mkdir(parents=True)
        (root / "chromium/lib/libjpeg.so.62.3.0").write_bytes(b"jpeg")
        (root / "chromium/lib/libjpeg.so.62").symlink_to("libjpeg.so.62.3.0")
        return root

    def test_nested_locale_and_symlink_match(self):
        root = self._mk()
        manifest = {"files": [
            {"path": "chromium/locales/en-US.pak", "bytes": 9,
             "sha256": sha256_file(root / "chromium/locales/en-US.pak"), "mode": "0o644"},
            {"path": "chromium/lib/libjpeg.so.62.3.0", "bytes": 4,
             "sha256": sha256_file(root / "chromium/lib/libjpeg.so.62.3.0"), "mode": "0o644"},
            {"path": "chromium/lib/libjpeg.so.62", "link_target": "libjpeg.so.62.3.0"},
        ]}
        self.assertEqual(verify_stage_manifest_entries(root, manifest), [])

    def test_hash_mismatch_reported(self):
        root = self._mk()
        manifest = {"files": [
            {"path": "chromium/locales/en-US.pak", "bytes": 9,
             "sha256": "0" * 64, "mode": "0o644"},
        ]}
        problems = verify_stage_manifest_entries(root, manifest)
        self.assertTrue(any("sha256" in p for p in problems))

    def test_symlink_escape_reported(self):
        root = self._mk()
        # symlink pointing outside the stage
        (root / "chromium/lib/escape").symlink_to("/etc/passwd")
        manifest = {"files": [
            {"path": "chromium/lib/escape", "link_target": "/etc/passwd"},
        ]}
        problems = verify_stage_manifest_entries(root, manifest)
        self.assertTrue(any("escape" in p for p in problems))


class TestBrowserStageGuard(unittest.TestCase):
    def _build_source_fixture(self):
        """A minimal but schema-real source_dir + source_pins + matching build graph."""
        sd = Path(tempfile.mkdtemp(prefix="9to1-src-"))
        cr = b"chromium source data"
        jp = b"libjpeg-turbo source data"
        (sd / "chromium.dsc").write_bytes(cr)
        (sd / "libjpeg.dsc").write_bytes(jp)
        cr_sha = sha256_bytes(cr)
        jp_sha = sha256_bytes(jp)
        pins = {
            "source_version": "155.0.8059.79-1~deb13u1",
            "inputs": [
                {"name": "chromium.dsc", "bytes": len(cr), "sha256": cr_sha},
            ],
            "libjpeg62-turbo": {
                "version": "1:2.1.5-4",
                "inputs": [
                    {"name": "libjpeg.dsc", "bytes": len(jp), "sha256": jp_sha},
                ],
            },
        }
        pins_path = sd / "source-pins.json"
        pins_path.write_text(json.dumps(pins))
        graph = {
            "chromium": {"version": pins["source_version"], "inputs": [
                {"name": "chromium.dsc", "bytes": len(cr), "sha256": cr_sha,
                 "verified": True}]},
            "libjpeg62-turbo": {"version": pins["libjpeg62-turbo"]["version"], "inputs": [
                {"name": "libjpeg.dsc", "bytes": len(jp), "sha256": jp_sha,
                 "verified": True}]},
        }
        return sd, pins_path, graph

    def _build_valid_stage(self):
        root = Path(tempfile.mkdtemp(prefix="9to1-guard-"))
        (root / "chromium/lib").mkdir(parents=True)
        (root / "chromium/lib/libjpeg.so.62.3.0").write_bytes(b"jpeg")
        (root / "chromium/lib/libjpeg.so.62").symlink_to("libjpeg.so.62.3.0")
        (root / "chromium/chrome-sandbox").write_bytes(b"sandbox")
        os.chmod(root / "chromium/chrome-sandbox", 0o4755)
        (root / "chromium/chromium").write_bytes(b"\x7fELF")
        (root / "launcher").write_bytes(b"#!/bin/sh\n")
        files = [
            {"path": "chromium/lib/libjpeg.so.62.3.0", "bytes": 4,
             "sha256": sha256_file(root / "chromium/lib/libjpeg.so.62.3.0"), "mode": "0o644"},
            {"path": "chromium/lib/libjpeg.so.62", "link_target": "libjpeg.so.62.3.0"},
            {"path": "chromium/chrome-sandbox", "bytes": 7,
             "sha256": sha256_file(root / "chromium/chrome-sandbox"), "mode": "0o4755"},
            {"path": "chromium/chromium", "bytes": 4,
             "sha256": sha256_file(root / "chromium/chromium"), "mode": "0o755"},
            {"path": "launcher", "bytes": 10,
             "sha256": sha256_file(root / "launcher"), "mode": "0o755"},
        ]
        _, _, graph = self._build_source_fixture()
        manifest = {
            "schema": "9to1.chromium-native.stage-manifest/v1",
            "version": "155.0.8059.79-1~deb13u1",
            "sandbox": {
                "mode_hex": "0o4755",
                "expected_owner": {"uid": 0, "gid": 0},
                "observed_uid": 1000, "observed_gid": 1000, "owner_applied": False,
            },
            "sources_verified": True,
            "source_pins": {"build_graph": graph},
            "files": files,
        }
        (root / "manifest.json").write_text(json.dumps(manifest))
        return root

    def test_valid_stage_passes(self):
        stage = self._build_valid_stage()
        sd, pins_path, _ = self._build_source_fixture()
        m = validate_browser_stage(stage, sd, pins_path)
        self.assertTrue(m["sources_verified"])

    def test_valid_stage_missing_source_args_fails(self):
        # Without real physical source inputs the stage merge must refuse
        # (no self-asserted bool/nonempty-list gate).
        stage = self._build_valid_stage()
        with self.assertRaises(SystemExit):
            validate_browser_stage(stage)

    def test_source_gate_rejects_fabricated_graph(self):
        # Independent counterexample: a metadata-only fabricated graph with zero
        # physical source files must fail — never pass.
        sd = Path(tempfile.mkdtemp(prefix="9to1-cx-"))
        pins = {
            "source_version": "155.0.8059.79-1~deb13u1",
            "inputs": [{"name": "missing-source.tar.xz", "bytes": 10,
                        "sha256": "0" * 64}],
            "libjpeg62-turbo": {"version": "1:2.1.5-4", "inputs": []},
        }
        manifest = {"source_pins": {"build_graph": {
            "chromium": {"version": pins["source_version"], "inputs": [
                {"name": "missing-source.tar.xz", "bytes": 10,
                 "sha256": "0" * 64}]},
            "libjpeg62-turbo": {"version": "1:2.1.5-4", "inputs": []},
        }}}
        with self.assertRaises(SystemExit):
            validate_source_gate(manifest, sd, pins)

    def test_unlisted_file_rejected(self):
        stage = self._build_valid_stage()
        sd, pins_path, _ = self._build_source_fixture()
        (stage / "rogue.bin").write_bytes(b"x")
        with self.assertRaises(SystemExit):
            validate_browser_stage(stage, sd, pins_path)

    def test_validate_path_contained_rejects_escape(self):
        stage = self._build_valid_stage()
        outside = Path(tempfile.mkdtemp(prefix="9to1-out-")) / "x"
        outside.write_bytes(b"x")
        try:
            (stage / "escape-link").symlink_to(str(outside))
            with self.assertRaises(SystemExit):
                validate_path_contained(stage, "escape-link")
        finally:
            (stage / "escape-link").unlink(missing_ok=True)
            outside.unlink(missing_ok=True)
            outside.parent.rmdir()

    def test_sandbox_spec_non_root_expected_owner(self):
        stage = self._build_valid_stage()
        sd, pins_path, _ = self._build_source_fixture()
        manifest = json.loads((stage / "manifest.json").read_text())
        manifest["sandbox"]["expected_owner"] = {"uid": 1000, "gid": 1000}
        (stage / "manifest.json").write_text(json.dumps(manifest))
        with self.assertRaises(SystemExit):
            validate_browser_stage(stage, sd, pins_path)


class TestAcquireInputGate(unittest.TestCase):
    def test_offline_matching_input_verified(self):
        with tempfile.TemporaryDirectory() as td:
            local = Path(td) / "in"
            local.write_bytes(b"abc")
            self.assertTrue(ensure_input(True, local, sha256_file(local),
                                         local.stat().st_size, "http://x/in"))

    def test_offline_corrupt_input_raises(self):
        with tempfile.TemporaryDirectory() as td:
            local = Path(td) / "in"
            local.write_bytes(b"abc")
            with self.assertRaises(RuntimeError):
                ensure_input(True, local, "0" * 64, local.stat().st_size, "http://x/in")

    def test_offline_wrong_size_raises(self):
        with tempfile.TemporaryDirectory() as td:
            local = Path(td) / "in"
            local.write_bytes(b"abc")
            with self.assertRaises(RuntimeError):
                ensure_input(True, local, sha256_file(local), 99999, "http://x/in")

    def test_offline_missing_input_raises(self):
        with tempfile.TemporaryDirectory() as td:
            local = Path(td) / "missing"
            with self.assertRaises(RuntimeError):
                ensure_input(True, local, "0" * 64, 1, "http://x/in")


class TestElfFailClosed(unittest.TestCase):
    def test_non_elf_raises(self):
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"not an elf")
            path = Path(fh.name)
        try:
            with self.assertRaises(RuntimeError):
                inspect_elf(path)
        finally:
            path.unlink()


class TestGlibcNumericCompare(unittest.TestCase):
    def test_max_is_numeric_not_lexical(self):
        lines = ["GLIBC_2.43", "GLIBC_2.9", "GLIBC_2.34"]
        tup = max_glibc_from_lines(lines)
        self.assertEqual(tup, (2, 43))
        self.assertEqual(parse_glibc_tuple("GLIBC_2.9"), (2, 9))
        self.assertIsNone(parse_glibc_tuple("GLIBC_<"))

    def test_max_glibc_from_readelf_lines(self):
        lines = [
            "0x0000000000000002  01 (NEEDED)  Shared library: [libc.so.6]",
            "   Name: GLIBC_2.34",
            "   Name: GLIBC_2.38",
            "   Name: GLIBC_2.39",
        ]
        self.assertEqual(max_glibc_from_lines(lines), (2, 39))


class TestJpegSourceLinkage(unittest.TestCase):
    def test_parse_sources_sha256_includes_orig_tar_gz(self):
        # Regression: .orig.tar.gz must NOT be filtered by extension guessing.
        stanza = {"Checksums-Sha256": (
            "26cbf22aa3b3e327df072513f14a5ddfb4a7b9a3d78c46a5dccfd711c13ac743 2508 libjpeg-turbo_2.1.5-4.dsc\n"
            "254f3642b04e309fee775123133c6464181addc150499561020312ec61c1bf7c 2264471 libjpeg-turbo_2.1.5.orig.tar.gz\n"
            "739e7dc22904dccdc5ab105de57a6e4c1515c0e841e68226e6410ff4976e0e91 108128 libjpeg-turbo_2.1.5-4.debian.tar.xz\n")}
        entries = {e["name"]: e for e in parse_sources_sha256(stanza)}
        self.assertEqual(set(entries), {"libjpeg-turbo_2.1.5-4.dsc",
                                         "libjpeg-turbo_2.1.5.orig.tar.gz",
                                         "libjpeg-turbo_2.1.5-4.debian.tar.xz"})

    def test_pinned_jpeg_inputs_match_signed_hashes(self):
        # The pinned manifest must pin exactly the three signed JPEG stanza inputs.
        src_pins = json.loads((HERE / "source-pins.json").read_text())
        jpeg = src_pins["libjpeg62-turbo"]
        self.assertEqual(jpeg["version"], "1:2.1.5-4")
        names = {i["name"] for i in jpeg["inputs"]}
        self.assertEqual(names, {"libjpeg-turbo_2.1.5-4.dsc",
                                 "libjpeg-turbo_2.1.5.orig.tar.gz",
                                 "libjpeg-turbo_2.1.5-4.debian.tar.xz"})
        for i in jpeg["inputs"]:
            self.assertGreater(i["bytes"], 0)
            self.assertEqual(len(i["sha256"]), 64)

    def test_pure_source_gate_real_fixture(self):
        # End-to-end gate on a real physical fixture: passes only when every file
        # exists with matching bytes+SHA and versions match.
        sd = Path(tempfile.mkdtemp(prefix="9to1-gate-"))
        cr = b"chromium payload"; jp = b"jpeg payload"
        (sd / "a.dsc").write_bytes(cr); (sd / "b.tar.xz").write_bytes(jp)
        pins = {
            "source_version": "155.0.8059.79-1~deb13u1",
            "inputs": [{"name": "a.dsc", "bytes": len(cr), "sha256": sha256_bytes(cr)}],
            "libjpeg62-turbo": {"version": "1:2.1.5-4",
                                 "inputs": [{"name": "b.tar.xz", "bytes": len(jp),
                                             "sha256": sha256_bytes(jp)}]},
        }
        receipt = {"source_graph": {
            "chromium": {"version": pins["source_version"], "inputs": [
                {"name": "a.dsc", "bytes": len(cr), "sha256": sha256_bytes(cr),
                 "verified": True}]},
            "libjpeg62-turbo": {"version": "1:2.1.5-4", "inputs": [
                {"name": "b.tar.xz", "bytes": len(jp), "sha256": sha256_bytes(jp),
                 "verified": True}]},
        }}
        res = validate_source_build_graph(sd, pins, receipt)
        self.assertTrue(res["ok"])

        # Removing one physical file fails the gate (not just the receipt).
        (sd / "b.tar.xz").unlink()
        res2 = validate_source_build_graph(sd, pins, receipt)
        self.assertFalse(res2["ok"])
        self.assertTrue(any("missing" in p for p in res2["problems"]))


# --------------------------------------------------------------------------- #
# Review-3: real symbol-version proof (readelf --version-info) fixtures
# --------------------------------------------------------------------------- #
class _FakeProc:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.returncode = returncode


def _mocked_run(get_out):
    def run(cmd, **kw):
        return _FakeProc(get_out(cmd))
    return run


VINFO_VALID = """\
Version definition section '.gnu.version_d' contains 3 entries:
 Addr: 0x00000000000021c0  Offset: 0x000021c0  Link: 5 (.dynstr)
  000000: Rev: 1  Flags: BASE  Index: 1  Cnt: 1  Name: libjpeg.so.62
  0x001c: Rev: 1  Flags: none  Index: 2  Cnt: 1  Name: LIBJPEG_6.2
  0x0038: Rev: 1  Flags: none  Index: 3  Cnt: 1  Name: LIBJPEGTURBO_6.2
Version needs section '.gnu.version_r' contains 2 entries:
 Addr: 0x000000000000c0cc  Offset: 0x0000c0cc  Link: 9 (.dynstr)
  000000: Version: 1  File: libc.so.6  Cnt: 2
  0x0010:   Name: GLIBC_2.17  Flags: none  Version: 2
  0x0020:   Name: GLIBC_2.34  Flags: none  Version: 3
  0x0100: Version: 1  File: ld-linux-aarch64.so.1  Cnt: 1
  0x0110:   Name: GLIBC_2.17  Flags: none  Version: 4
"""


def _out_for(path, vinfo=VINFO_VALID, dyn=None):
    def out(cmd):
        if cmd[0] == "file":
            return f"x: {path}: ELF 64-bit LSB pie executable, ARM aarch64\n"
        if cmd[0] == "readelf":
            opts = cmd[1]
            if opts == "-h":
                return "Class:                             ELF64\nMachine:                           AArch64\n"
            if opts == "-l":
                return "Requesting program interpreter: /lib/ld-linux-aarch64.so.1\n"
            if opts == "-d":
                if dyn is not None:
                    return dyn
                return ("Dynamic section at offset 0x100 contains 3 entries:\n"
                        "  Tag        Type                         Name/Value\n"
                        " 0x0001 (NEEDED)  Shared library: [libc.so.6]\n"
                        " 0x0001 (NEEDED)  Shared library: [ld-linux-aarch64.so.1]\n"
                        " 0x000e (SONAME)  Library soname: [libfoo.so]\n")
            if "--version-info" in opts:
                return vinfo
        return ""
    return out


class TestVersionInfoParsing(unittest.TestCase):
    def test_parse_real_readelf_format(self):
        vi = parse_version_info(VINFO_VALID.splitlines())
        self.assertEqual(vi["definitions"],
                         ["libjpeg.so.62", "LIBJPEG_6.2", "LIBJPEGTURBO_6.2"])
        self.assertEqual(vi["needs"]["libc.so.6"], ["GLIBC_2.17", "GLIBC_2.34"])
        self.assertEqual(vi["needs"]["ld-linux-aarch64.so.1"], ["GLIBC_2.17"])
        self.assertTrue(vi["saw_def_section"])
        self.assertTrue(vi["saw_need_section"])

    def test_parent_continuation_lines_ignored(self):
        sample = ("Version definition section '.gnu.version_d' contains 2 entries:\n"
                  "  000000: Rev: 1  Flags: BASE  Index: 1  Cnt: 1  Name: liba.so\n"
                  "  0x001c: Rev: 1  Flags: none  Index: 2  Cnt: 2  Name: VER_1.0\n"
                  "  0x0038: Parent 1: VER_1.0\n")
        vi = parse_version_info(sample.splitlines())
        self.assertEqual(vi["definitions"], ["liba.so", "VER_1.0"])

    def test_empty_output_has_no_sections(self):
        vi = parse_version_info([])
        self.assertFalse(vi["saw_any_section"])
        self.assertEqual(vi["definitions"], [])
        self.assertEqual(vi["needs"], {})


class TestElfSymbolVersionFailClosed(unittest.TestCase):
    def test_valid_inspect_records_needs_and_definitions(self):
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(str(path)))):
                info = inspect_elf(path)
            self.assertEqual(info["machine"], "AArch64")
            self.assertEqual(info["soname"], "libfoo.so")
            self.assertIn("GLIBC_2.34", info["version_needs"]["libc.so.6"])
            self.assertIn("LIBJPEG_6.2", info["defined_versions"])
        finally:
            path.unlink()

    def test_malformed_empty_version_output_with_needed_fails(self):
        # A "successful" readelf that yields NO version sections for an ELF with
        # DT_NEEDED must FAIL (fail-closed), not pass as empty-needed/maxnone.
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(str(path), vinfo=""))):
                with self.assertRaises(RuntimeError):
                    inspect_elf(path)
        finally:
            path.unlink()

    def test_malformed_empty_dynamic_rejected(self):
        # Malformed `-d` tool output with no recognized dynamic-section marker must
        # FAIL rather than silently yield an empty needed set.
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(
                                       str(path), dyn="garbage, no dynamic marker\n"))):
                with self.assertRaises(RuntimeError):
                    inspect_elf(path)
        finally:
            path.unlink()

    def test_malformed_needed_unparseable_payload_rejected(self):
        # A NEEDED tag with an unparseable (missing) bracketed payload must FAIL.
        dyn = ("Dynamic section at offset 0x100 contains 2 entries:\n"
               "  Tag        Type                         Name/Value\n"
               " 0x0001 (NEEDED)  Shared library: libc.so.6\n"
               " 0x000e (SONAME)  Library soname: [libfoo.so]\n")
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(str(path), dyn=dyn))):
                with self.assertRaises(RuntimeError):
                    inspect_elf(path)
        finally:
            path.unlink()

    def test_header_only_def_section_rejected(self):
        # A definition-section header that declares entries but has NO actual rows
        # must be rejected (was previously accepted as saw_any_section=True).
        vinfo = "Version definition section '.gnu.version_d' contains 3 entries:\n"
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(str(path), vinfo=vinfo))):
                with self.assertRaises(RuntimeError):
                    inspect_elf(path)
        finally:
            path.unlink()

    def test_header_only_needs_section_rejected(self):
        # A needs-section header that declares entries but has NO per-file blocks /
        # rows must be rejected even when the ELF declares nonempty DT_NEEDED.
        vinfo = ("Version needs section '.gnu.version_r' contains 2 entries:\n"
                 " Addr: 0x000000000000c0cc\n")
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(b"\x7fELF\x02\x01\x01")
            path = Path(fh.name)
        try:
            with mock.patch.object(_verify_mod.subprocess, "run",
                                   _mocked_run(_out_for(str(path), vinfo=vinfo))):
                with self.assertRaises(RuntimeError):
                    inspect_elf(path)
        finally:
            path.unlink()


class TestVersionEdgeProof(unittest.TestCase):
    def test_missing_provider_version_fails(self):
        consumer = {
            "rel": "chromium/chromium", "path": "/s/chromium/chromium",
            "needed": ["libgcc_s.so.1"],
            "version_needs": {"libgcc_s.so.1": ["GCC_14.0"]},
        }
        proven = {"libgcc_s.so.1": {
            "proved": True, "role": "provider", "provider": "libgcc-s1",
            "version": "16-20260322-1ubuntu1",
            "defined_versions": ["GCC_3.0"],
        }}
        edges, failures = check_version_edges([consumer], proven)
        self.assertFalse(edges[0]["satisfied"])
        self.assertEqual(edges[0]["missing"], ["GCC_14.0"])
        self.assertTrue(any("GCC_14.0" in f for f in failures))

    def test_satisfied_edge_passes(self):
        consumer = {
            "rel": "chromium/lib/libjpeg.so.62.3.0", "path": "/s/x",
            "needed": ["libc.so.6"],
            "version_needs": {"libc.so.6": ["GLIBC_2.17"]},
        }
        proven = {"libc.so.6": {
            "proved": True, "role": "provider", "provider": "libc6",
            "version": "2.43-2ubuntu2",
            "defined_versions": ["GLIBC_2.17", "GLIBC_2.43"],
        }}
        edges, failures = check_version_edges([consumer], proven)
        self.assertTrue(edges[0]["satisfied"])
        self.assertEqual(failures, [])

    def test_private_staged_proof_from_real_elf(self):
        staged = {
            "path": "/stage/chromium/lib/libjpeg.so.62.3.0",
            "machine": "AArch64", "class": "ELF64",
            "needed": ["libc.so.6", "ld-linux-aarch64.so.1"],
            "version_needs": {"libc.so.6": ["GLIBC_2.17"]},
            "defined_versions": ["libjpeg.so.62", "LIBJPEG_6.2"],
            "max_glibc_symbol": "GLIBC_2.17",
        }
        rec = prove_private_staged("libjpeg.so.62", staged)
        self.assertTrue(rec["proved"])
        self.assertEqual(rec["role"], "private_staged")
        self.assertIn("LIBJPEG_6.2", rec["defined_versions"])


class TestBrowserProofGate(unittest.TestCase):
    def _write_minimal_elf(self, path, machine=183):
        import struct
        b = bytearray(64)
        b[0:4] = b"\x7fELF"
        b[4] = 2  # ELFCLASS64
        b[5] = 1  # little endian
        b[6] = 1  # EV_CURRENT
        b[16:18] = struct.pack("<H", 2)      # ET_EXEC
        b[18:20] = struct.pack("<H", machine) # EM_AARCH64=183
        b[20:24] = struct.pack("<I", 1)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(bytes(b) + b"\x00" * 192)

    def _mk_stage(self):
        root = Path(tempfile.mkdtemp(prefix="9to1-proof-"))
        self._write_minimal_elf(root / "chromium/chromium")
        self._write_minimal_elf(root / "chromium/lib/libjpeg.so.62.3.0")
        manifest = {"schema": "9to1.chromium-native.stage-manifest/v1",
                    "version": "155.0.8059.79-1~deb13u1", "files": []}
        (root / "manifest.json").write_text(json.dumps(manifest))
        return root

    def _mk_proof(self, stage, resol):
        def elf_rec(rel):
            p = stage / rel
            return {"path": str(p), "rel": rel, "sha256": sha256_file(p),
                    "size": p.stat().st_size, "machine": "AArch64",
                    "class": "ELF64", "interpreter": "", "needed": [],
                    "version_needs": {}, "defined_versions": []}

        elfs = [elf_rec("chromium/chromium"),
                elf_rec("chromium/lib/libjpeg.so.62.3.0")]

        def provider(son, pkg, role="provider", **kw):
            rec = {"proved": True, "role": role, "provider": pkg,
                   "version": "2.43-2ubuntu2", "architecture": "arm64",
                   "sha256": "0" * 64, "filename": "pool/main/x/x.deb",
                   "path_in_pkg": "usr/lib/x.so.1", "soname": son,
                   "defined_versions": [], "needed": []}
            rec.update(kw)
            return rec

        provs = {
            "libc.so.6": provider("libc.so.6", "libc6"),
            "libgcc_s.so.1": provider("libgcc_s.so.1", "libgcc-s1"),
            "libz.so.1": provider("libz.so.1", "zlib1g"),
            "libnss3.so": provider("libnss3.so", "libnss3"),
            "libstdc++.so.6": provider("libstdc++.so.6", "libstdc++6"),
            "ld-linux-aarch64.so.1": provider(
                "ld-linux-aarch64.so.1", "libc6", role="loader",
                interpreter_path="/lib/ld-linux-aarch64.so.1",
                path_in_pkg="usr/lib/aarch64-linux-gnu/ld-linux-aarch64.so.1"),
            "libjpeg.so.62": provider(
                "libjpeg.so.62", "stage", role="private_staged", version=None,
                sha256=None, filename=None,
                path_in_pkg="chromium/lib/libjpeg.so.62.3.0"),
        }
        return {
            "schema": BROWSER_PROOF_SCHEMA,
            "verdict": "PASS",
            "acceptance": "NOT RUN",
            "manifest": {"manifest_sha256": sha256_file(stage / "manifest.json")},
            "resolution_sha256": sha256_file(resol),
            "providers": {"proved_count": len(provs), "blocked": {},
                          "proved": provs},
            "version_edges": [],
            "version_edge_count": 0, "version_edge_satisfied": 0,
            "elfs": elfs,
        }

    def _write_proof(self, proof):
        fh = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False)
        json.dump(proof, fh)
        fh.close()
        return Path(fh.name)

    def _assert_rejected(self, proof):
        stage = self._mk_stage()
        resol = HERE / "chromium-soname-resolution.json"
        pp = self._write_proof(proof)
        try:
            with self.assertRaises(SystemExit):
                _validate_browser_proof_layer1(pp, stage, resol)
        finally:
            pp.unlink()

    def test_valid_proof_passes(self):
        stage = self._mk_stage()
        resol = HERE / "chromium-soname-resolution.json"
        proof = self._mk_proof(stage, resol)
        pp = self._write_proof(proof)
        try:
            got = _validate_browser_proof_layer1(pp, stage, resol)
            self.assertEqual(got["verdict"], "PASS")
        finally:
            pp.unlink()

    def test_public_gate_requires_auth_inputs(self):
        # The COMPLETE public gate must require the authenticated inputs; a Layer-1-only
        # call is not a complete PASS and must be refused when auth inputs are absent.
        stage = self._mk_stage()
        resol = HERE / "chromium-soname-resolution.json"
        proof = self._mk_proof(stage, resol)
        pp = self._write_proof(proof)
        try:
            with self.assertRaises(SystemExit):
                validate_browser_proof(pp, stage, resol, None, None, None)
        finally:
            pp.unlink()

    def test_not_pass_fails(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["verdict"] = "FAIL"
        self._assert_rejected(proof)

    def test_manifest_sha_mismatch_fails(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["manifest"]["manifest_sha256"] = "0" * 64
        self._assert_rejected(proof)

    def test_blocked_provider_fails(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["providers"]["blocked"] = {"libX11.so.6": "no provider"}
        self._assert_rejected(proof)

    def test_fabricated_counterexample_rejected(self):
        # Mirrors the review-3 fabricated counterexample: no elfs, name-only provider
        # strings, arbitrary satisfied=True edges, no loader/private/auth. Must FAIL.
        stage = self._mk_stage()
        resol = HERE / "chromium-soname-resolution.json"
        proof = {
            "schema": BROWSER_PROOF_SCHEMA,
            "verdict": "PASS",
            "acceptance": "NOT RUN",
            "manifest": {"manifest_sha256": sha256_file(stage / "manifest.json")},
            "resolution_sha256": sha256_file(resol),
            "providers": {"proved_count": 1, "blocked": {},
                          "proved": {"0": {"provider": "libc6"},
                                     "1": {"provider": "libgcc-s1"},
                                     "2": {"provider": "zlib1g"},
                                     "3": {"provider": "libnss3"},
                                     "4": {"provider": "libstdc++6"}}},
            "version_edges": [
                {"consumer_rel": "chromium/chrome-sandbox", "soname": "libc.so.6",
                 "required": [], "satisfied": True},
            ],
            "version_edge_count": 1, "version_edge_satisfied": 1,
        }
        self._assert_rejected(proof)

    def test_loader_omitted_rejected(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        del proof["providers"]["proved"]["ld-linux-aarch64.so.1"]
        self._assert_rejected(proof)

    def test_private_staged_omitted_rejected(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        del proof["providers"]["proved"]["libjpeg.so.62"]
        self._assert_rejected(proof)

    def test_wrong_machine_rejected(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["elfs"][0]["machine"] = "X86_64"
        self._assert_rejected(proof)

    def test_provider_missing_identity_field_rejected(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        del proof["providers"]["proved"]["libc.so.6"]["sha256"]
        self._assert_rejected(proof)

    def test_forged_floor_version_needs_rejected(self):
        # A proof claiming an inflated libc requirement not present in the actual ELF
        # must be rejected (forged floor), not accepted.
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["elfs"][0]["version_needs"] = {"libc.so.6": ["GLIBC_2.39"]}
        proof["version_edges"] = [
            {"consumer_rel": "chromium/chromium", "soname": "libc.so.6",
             "required": ["GLIBC_2.39"], "satisfied": True},
        ]
        self._assert_rejected(proof)

    def test_orphan_arbitrary_edge_rejected(self):
        stage = self._mk_stage()
        proof = self._mk_proof(stage, HERE / "chromium-soname-resolution.json")
        proof["version_edges"] = [
            {"consumer_rel": "chromium/chromium", "soname": "libc.so.6",
             "required": ["GLIBC_2.17"], "satisfied": True},
        ]
        proof["version_edge_count"] = 1
        self._assert_rejected(proof)


class TestDerivedFloors(unittest.TestCase):
    def test_derive_libc_floor_from_staged_elfs(self):
        proof = {"elfs": [
            {"version_needs": {"libc.so.6": ["GLIBC_2.17", "GLIBC_2.39"]}},
            {"version_needs": {"libc.so.6": ["GLIBC_2.34"]}},
        ]}
        self.assertEqual(derive_libc_floor(proof), (2, 39))

    def test_derive_browser_deps_from_provider_set(self):
        resol = json.loads((HERE / "chromium-soname-resolution.json").read_text())
        proof = {
            "elfs": [
                {"needed": ["libnss3.so", "libjpeg.so.62", "ld-linux-aarch64.so.1"]},
            ],
            "providers": {"proved": {
                "libnss3.so": {"role": "provider", "provider": "libnss3",
                               "version": "2:3.120-1ubuntu2"},
                "libjpeg.so.62": {"role": "private_staged", "provider": "stage"},
                "ld-linux-aarch64.so.1": {"role": "loader", "provider": "libc6"},
            }},
        }
        deps = derive_browser_deps(proof, resol)
        self.assertIn("libnss3 (>= 2:3.120-1ubuntu2)", deps)
        # loader and private staged must not appear as package deps
        self.assertFalse(any("libc6" in d for d in deps))
        self.assertFalse(any("libjpeg" in d for d in deps))


if __name__ == "__main__":
    unittest.main()
