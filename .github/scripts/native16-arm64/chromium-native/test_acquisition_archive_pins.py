"""Immutable acquisition origins, byte pins and authentic signed-date controls.

Uses a real disposable GPG key and local file transport. No browser, package,
model, installer, SDK or native-success substitutes are involved.
"""
from __future__ import annotations

import copy
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "acquisition_archive_pins", HERE / "acquire-chromium-debian.py")
acquire = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(acquire)
UTC = datetime.timezone.utc
NOW = datetime.datetime(2026, 10, 24, 12, tzinfo=UTC)


class ArchiveOriginTests(unittest.TestCase):
    def setUp(self):
        self.pinned = json.loads((HERE / "chromium-pinned-manifest.json").read_text())

    def test_manifest_preserves_exact_version_payloads_and_official_snapshots(self):
        origins = acquire.archive_origins(self.pinned)
        self.assertEqual(set(origins), {"debian", "security"})
        self.assertEqual(self.pinned["selected_version"]["binary_package_version"],
                         "155.0.8059.79-1~deb13u1")
        self.assertEqual({p["debian_package"] for p in self.pinned["packages"]},
                         {"chromium", "chromium-common", "chromium-sandbox",
                          "libjpeg62-turbo", "libflac14", "libopenh264-8"})
        self.assertEqual(origins["security"]["inrelease_sha256"],
                         "4ac49185ca56f0a08333aec9d484e56a8d73a0d64b002f494a83a7bdfb7f4e4a")

    def test_rolling_untrusted_mutable_or_malformed_origins_refused(self):
        invalid = [
            "https://security.debian.org/debian-security",
            "http://snapshot.debian.org/archive/debian-security/20261021T000000Z",
            "https://snapshot.debian.org.evil.invalid/archive/debian-security/20261021T000000Z",
            "https://user@snapshot.debian.org/archive/debian-security/20261021T000000Z",
            "https://snapshot.debian.org/archive/debian/20261021T000000Z",
            "https://snapshot.debian.org/archive/debian-security/20261021T000000Z/",
            "https://snapshot.debian.org/archive/debian-security/20261021T000000Z?latest=1",
            "https://snapshot.debian.org/archive/debian-security/20261399T000000Z",
            "https://snapshot.debian.org/archive/debian-security/29990101T000000Z",
        ]
        for base in invalid:
            with self.subTest(base=base):
                pinned = copy.deepcopy(self.pinned)
                pinned["archive_snapshots"]["security"]["base_url"] = base
                with self.assertRaises(RuntimeError):
                    acquire.archive_origins(pinned)

    def test_missing_or_invalid_byte_identity_refused_before_acquisition(self):
        cases = [("inrelease_sha256", None), ("inrelease_sha256", "A" * 64),
                 ("inrelease_sha256", "0" * 63), ("inrelease_bytes", True),
                 ("inrelease_bytes", 0), ("inrelease_bytes", "43409")]
        for key, value in cases:
            with self.subTest(key=key, value=value):
                pinned = copy.deepcopy(self.pinned)
                pinned["archive_snapshots"]["security"][key] = value
                with self.assertRaises(RuntimeError):
                    acquire.archive_origins(pinned)
        del self.pinned["archive_snapshots"]
        with self.assertRaises(RuntimeError):
            acquire.archive_origins(self.pinned)


class SignedWindowTests(unittest.TestCase):
    def text(self, date="Tue, 20 Oct 2026 21:32:29 UTC",
             expiry="Tue, 27 Oct 2026 21:32:29 UTC"):
        return f"Date: {date}\n" + (f"Valid-Until: {expiry}\n" if expiry is not None else "")

    def test_security_accepts_unexpired_and_refuses_at_exact_expiry(self):
        result = acquire.validate_release_window(self.text(), "trixie-security", NOW)
        self.assertEqual(result["valid_until"], "2026-10-27T21:32:29+00:00")
        boundary = datetime.datetime(2026, 10, 27, 21, 32, 29, tzinfo=UTC)
        for now in (boundary, boundary + datetime.timedelta(seconds=1)):
            with self.subTest(now=now), self.assertRaisesRegex(RuntimeError, "expired"):
                acquire.validate_release_window(self.text(), "trixie-security", now)

    def test_security_requires_valid_until_and_main_checks_when_present(self):
        with self.assertRaisesRegex(RuntimeError, "Valid-Until"):
            acquire.validate_release_window(self.text(expiry=None), "trixie-security", NOW)
        main = acquire.validate_release_window(self.text(expiry=None), "trixie", NOW)
        self.assertIsNone(main["valid_until"])
        with self.assertRaisesRegex(RuntimeError, "expired"):
            acquire.validate_release_window(
                self.text(expiry="Wed, 21 Oct 2026 21:32:29 UTC"), "trixie", NOW)

    def test_missing_duplicate_malformed_future_or_inverted_dates_refused(self):
        cases = ["Valid-Until: Tue, 27 Oct 2026 21:32:29 UTC\n",
                 self.text() + "Date: Tue, 20 Oct 2026 21:32:29 UTC\n",
                 self.text() + "Valid-Until: Tue, 27 Oct 2026 21:32:29 UTC\n",
                 self.text(date="invalid"), self.text(expiry="invalid"),
                 self.text(date="Sun, 25 Oct 2026 21:32:29 UTC"),
                 self.text(date="Tue, 20 Oct 2026 21:32:29"),
                 self.text(expiry="Tue, 20 Oct 2026 21:32:29 UTC")]
        for text in cases:
            with self.subTest(text=text), self.assertRaises(RuntimeError):
                acquire.validate_release_window(text, "trixie-security", NOW)


class RealSignedAcquisitionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(
            prefix="a-", dir=os.environ.get("ARM_ACQUISITION_TEST_TMP"))
        cls.root = Path(cls.temp.name)
        cls.home = cls.root / "g"
        cls.home.mkdir(mode=0o700)
        cls.previous_home = os.environ.get("GNUPGHOME")
        os.environ["GNUPGHOME"] = str(cls.home)
        cls.addClassCleanup(cls.cleanup)
        generated = subprocess.run(["gpg", "--batch", "--pinentry-mode", "loopback", "--passphrase", "",
                        "--quick-generate-key", "9to1 signed acquisition fixture", "ed25519", "sign", "0"],
                       capture_output=True, text=True)
        if generated.returncode:
            raise RuntimeError(f"GPG fixture generation failed: {generated.stderr}")
        listing = subprocess.run(["gpg", "--batch", "--with-colons", "--list-keys"],
                                 check=True, capture_output=True, text=True).stdout
        cls.fingerprint = next(line.split(":")[9] for line in listing.splitlines()
                               if line.startswith("fpr:"))
        cls.keyring = cls.root / "fixture-keyring.gpg"
        cls.keyring.write_bytes(subprocess.run(["gpg", "--batch", "--export"],
                                               check=True, capture_output=True).stdout)

    @classmethod
    def cleanup(cls):
        subprocess.run(["gpgconf", "--homedir", str(cls.home), "--kill", "gpg-agent"],
                       check=False, capture_output=True)
        if cls.previous_home is None:
            os.environ.pop("GNUPGHOME", None)
        else:
            os.environ["GNUPGHOME"] = cls.previous_home
        cls.temp.cleanup()

    def setUp(self):
        self.case = self.root / self._testMethodName
        self.origin = self.case / "origin"
        self.release = self.origin / "dists/trixie-security/InRelease"
        self.release.parent.mkdir(parents=True)
        self.evidence = self.case / "evidence"
        self.evidence.mkdir()

    def sign(self, expired=False, missing_expiry=False):
        now = datetime.datetime.now(UTC)
        date = now - datetime.timedelta(days=2)
        expiry = now + datetime.timedelta(days=-1 if expired else 1)
        text = f"Date: {date.strftime('%a, %d %b %Y %H:%M:%S +0000')}\n"
        if not missing_expiry:
            text += f"Valid-Until: {expiry.strftime('%a, %d %b %Y %H:%M:%S +0000')}\n"
        unsigned = self.case / "Release"
        unsigned.write_text(text)
        subprocess.run(["gpg", "--batch", "--yes", "--armor", "--clearsign",
                        "--output", str(self.release), str(unsigned)],
                       check=True, capture_output=True)
        data = self.release.read_bytes()
        return {"inrelease_sha256": hashlib.sha256(data).hexdigest(),
                "inrelease_bytes": len(data)}

    def fetch(self, pin, offline=False, fingerprint=None):
        return acquire.fetch_inrelease(self.origin.as_uri(), "trixie-security", self.evidence,
                                       self.keyring, fingerprint or self.fingerprint,
                                       offline, pin)

    def test_real_signed_download_and_exact_offline_cache_revalidate(self):
        pin = self.sign()
        first = self.fetch(pin)
        self.assertTrue(first["check"]["verified"])
        self.assertEqual(first["check"]["required_fpr"], self.fingerprint)
        self.release.unlink()  # Cache verification must not require the origin.
        second = self.fetch(pin, offline=True)
        self.assertTrue(second["check"]["verified"])
        self.assertIsNotNone(second["release_window"]["valid_until"])

    def test_wrong_hash_and_size_refuse_without_replacing_verified_cache(self):
        pin = self.sign()
        self.fetch(pin)
        cached = self.evidence / "InRelease.trixie-security"
        before = cached.read_bytes()
        for broken in ({**pin, "inrelease_sha256": "0" * 64},
                       {**pin, "inrelease_bytes": pin["inrelease_bytes"] + 1}):
            for offline in (False, True):
                with self.subTest(pin=broken, offline=offline), self.assertRaises(RuntimeError):
                    self.fetch(broken, offline=offline)
                self.assertEqual(cached.read_bytes(), before)

    def test_byte_valid_but_wrong_signer_still_refuses(self):
        pin = self.sign()
        with self.assertRaisesRegex(RuntimeError, "signature verification failed"):
            self.fetch(pin, fingerprint="0" * 40)

    def test_signed_expired_or_missing_security_expiry_still_refuses(self):
        pin = self.sign(expired=True)
        with self.assertRaisesRegex(RuntimeError, "expired"):
            self.fetch(pin)
        pin = self.sign(missing_expiry=True)
        with self.assertRaisesRegex(RuntimeError, "Valid-Until"):
            self.fetch(pin)


if __name__ == "__main__":
    unittest.main()
