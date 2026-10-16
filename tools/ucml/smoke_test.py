"""Process-level tests for the actual compiled UCML file tool; synthetic data only."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import uuid

DLL = Path(__file__).parent / "bin/Release/net10.0/9to1-ucml.dll"


class UcmlToolTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.metadata = {
            "schemaVersion": 1, "id": str(uuid.uuid4()), "revision": 1,
            "title": "Synthetic local models preference", "ownerId": "test-owner",
            "scope": {"kind": "personal", "id": "test-owner"}, "category": "models",
            "classification": "private", "createdAt": "2026-01-01T00:00:00Z",
            "updatedAt": "2026-01-01T00:00:00Z", "expiresAt": None,
            "retention": "until-revoked",
            "consent": {"status": "explicit", "apps": ["9to1.assistants"],
                        "purposes": ["personalization"], "recipients": ["local-model"]},
            "sources": [{"id": "test-source", "title": "Synthetic fixture", "kind": "test",
                         "uri": "urn:9to1:test:source", "observedAt": "2026-01-01T00:00:00Z"}],
            "confidence": 1.0, "supersedesId": None,
        }

    def tearDown(self):
        self.temporary.cleanup()

    def run_tool(self, *args, success=True):
        result = subprocess.run(["dotnet", str(DLL), *map(str, args)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0 if success else 1, result.stderr)
        return result

    def write_record(self, name="fixture.ucml", body="Prefer local models.\n", **changes):
        metadata = dict(self.metadata, **changes)
        path = self.root / name
        path.write_text("---ucml\n" + json.dumps(metadata) + "\n---\n" + body)
        return path

    def test_real_pack_inspect_unpack_round_trip_and_private_mode(self):
        body = self.root / "body.md"
        body.write_text("# Source\n\nUnicode: café.\n\n---\nMarkdown delimiter is body data.\n")
        metadata = self.root / "metadata.json"
        metadata.write_text(json.dumps(self.metadata))
        packed, unpacked = self.root / "pack.ucml", self.root / "unpacked.md"
        self.run_tool("pack", body, "--metadata", metadata, "--output", packed)
        self.run_tool("validate", packed)
        inspected = json.loads(self.run_tool("inspect", packed).stdout)
        self.assertEqual(inspected["id"], self.metadata["id"])
        self.run_tool("unpack", packed, "--output", unpacked)
        self.assertEqual(body.read_bytes(), unpacked.read_bytes())
        if os.name != "nt":
            self.assertEqual(packed.stat().st_mode & 0o777, 0o600)
            self.assertEqual(unpacked.stat().st_mode & 0o777, 0o600)

    def test_refuses_overwrite_and_preserves_existing_bytes(self):
        record = self.write_record()
        output = self.root / "existing.md"
        output.write_bytes(b"Existing important content")
        self.run_tool("unpack", record, "--output", output, success=False)
        self.assertEqual(output.read_bytes(), b"Existing important content")

    def test_selector_omits_revoked_expired_and_remote_records(self):
        good = self.write_record()
        self.write_record("revoked.ucml", body="local models REVOKED_MARKER", consent={**self.metadata["consent"], "status": "revoked"})
        self.write_record("expired.ucml", body="local models EXPIRED_MARKER", expiresAt="2026-01-02T00:00:00Z")
        self.write_record("remote.ucml", body="local models REMOTE_MARKER", consent={**self.metadata["consent"], "recipients": ["remote-provider"]})
        output = self.root / "context.md"
        self.run_tool("select", self.root, "--query", "local models", "--output", output, "--max-chars", 1000)
        text = output.read_text()
        self.assertIn(self.metadata["id"], text)
        self.assertIn("Prefer local models", text)
        self.assertNotIn("_MARKER", text)
        self.assertLessEqual(len(text), 1000)

    def test_selector_retains_whole_record_or_refuses(self):
        self.write_record(body="models " * 1500 + "END_OF_RECORD")
        output = self.root / "context.md"
        self.run_tool("select", self.root, "--query", "models", "--output", output, "--max-chars", 1000, success=False)
        self.assertFalse(output.exists())

    def test_invalid_json_does_not_leak_body_to_logs(self):
        path = self.root / "bad.ucml"
        path.write_text('---ucml\n{"private": "PRIVATE_BODY_MARKER"}\n---\nPRIVATE_BODY_MARKER')
        result = self.run_tool("validate", path, success=False)
        self.assertNotIn("PRIVATE_BODY_MARKER", result.stdout + result.stderr)

    def test_duplicate_metadata_and_unknown_options_refused(self):
        path = self.write_record()
        path.write_text(path.read_text().replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'))
        self.run_tool("validate", path, success=False)
        self.run_tool("inspect", path, "--unsupported", "value", success=False)

    def test_invalid_utf8_refused(self):
        path = self.root / "bad.ucml"
        path.write_bytes(b"---ucml\n\xff\xfe\n---\ntext")
        self.run_tool("validate", path, success=False)


if __name__ == "__main__":
    unittest.main()
