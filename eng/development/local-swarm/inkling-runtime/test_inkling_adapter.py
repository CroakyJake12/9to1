import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import engine_profiles as p
from engines import EngineManager


class InklingAdapterTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.model = self.root / "Inkling-2-Mini-UD-Q4_K_XL.gguf"
        self.model.write_bytes(b"GGUFfixture")
        self.tokenizer = self.root / "tokenizer"
        self.tokenizer.mkdir()
        self.source = self.root / "source.json"
        self.cfg = dict(arch="inkling2_loop", exe="component:strata-inkling", gpu=0,
                        backend=None, host="127.0.0.1", parallel=1,
                        tokenizer=str(self.tokenizer),
                        args=["--native", str(self.model), "--max-context", "32768",
                              "--vram-experts-gb", "6.68", "--ram-experts-gb", "8.04"])
        self.write()
        p.atomic_json(self.root / "inkling-source.json", {"profile": str(self.source)})
        self.manager = EngineManager.__new__(EngineManager)
        self.manager.root = self.root
        self.manager.tag = "test"

    def write(self):
        p.atomic_json(self.source, self.cfg)

    def test_actual_native_profile_and_identity(self):
        self.assertEqual(p.find_inkling_profile(self.root)[1]["args"], self.cfg["args"])
        spec = self.manager._spec(p.INKLING)
        self.assertEqual((spec["id"], spec["gpu"], spec["context"], spec["slots"], spec["port"]),
                         (p.INKLING, p.GPU_RTX, 32768, 1, 19042))

    def test_wrong_engine_and_arch_are_refused(self):
        for key, value in (("arch", "qwen"), ("exe", "component:strata-engine")):
            original = self.cfg[key]
            self.cfg[key] = value
            self.write()
            with self.assertRaises(p.EngineError):
                p.find_inkling_profile(self.root)
            self.cfg[key] = original

    def test_wrong_gpu_and_backend_are_refused(self):
        for key, value in (("gpu", 1), ("backend", "metal"), ("host", "0.0.0.0")):
            original = self.cfg[key]
            self.cfg[key] = value
            self.write()
            with self.assertRaises(p.EngineError):
                p.find_inkling_profile(self.root)
            self.cfg[key] = original

    def test_source_symlink_is_refused(self):
        alias = self.root / "alias.json"
        alias.symlink_to(self.source)
        p.atomic_json(self.root / "inkling-source.json", {"profile": str(alias)})
        with self.assertRaises(p.EngineError):
            p.find_inkling_profile(self.root)

    def test_fixed_settings_and_separate_unit(self):
        spec = self.manager._spec(p.INKLING)
        self.assertEqual(self.manager._settings(spec, {}), (32768, 4))
        for settings in ({"context": 65536}, {"gpu": "CPU"}, {"threads": 8}):
            with self.assertRaises(p.EngineError):
                self.manager._settings(spec, settings)
        self.assertEqual(self.manager._name(p.INKLING), "dulche-inkling-test.service")
        self.assertNotEqual(self.manager._name(p.INKLING), self.manager._name(p.PRIMARY))

    def test_owned_profile_preserves_native_placement_and_auth(self):
        home = self.root / "strata"
        control = home / "components/control/test"
        (control / "python/bin").mkdir(parents=True)
        (control / "python/bin/python3.12").write_text("")
        (control / "serve").mkdir()
        (control / "serve/inkling_kind.py").write_text("")
        p.atomic_json(control.parent / "active.json", {"folder": "test"})
        with patch.object(p, "strata_home", return_value=home):
            source, owned, _, _, runtime = p.prepare_inkling(self.root)
        self.assertEqual(owned.name, "inkling-owned.json")
        self.assertEqual(runtime["model_name"], p.INKLING)
        self.assertEqual(runtime["exe"], "component:strata-inkling")
        self.assertEqual(runtime["backend"], "cuda")
        self.assertEqual(runtime["args"], self.cfg["args"])
        self.assertGreaterEqual(len(runtime["api_key"]), 32)
        self.assertEqual(owned.stat().st_mode & 0o777, 0o600)
        self.assertEqual(json.loads(source.read_text()), self.cfg)

    def test_native_tokenization_route_and_margin(self):
        class Response:
            status = 200
            def read(self, size):
                return b'{"input_tokens":128}'
        requests = []
        class Connection:
            def __init__(self, *args, **kwargs): pass
            def request(self, *args): requests.append(args)
            def getresponse(self): return Response()
            def close(self): pass
        with patch.object(self.manager, "_observe", return_value=({"state": "loaded"}, (19042, "private"), {})), patch("engines.http.client.HTTPConnection", Connection):
            counted = self.manager.count_prompt(p.INKLING, [{"role": "user", "content": "hi"}], {"reasoning_effort": "none"})
        self.assertEqual(counted["input_tokens"], 160)
        self.assertEqual(requests[0][1], "/v1/messages/count_tokens")
        self.assertEqual(json.loads(requests[0][2])["model"], p.INKLING)
        self.assertEqual(requests[0][3]["Authorization"], "Bearer private")


if __name__ == "__main__":
    unittest.main()
