import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from external import External
from state import Fault, State


class Context:
    def __init__(self, root):
        self.state = State(root)
        self.lock = self.state.lock
        self.cache = {"q": "loaded", "v": "suspended"}

    def model(self, mid):
        return {
            "id": mid,
            "name": mid,
            "engine": "Thunder",
            "context": 32768,
            "state": self.cache[mid],
        }


class ExternalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.ctx = Context(self.root / "state")
        self.service = External(self.ctx)

    def tearDown(self):
        self.ctx.state.db.close()
        self.temp.cleanup()

    def test_revocation_and_publication_state(self):
        r = self.service.issue("Test")
        auth = "Bearer " + r["token"]
        with self.assertRaises(Fault):
            self.service.authorize(auth)
        self.ctx.state.set("external_enabled", True)
        self.assertEqual(r["id"], self.service.authorize(auth))
        self.assertNotIn(
            r["token"],
            self.ctx.state.db.execute("select digest from external_clients").fetchone()[
                0
            ],
        )
        with self.assertRaises(Fault):
            self.service.authorize(auth + "a")
        self.service.revoke(r["id"])
        with self.assertRaises(Fault):
            self.service.authorize(auth)

    def test_models_verify_visibility(self):
        self.assertEqual(
            ["q"], [r["id"] for r in self.service.models(loaded_only=True)]
        )

    def test_install_is_optional_idempotent_and_does_not_expose_secret(self):
        conf = self.root / "config" / "opencode"
        conf.mkdir(parents=True)
        path = conf / "opencode.json"
        path.write_text(
            json.dumps(
                {"model": "existing/test", "providers": {"existing": {"name": "Other"}}}
            )
        )
        self.ctx.state.set("external_enabled", True)
        with mock.patch.dict(os.environ, {"XDG_CONFIG_HOME": str(conf.parent)}):
            first = self.service.connect_opencode("My OpenCode")
            second = self.service.connect_opencode("My OpenCode")
            self.assertEqual(first["client_id"], second["client_id"])
            doc = json.loads(path.read_text())
            self.assertEqual("existing/test", doc["model"])
            self.assertEqual({"name": "Other"}, doc["providers"]["existing"])
            ref = doc["providers"]["dulche"]["settings"]["apiKey"]
            self.assertTrue(ref.startswith("{file:"))
            key = Path(ref[6:-1])
            self.assertEqual(0o600, key.stat().st_mode & 0o777)
            self.assertNotIn(key.read_text(), path.read_text())
            self.assertEqual(
                first["client_id"], self.service.authorize("Bearer " + key.read_text())
            )
            doc["providers"]["dulche"]["name"] = "Changed by user"
            path.write_text(json.dumps(doc))
            with self.assertRaises(Fault):
                self.service.connect_opencode("My OpenCode")
