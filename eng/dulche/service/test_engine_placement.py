import unittest
from unittest import mock

import engine_placement as ep
from engine_profiles import GPU_RTX, GPU_V100, PRIMARY, V100


class PlacementTests(unittest.TestCase):
    def setUp(self):
        ep.CACHE.clear()

    def test_native_snapshot_requires_exact_identity_and_usable_live_counters(self):
        req = ep.configured_placement({"id": PRIMARY, "gpu": GPU_RTX})
        valid = {
            "engine": {
                "model": PRIMARY,
                "arena_mib": 56762,
                "expert_cache_mib": 5570,
                "expert_slots": 2417,
                "kv_resident": 32768,
                "drafting": "dflash2",
            }
        }
        observed = ep.native_observation(req, PRIMARY, valid)
        self.assertEqual(observed["host_arena_mib"], 56762)
        self.assertNotIn("cpu_weight_buffer_mib", observed)
        self.assertEqual(observed["expert_cache_mib"], 5570)
        self.assertEqual(observed["evidence"], "observed")
        self.assertIsNone(
            ep.native_observation(
                req, PRIMARY, {"engine": {**valid["engine"], "model": "other"}}
            )
        )
        self.assertIsNone(
            ep.native_observation(
                req, PRIMARY, {"engine": {**valid["engine"], "arena_mib": None}}
            )
        )

    def test_unloaded_never_reuses_a_native_observation(self):
        spec = {"id": PRIMARY, "gpu": GPU_RTX}
        fn = mock.Mock(
            return_value={
                "engine": {
                    "model": PRIMARY,
                    "arena_mib": 10,
                    "expert_cache_mib": 2,
                    "expert_slots": 3,
                }
            }
        )
        common = (
            spec,
            {"state": "loaded", "settings": {"gpu": GPU_RTX}},
            {"MainPID": "90"},
            {},
            (18500, "private", "dulche", 90),
            fn,
        )
        a = ep.model_placements(*common)
        self.assertEqual(a["placement"]["kind"], "native_strata_expert_cache")
        b = ep.model_placements(
            spec, {"state": "suspended", "settings": {"gpu": GPU_RTX}}, {}, {}, None, fn
        )
        self.assertIsNone(b["placement_observation"])
        self.assertEqual(b["placement"]["evidence"], "configured")
        fn.assert_called_once()

    def test_gguf_plan_has_estimates_only_until_measured(self):
        spec = {"id": "local-123", "bytes": 123456, "gpu": "CPU"}
        settings = {
            "gpu": GPU_V100,
            "gpu_layers": 14,
            "vram_ceiling_mib": 4096,
            "context": 32768,
        }
        projected = ep.configured_placement(
            spec,
            settings,
            {
                "estimated_cpu_weight_mib": 11000,
                "weight_upper_mib": 2700,
                "estimated_vram_mib": 3900,
                "total_layer_groups": 65,
            },
        )
        self.assertEqual(projected["kind"], "partial_gpu_layers")
        self.assertEqual(projected["evidence"], "configured")
        self.assertEqual(projected["estimated_gpu_weight_mib"], 2700)
        self.assertNotIn("observed_model_vram_mib", projected)

    def test_invocation_filter_buffer_types_and_duplicate_logs(self):
        current = "abcde123" * 4
        old = "deadbeef" * 4

        def row(inv, t, msg):
            return {
                "_SYSTEMD_INVOCATION_ID": inv,
                "__REALTIME_TIMESTAMP": str(t),
                "MESSAGE": msg,
            }

        lines = [
            row(old, 100, "load_tensors: CPU_Mapped model buffer size = 2000 MiB"),
            row(current, 50, "load_tensors: CPU_Mapped model buffer size = 400.00 MiB"),
            row(
                current,
                60,
                "0.08.275.006 I load_tensors:   CPU_Mapped model buffer size = 521.00 MiB",
            ),
            row(
                current,
                70,
                "load_tensors: RPC0[127.0.0.1:19110] model buffer size = 12821.78 MiB",
            ),
            row(
                current,
                80,
                "llama_kv_cache: RPC0[127.0.0.1:19110] KV buffer size = 1152.0 MiB",
            ),
            row(current, 90, "load_tensors: offloaded 66/66 layers to GPU"),
            row(
                current,
                100,
                "http request text includes load_tensors: CPU_Mapped model buffer size = 999999 MiB",
            ),
        ]
        o = ep.journal_buffers(lines, current)
        self.assertEqual(o["cpu_weight_buffer_mib"], 521)
        self.assertEqual(o["gpu_weight_buffer_mib"], 12821.78)
        self.assertEqual(o["gpu_kv_buffer_mib"], 1152)
        self.assertEqual(o["offloaded_layers"], 66)
        self.assertEqual(o["total_offload_layers"], 66)
        self.assertEqual(ep.journal_buffers(lines, "wrong"), {})

    def test_observed_vram_is_not_weight_memory(self):
        s = {"id": "local-2", "gpu": GPU_V100}
        a = {
            "state": "loaded",
            "settings": {"gpu": GPU_V100, "gpu_layers": 10, "vram_ceiling_mib": 3000},
            "observed_model_vram_mib": 2200,
        }
        r = ep.model_placements(s, a, {}, {}, (1, None, "dulche", 2), mock.Mock())
        o = r["placement_observation"]
        self.assertEqual(o["observed_model_vram_mib"], 2200)
        self.assertNotIn("gpu_weight_buffer_mib", o)
        self.assertNotIn("host_arena_mib", o)

    def test_display_name_cannot_grant_a_native_engine(self):
        self.assertEqual(
            ep.configured_placement(
                {"id": "foreign", "name": "Qwen3.8-Flash-Next Strata", "gpu": GPU_RTX}
            )["kind"],
            "unknown",
        )
        self.assertEqual(
            ep.configured_placement({"id": V100, "gpu": GPU_V100})["kind"],
            "gpu_layer_offload",
        )


if __name__ == "__main__":
    unittest.main()
