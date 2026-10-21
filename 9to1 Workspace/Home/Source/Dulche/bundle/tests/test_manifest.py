from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.identity import IdentityError  # noqa: E402
from bundle.licence import validate_license, validate_redistribution  # noqa: E402
from bundle.manifest import parse_manifest  # noqa: E402
from bundle.spec import parse_spec  # noqa: E402
from bundle.tests.helpers import minimal_spec  # noqa: E402


class ManifestAndLicenceTests(unittest.TestCase):
    def test_licence_validation(self) -> None:
        self.assertEqual("MIT", validate_license("MIT"))
        self.assertEqual("Apache-2.0", validate_license("Apache-2.0"))
        with self.assertRaises(IdentityError):
            validate_license("")
        with self.assertRaises(IdentityError):
            validate_license("not a license id!")

    def test_redistribution_policy_validation(self) -> None:
        self.assertEqual("review-required", validate_redistribution("review-required"))
        with self.assertRaises(IdentityError):
            validate_redistribution("whatever")

    def test_parse_manifest_rejects_invalid_state_unless_allowed(self) -> None:
        spec = parse_spec(minimal_spec())
        # Build an invalid manifest shape (state tampered).
        raw = {
            "manifestSchemaVersion": 1,
            "bundleId": "b",
            "bundleVersion": "1",
            "purpose": "bundled-dulche-runtime-model-assets",
            "host": {"os": "linux", "arch": "x86_64"},
            "state": "invalid",
            "assets": [],
            "missingAssets": [],
        }
        with self.assertRaises(IdentityError):
            parse_manifest(raw)
        # allow_invalid lets callers inspect but never treats it as admitted.
        parsed = parse_manifest(raw, allow_invalid=True)
        self.assertEqual("invalid", parsed["state"])

    def test_parse_manifest_rejects_wrong_schema_version(self) -> None:
        raw = {"manifestSchemaVersion": 99}
        with self.assertRaises(IdentityError):
            parse_manifest(raw, allow_invalid=True)


if __name__ == "__main__":
    unittest.main()
