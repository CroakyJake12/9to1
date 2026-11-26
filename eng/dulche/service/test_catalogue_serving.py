"""Catalogue visibility must not promise a serving route that no engine observes."""
import tempfile
import unittest

from controller import Controller
from state import Fault
from test_service import Engines


class Catalog:
    def installed(self):
        return [self.get("local-external"), self.get("local-gguf")]

    def get(self, mid):
        if mid == "local-gguf":
            return {"id": mid, "name": "Installed GGUF", "format": "gguf", "backend": "llama.cpp"}
        if mid in ("local-external", "local-just-imported"):
            return {"id": mid, "name": "Catalogue-only native checkpoint", "backend": "external"}
        raise KeyError(mid)

    def close(self):
        pass


class ObservedEngines(Engines):
    def __init__(self):
        super().__init__()
        self.extra_available = False

    def models(self):
        rows = [*super().models(), {"id": "local-gguf", "state": "unloaded", "format": "gguf",
                                     "engine": "Lightning / llama.cpp", "gpu": "CPU"}]
        if self.extra_available:
            rows.append({"id": "local-external", "state": "unloaded", "engine": "Registered native", "gpu": "CPU"})
        return rows


class CatalogueServingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="dulche-catalogue-serving-")
        self.addCleanup(self.temp.cleanup)
        self.engines = ObservedEngines()
        self.ctx = Controller(self.temp.name, self.engines, Catalog(), interval=1000)
        self.addCleanup(self.ctx.state.db.close)
        self.addCleanup(self.ctx.close)

    def test_catalogue_only_native_entry_is_visible_but_cannot_auto_start(self):
        row = self.ctx.model("local-external")
        self.assertEqual("Catalogue-only native checkpoint", row["name"])
        self.assertFalse(row["serving_supported"])
        self.assertFalse(row["can_start"])
        with self.assertRaisesRegex(Fault, "no installed serving backend") as error:
            self.ctx.admit("local-external")
        self.assertEqual(422, error.exception.status)
        self.assertEqual(0, self.engines.load_count)

    def test_engine_observed_installed_gguf_remains_startable(self):
        self.assertTrue(self.ctx.model("local-gguf")["can_start"])
        self.assertEqual("unloaded", self.ctx.admit("local-gguf")["state"])

    def test_new_catalogue_entry_before_refresh_is_not_assumed_runnable(self):
        self.assertFalse(self.ctx.model("local-just-imported")["can_start"])
        with self.assertRaises(Fault):
            self.ctx.admit("local-just-imported")

    def test_later_registered_engine_replaces_catalogue_only_limitation(self):
        self.assertFalse(self.ctx.model("local-external")["can_start"])
        self.engines.extra_available = True
        self.ctx.refresh()
        self.assertTrue(self.ctx.model("local-external")["can_start"])
        self.ctx.admit("local-external")


if __name__ == "__main__":
    unittest.main()
