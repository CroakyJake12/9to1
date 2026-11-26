"""Regression tests for configurable pooled slots and RAM placement."""
import unittest
from dataclasses import replace
import pooled_backend as b
import test_pooled_backend as backend_tests
import test_pooled_engine as engine_tests
from engine_profiles import atomic_json, INKLING_POOL

class IndependenceConfigTests(unittest.TestCase):
    def fixture(self, kind):
        case = kind()
        self.addCleanup(case.doCleanups)
        case.setUp()
        return case

    def test_multi_slot_and_static_ram_offload(self):
        f = self.fixture(backend_tests.BackendTests)
        proof = f.proof()
        proof = replace(proof, flags=proof.flags | {"--cache-type-k", "--cache-type-v", "--flash-attn", "--n-cpu-moe"})
        plan = f.plan(proof=proof, context=16384, parallel=4, kv_type="q8_0", cpu_moe_layers=32)
        for flag, value in {"--ctx-size":"65536", "--parallel":"4", "--cache-type-k":"q8_0", "--cache-type-v":"q8_0", "--n-cpu-moe":"32"}.items():
            self.assertEqual(plan.argv[plan.argv.index(flag)+1], value)
        self.assertEqual(plan.display_reserve_policy_bytes, 1400000000)

    def test_unsupported_and_over_budget_refused(self):
        f = self.fixture(backend_tests.BackendTests)
        for changes in ({"parallel":65}, {"parallel":True}, {"parallel":8,"context":262144}, {"kv_type":"q8_0"}, {"cpu_moe_layers":32}, {"cpu_moe_layers":49}):
            with self.subTest(changes=changes), self.assertRaises(b.BackendError):
                f.plan(**changes)

    def test_config_advertises_requested_slots(self):
        f = self.fixture(engine_tests.PoolIntegrationTests)
        atomic_json(f.manager.root / "inkling-pool-source.json", {**f.manifest, "context":16384, "parallel":4, "kv_type":"q8_0", "cpu_moe_layers":32})
        s = f.manager._spec(INKLING_POOL)
        self.assertEqual((s["context"], s["slots"], s["max_context"]), (16384, 4, 262144))
