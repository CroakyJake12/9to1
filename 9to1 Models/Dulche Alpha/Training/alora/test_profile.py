"""Software-only arithmetic fixtures for ALORA profiling and curriculum metadata."""

import copy
import hashlib
import json
import math
import unittest

from alora.curriculum import CATEGORY_WEIGHTS, target_counts, validate_counts
from alora.profile import ActivationProfiler


class CurriculumTests(unittest.TestCase):
    def test_owner_proportions_at_both_bounds(self):
        self.assertEqual(target_counts(5000), {
            "autonomous_recovery": 1250, "ui": 1250, "ux": 750,
            "proactive_reasoning": 750, "coding": 1000,
        })
        self.assertEqual(target_counts(10000),
                         {key: 2 * value for key, value in target_counts(5000).items()})

    def test_largest_remainders_are_exact_and_ties_are_stable(self):
        counts = target_counts(5001)
        self.assertEqual(sum(counts.values()), 5001)
        self.assertEqual(counts["autonomous_recovery"], 1251)
        self.assertEqual(counts["ui"], 1250)
        for total in (5003, 5099, 7333, 9999):
            counts = target_counts(total)
            self.assertEqual(sum(counts.values()), total)
            for category, weight in CATEGORY_WEIGHTS.items():
                self.assertLess(abs(counts[category] - total * weight), 1)

    def test_invalid_counts_and_totals_refuse(self):
        for total in (4999, 10001, True, 5000.0, "5000"):
            with self.subTest(total=total), self.assertRaises(ValueError):
                target_counts(total)
        self.assertEqual(validate_counts(target_counts(6000)), target_counts(6000))
        for counts in (
            {}, {"ui": 5000},
            dict(target_counts(5000), coding=True),
            dict(target_counts(5000), coding=999),
            dict(target_counts(5000), unexpected=1),
        ):
            with self.subTest(counts=counts), self.assertRaises(ValueError):
                validate_counts(counts)


class ActivationProfilerTests(unittest.TestCase):
    def one(self, profile, sample, expert, category="ui", layer="layer", repeats=1):
        profile.record(layer, category, sample,
                       [[expert]] * repeats, [[1.0]] * repeats, [True] * repeats)

    def test_padding_and_upstream_sentinel_do_not_influence_selection(self):
        profile = ActivationProfiler({"layer": 3})
        profile.record("layer", "ui", "padded",
                       [[0, 3], [2, 3], [3, 3]],
                       [[0.25, 0.75], [1e300, 0], [1.0, 0.0]],
                       [True, False, True])
        self.assertEqual(profile.select(2, False), {"layer": (0,)})
        stats = profile.to_record()["layers"]["layer"]["ui"]
        self.assertEqual(stats["valid_tokens"], 1)
        self.assertEqual(stats["mean_routing_mass"], [1.0, 0.0, 0.0])

    def test_sample_length_cannot_dominate_expert_rank(self):
        short, long = ActivationProfiler({"layer": 2}), ActivationProfiler({"layer": 2})
        for profile, length in ((short, 1), (long, 100)):
            self.one(profile, "first", 1, repeats=length)
            self.one(profile, "second", 0)
        self.assertEqual(short.select(2, False), long.select(2, False))
        self.assertEqual(long.select(2, False), {"layer": (0, 1)})
        self.assertEqual(long.to_record()["layers"]["layer"]["ui"]["mean_routing_mass"], [0.5, 0.5])

    def test_category_population_cannot_override_owner_weights(self):
        profile = ActivationProfiler({"layer": 2})
        for index in range(100):
            self.one(profile, f"ui-{index}", 1)
        for category in CATEGORY_WEIGHTS:
            if category != "ui":
                self.one(profile, category, 0, category)
        self.assertEqual(profile.select(1), {"layer": (0,)})

    def test_routing_magnitude_cannot_override_per_token_distribution(self):
        profile = ActivationProfiler({"layer": 2})
        profile.record("layer", "ui", "magnitudes",
                       [[0, 1], [0, 1]], [[1, 3], [1e308, 1e308]], [True, True])
        actual = profile.to_record()["layers"]["layer"]["ui"]["mean_routing_mass"]
        self.assertAlmostEqual(actual[0], 0.375)
        self.assertAlmostEqual(actual[1], 0.625)
        self.assertEqual(profile.select(1, False), {"layer": (1,)})

    def test_zero_mass_does_not_allocate_unseen_experts(self):
        profile = ActivationProfiler({"layer": 3})
        profile.record("layer", "ui", "zero",
                       [[0, 1], [1, 2]], [[0, 0], [0, 1]], [True, True])
        self.assertEqual(profile.select(3, False), {"layer": (2,)})
        self.assertEqual(profile.to_record()["layers"]["layer"]["ui"]["valid_tokens"], 1)

    def test_equal_scores_use_expert_id_and_layer_order_is_canonical(self):
        profile = ActivationProfiler({"z": 3, "a": 3})
        for layer in ("z", "a"):
            self.one(profile, "third", 2, layer=layer)
            self.one(profile, "first", 0, layer=layer)
        self.assertEqual(profile.select(1, False), {"a": (0,), "z": (0,)})
        self.assertEqual(list(profile.select(1, False)), ["a", "z"])

    def test_completeness_gate_cannot_be_satisfied_by_empty_or_other_layer(self):
        profile = ActivationProfiler({"layer": 3})
        self.one(profile, "only-ui", 1)
        with self.assertRaisesRegex(ValueError, "lacks curriculum"):
            profile.select(1)
        empty_layer = ActivationProfiler({"layer": 3, "unobserved": 3})
        for category in CATEGORY_WEIGHTS:
            self.one(empty_layer, category, 1, category)
        with self.assertRaisesRegex(ValueError, "no observations"):
            empty_layer.select(1, False)
        with self.assertRaises(ValueError):
            ActivationProfiler({"layer": 3}).select(1, False)

    def test_duplicate_and_cross_category_sample_rejection_are_transactional(self):
        profile = ActivationProfiler({"layer": 2, "other": 2})
        self.one(profile, "same-sample", 0)
        before = profile.to_record()
        for layer, category in (("layer", "ui"), ("other", "coding")):
            with self.subTest(layer=layer), self.assertRaises(ValueError):
                self.one(profile, "same-sample", 1, category, layer)
            self.assertEqual(profile.to_record(), before)
        self.one(profile, "same-sample", 1, "ui", "other")
        self.assertEqual(profile.select(1, False), {"layer": (0,), "other": (1,)})

    def test_invalid_observations_do_not_partially_record(self):
        profile = ActivationProfiler({"layer": 2})
        base = {"layer": "layer", "category": "ui", "sample_id": "invalid",
                "expert_indices": [[0], [1]], "routing_weights": [[1], [1]],
                "token_mask": [True, True]}
        mutations = (
            {"layer": "unknown"}, {"category": "unknown"}, {"sample_id": ""},
            {"expert_indices": [[0], [-1]]}, {"expert_indices": [[0], [3]]},
            {"expert_indices": [[0], [True]]},
            {"expert_indices": [[0, 0], [0, 1]], "routing_weights": [[1, 1], [1, 1]]},
            {"routing_weights": [[1], [math.nan]]},
            {"routing_weights": [[1], [math.inf]]},
            {"routing_weights": [[1], [-1]]},
            {"routing_weights": [[1], [True]]},
            {"routing_weights": [[1], ["1"]]},
            {"routing_weights": [[1]]},
            {"token_mask": [True, 2]},
            {"token_mask": [True]},
            {"expert_indices": [[0], [0, 1]], "routing_weights": [[1], [1, 1]]},
            {"expert_indices": [], "routing_weights": [], "token_mask": []},
        )
        before = profile.to_record()
        for mutation in mutations:
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                profile.record(**dict(base, **mutation))
            self.assertEqual(profile.to_record(), before)
        profile.record(**base)
        self.assertEqual(profile.to_record()["layers"]["layer"]["ui"]["samples"], 1)

    def test_empty_signal_refuses_without_consuming_identity(self):
        profile = ActivationProfiler({"layer": 2})
        for indices, weights, mask in (
            ([[0]], [[1]], [False]),
            ([[2]], [[1]], [True]),
            ([[0]], [[0]], [True]),
        ):
            with self.assertRaisesRegex(ValueError, "no unmasked"):
                profile.record("layer", "ui", "retry", indices, weights, mask)
        self.one(profile, "retry", 1)
        self.assertEqual(profile.select(1, False), {"layer": (1,)})

    def test_bad_topology_and_selection_budget_refuse(self):
        for topology in ({}, {"layer": 0}, {"layer": True}, {"layer": 1.0}, {"": 2}):
            with self.subTest(topology=topology), self.assertRaises(ValueError):
                ActivationProfiler(topology)
        profile = ActivationProfiler({"layer": 2})
        self.one(profile, "sample", 0)
        for budget in (0, -1, 3, True, 1.0):
            with self.subTest(budget=budget), self.assertRaises(ValueError):
                profile.select(budget, False)
        with self.assertRaises(ValueError):
            profile.select(1, None)

    def test_snapshot_is_detached_and_binds_original_observation(self):
        profile = ActivationProfiler({"layer": 2})
        self.one(profile, "sample", 0)
        snapshot = profile.to_record()
        original = copy.deepcopy(snapshot)
        supplied_digest = snapshot.pop("sha256")
        canonical = json.dumps(snapshot, sort_keys=True, separators=(",", ":"), allow_nan=False)
        self.assertEqual(supplied_digest, hashlib.sha256(canonical.encode()).hexdigest())
        snapshot["layers"]["layer"]["ui"]["routing_mass_sum"][0] = 99
        snapshot["topology"]["layer"] = 500
        self.assertEqual(profile.to_record(), original)
        with self.assertRaises(TypeError):
            profile.topology["layer"] = 500
        other = ActivationProfiler({"layer": 2})
        self.one(other, "different-input-identity", 0)
        self.assertNotEqual(other.to_record()["sha256"], original["sha256"])

    def dataset_identity(self, purpose="software_test_only"):
        return {"manifest_sha256": "a" * 64, "dataset_sha256": "b" * 64,
                "purpose": purpose, "format": "alora-pretokenized-jsonl-v1"}

    def test_base_identity_and_dataset_binding_are_explicit_and_read_only(self):
        for digest in ("", "A" * 64, "a" * 63, True, 1):
            with self.subTest(digest=digest), self.assertRaises(ValueError):
                ActivationProfiler({"layer": 2}, base_model_sha256=digest)
        profile = ActivationProfiler({"layer": 2}, base_model_sha256="c" * 64)
        initial = profile.to_record()
        identity = self.dataset_identity()
        profile.bind_dataset(identity)
        identity["manifest_sha256"] = "d" * 64
        self.assertEqual(profile.base_model_sha256, "c" * 64)
        self.assertEqual(profile.source_dataset, self.dataset_identity())
        self.assertEqual(profile.purpose, "software_test_only")
        record = profile.to_record()
        self.assertEqual(record["source_dataset"], self.dataset_identity())
        self.assertEqual(record["purpose"], "software_test_only")
        self.assertEqual(record["base_model_sha256"], "c" * 64)
        self.assertNotEqual(record["sha256"], initial["sha256"])
        returned = profile.source_dataset
        returned["purpose"] = "real_training"
        self.assertEqual(profile.purpose, "software_test_only")
        profile.bind_dataset(self.dataset_identity())
        self.assertEqual(profile.to_record(), record)
        with self.assertRaises(AttributeError):
            profile.base_model_sha256 = "d" * 64

    def test_dataset_or_purpose_rebinding_is_refused_without_mutation(self):
        profile = ActivationProfiler({"layer": 2}, base_model_sha256="c" * 64)
        profile.bind_dataset(self.dataset_identity())
        before = profile.to_record()
        for change in ({"purpose": "real_training"}, {"manifest_sha256": "d" * 64},
                       {"dataset_sha256": "e" * 64}, {"purpose": "unrecognized"},
                       {"format": "unknown"}, {"dataset_sha256": "bad"},
                       {"unexpected": "field"}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                profile.bind_dataset(dict(self.dataset_identity(), **change))
            self.assertEqual(profile.to_record(), before)

    def test_real_profile_metadata_requires_base_and_cannot_claim_old_unbound_observations(self):
        profile = ActivationProfiler({"layer": 2})
        with self.assertRaisesRegex(ValueError, "base model"):
            profile.bind_dataset(self.dataset_identity("real_training"))
        self.assertIsNone(profile.source_dataset)
        self.one(profile, "unbound", 0)
        with self.assertRaisesRegex(ValueError, "retroactively"):
            profile.bind_dataset(self.dataset_identity())
        self.assertIsNone(profile.purpose)
        # Metadata-only acceptance exercises an identity contract, not a dataset.
        bound = ActivationProfiler({"layer": 2}, base_model_sha256="c" * 64)
        bound.bind_dataset(self.dataset_identity("real_training"))
        self.assertEqual(bound.purpose, "real_training")
        self.assertEqual(bound.to_record()["observations"], [])

    def test_deepcopy_preserves_bindings_and_isolates_observations(self):
        profile = ActivationProfiler({"layer": 2}, base_model_sha256="c" * 64)
        profile.bind_dataset(self.dataset_identity())
        self.one(profile, "first", 0)
        before = profile.to_record()
        staged = copy.deepcopy(profile)
        self.assertEqual(staged.to_record(), before)
        self.one(staged, "second", 1)
        self.assertEqual(profile.to_record(), before)
        self.assertEqual(staged.to_record()["layers"]["layer"]["ui"]["samples"], 2)

    def roundtrip_fixture(self):
        profile = ActivationProfiler({"z": 3, "a": 3}, base_model_sha256="c" * 64)
        profile.bind_dataset(self.dataset_identity())
        for category in CATEGORY_WEIGHTS:
            for layer in ("a", "z"):
                profile.record(layer, category, category,
                               [[0, 1], [1, 2]], [[1.0, 3.0], [2.0, 2.0]], [True, True])
        return profile

    def resign_metadata(self, record):
        # Test-only re-signing exercises semantic validation even when supplied
        # pins agree with invalid metadata. It never authorizes a dataset/run.
        payload = {key: value for key, value in record.items() if key != "sha256"}
        record["sha256"] = hashlib.sha256(json.dumps(
            payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        return record

    def test_pinned_calibration_roundtrip_restores_selection_and_independent_mutability(self):
        profile = self.roundtrip_fixture()
        record = profile.to_record()
        restored = ActivationProfiler.from_record(record, expected_sha256=record["sha256"])
        self.assertEqual(restored.to_record(), record)
        self.assertEqual(restored.select(2), profile.select(2))
        self.assertEqual(restored.source_dataset, self.dataset_identity())
        self.assertEqual(restored.base_model_sha256, "c" * 64)
        self.one(restored, "next", 2, "coding", "a")
        self.assertEqual(profile.to_record(), record)
        self.assertNotEqual(restored.to_record()["sha256"], record["sha256"])

    def test_calibration_expected_and_embedded_pins_are_both_required(self):
        original = self.roundtrip_fixture().to_record()
        for expected in ("0" * 64, "A" * 64, "bad", None):
            with self.subTest(expected=expected), self.assertRaises(ValueError):
                ActivationProfiler.from_record(original, expected_sha256=expected)
        changed = copy.deepcopy(original)
        changed["observations"][0]["sha256"] = "d" * 64
        with self.assertRaisesRegex(ValueError, "pinned canonical"):
            ActivationProfiler.from_record(changed, expected_sha256=original["sha256"])
        changed = copy.deepcopy(original)
        changed["sha256"] = "d" * 64
        with self.assertRaisesRegex(ValueError, "pinned canonical"):
            ActivationProfiler.from_record(changed, expected_sha256=original["sha256"])

    def test_resigned_malformed_calibration_cannot_restore(self):
        original = self.roundtrip_fixture().to_record()
        def stats(record):
            return record["layers"]["a"]["ui"]
        changes = [
            lambda r: r.__setitem__("schema", "unknown"),
            lambda r: r.__setitem__("normalization", "raw-token-total"),
            lambda r: r.__setitem__("unexpected", True),
            lambda r: r.pop("purpose"),
            lambda r: r["category_weights"].__setitem__("ui", 0.5),
            lambda r: r["topology"].__setitem__("a", True),
            lambda r: r["layers"].pop("z"),
            lambda r: r["layers"]["a"].pop("coding"),
            lambda r: stats(r).__setitem__("samples", True),
            lambda r: stats(r).__setitem__("samples", 2),
            lambda r: stats(r).__setitem__("valid_tokens", 0),
            lambda r: stats(r).__setitem__("valid_tokens", 2.0),
            lambda r: stats(r).__setitem__("routing_mass_sum", [0.5, 0.5]),
            lambda r: stats(r)["routing_mass_sum"].__setitem__(0, -0.1),
            lambda r: stats(r)["routing_mass_sum"].__setitem__(0, math.inf),
            lambda r: stats(r)["routing_mass_sum"].__setitem__(0, math.nan),
            lambda r: stats(r)["routing_mass_sum"].__setitem__(0, True),
            lambda r: stats(r)["routing_mass_sum"].__setitem__(0, 0.2),
            lambda r: stats(r).__setitem__("mean_routing_mass", None),
            lambda r: stats(r)["mean_routing_mass"].__setitem__(0, 0.2),
            lambda r: stats(r).__setitem__("unexpected", 1),
            lambda r: r["observations"].append(copy.deepcopy(r["observations"][0])),
            lambda r: r["observations"].pop(),
            lambda r: r["observations"][0].__setitem__("sample_id", ""),
            lambda r: r["observations"][0].__setitem__("layer", "unknown"),
            lambda r: r["observations"][0].__setitem__("category", "unknown"),
            lambda r: r["observations"][0].__setitem__("sha256", "invalid"),
            lambda r: r["observations"][-1].__setitem__("category", "ui"),
            lambda r: r["observations"].reverse(),
            lambda r: r.__setitem__("purpose", "real_training"),
            lambda r: r["source_dataset"].__setitem__("format", "unknown"),
            lambda r: r.__setitem__("source_dataset", None),
        ]
        for index, change in enumerate(changes):
            altered = copy.deepcopy(original)
            change(altered)
            self.resign_metadata(altered)
            with self.subTest(case=index), self.assertRaises(ValueError):
                ActivationProfiler.from_record(altered, expected_sha256=altered["sha256"])
        self.assertEqual(self.roundtrip_fixture().to_record(), original)

    def test_restored_real_binding_requires_base_and_unbound_history_stays_unbound(self):
        record = self.roundtrip_fixture().to_record()
        record["source_dataset"]["purpose"] = "real_training"
        record["purpose"] = "real_training"
        record["base_model_sha256"] = None
        self.resign_metadata(record)
        with self.assertRaisesRegex(ValueError, "base model"):
            ActivationProfiler.from_record(record, expected_sha256=record["sha256"])
        unbound = ActivationProfiler({"layer": 2})
        self.one(unbound, "unbound", 0)
        record = unbound.to_record()
        restored = ActivationProfiler.from_record(record, expected_sha256=record["sha256"])
        self.assertEqual(restored.to_record(), record)
        self.assertIsNone(restored.purpose)
        with self.assertRaisesRegex(ValueError, "retroactively"):
            restored.bind_dataset(self.dataset_identity())

    def test_empty_calibration_roundtrip_rejects_invented_tokens_or_means(self):
        record = ActivationProfiler({"layer": 2}).to_record()
        restored = ActivationProfiler.from_record(record, expected_sha256=record["sha256"])
        self.assertEqual(restored.to_record(), record)
        for field, value in (("valid_tokens", 1), ("mean_routing_mass", [0.0, 0.0]),
                             ("routing_mass_sum", [1e-12, 0.0])):
            altered = copy.deepcopy(record)
            altered["layers"]["layer"]["ui"][field] = value
            self.resign_metadata(altered)
            with self.subTest(field=field), self.assertRaises(ValueError):
                ActivationProfiler.from_record(altered, expected_sha256=altered["sha256"])


if __name__ == "__main__":
    unittest.main()
