import unittest
from types import SimpleNamespace

from swarm_placement import configured, participant_placement


class PlacementTests(unittest.TestCase):
    def test_same_native_flash_placement_is_available_to_worker_or_mentor(self):
        row = {
            "id": "qwen3.8-flash-next-unsloth-ud-iq4_xs",
            "engine": "Thunder / Strata",
            "gpu": "RTX 5080",
            "state": "loaded",
            "active_profile_id": "current",
            "placement_observation": {
                "model_id": "qwen3.8-flash-next-unsloth-ud-iq4_xs",
                "kind": "native_strata_expert_cache",
                "source": "authenticated native metrics",
                "sampled_at": 100,
                "host_arena_mib": 56762,
                "expert_cache_mib": 5570,
                "expert_slots": 2417,
                "kv_resident": 32768,
                "drafting": "dflash2",
            },
        }
        role = {
            "model": row["id"],
            "profile": "current",
            "engine": row["engine"],
            "gpu": row["gpu"],
            "context": 32768,
            "placement": configured(row, row),
        }
        ctx = SimpleNamespace(model=lambda mid: row)
        for name in ("mentor", "worker-A"):
            item = participant_placement(ctx, role, name)
            self.assertEqual("native_strata_expert_cache", item["configured"]["kind"])
            self.assertTrue(item["observed"]["profile_matches"])
            self.assertEqual(56762, item["observed"]["host_expert_arena_mib"])
            self.assertEqual(5570, item["observed"]["gpu_expert_cache_mib"])
            self.assertIsNone(item["observed"]["cpu_weight_buffer_mib"])
        row["state"] = "unloaded"
        self.assertIsNone(
            participant_placement(ctx, role, "worker-A")["observed"][
                "host_expert_arena_mib"
            ]
        )

    def test_requested_v100_worker_never_inherits_unrelated_live_cpu_measurement(self):
        row = {
            "id": "local-G",
            "engine": "Lightning / llama.cpp",
            "state": "unloaded",
            "gpu": "CPU",
            "profile_id": "current",
            "bytes": 8 * 2**30,
            "placement_requested": {
                "kind": "cpu",
                "gpu": "CPU",
                "estimated_cpu_weight_mib": 8192,
            },
        }
        settings = {
            "engine": "Lightning / llama.cpp RPC",
            "gpu": "Tesla V100",
            "gpu_layers": 4,
            "vram_ceiling_mib": 4096,
            "placement_requested": {
                "kind": "partial_gpu_layers",
                "gpu": "Tesla V100",
                "estimated_cpu_weight_mib": 7500,
                "estimated_gpu_weight_mib": 692,
            },
        }
        req = configured(row, settings)
        role = {
            "model": row["id"],
            "profile": "low",
            "engine": settings["engine"],
            "gpu": settings["gpu"],
            "placement": req,
        }
        ctx = SimpleNamespace(model=lambda _: row)
        item = participant_placement(ctx, role, "validator")
        self.assertEqual("partial_gpu_layers", req["kind"])
        self.assertEqual(7500, req["estimated_cpu_weight_mib"])
        self.assertIsNone(item["observed"]["model_vram_mib"])
        row.update(
            state="loaded",
            active_profile_id="low",
            engine=settings["engine"],
            gpu=settings["gpu"],
            placement_observation={
                "kind": "partial_gpu_layers",
                "source": "buffer log",
                "cpu_weight_buffer_mib": 7470,
                "gpu_weight_buffer_mib": 708,
                "offloaded_layers": 4,
                "total_offload_layers": 65,
                "observed_model_vram_mib": 2810,
            },
        )
        observed = participant_placement(ctx, role, "validator")["observed"]
        self.assertTrue(observed["profile_matches"])
        self.assertEqual(7470, observed["cpu_weight_buffer_mib"])
        self.assertEqual(2810, observed["model_vram_mib"])
        self.assertEqual(4, observed["offloaded_layers"])
