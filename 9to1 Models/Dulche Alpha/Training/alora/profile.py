"""Activation-profiled expert selection, independent of any Torch/runtime import.

One record is one original sample at one expert layer. Callers pass ordinary
lists/tuples after detaching their actual dispatch observations. Padding and
the upstream num_experts sentinel are excluded. Positive real-expert weights
are normalized per token, then averaged per sample and per category. This
prevents token length or sample-count imbalance from rewriting the curriculum.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
import copy
import hashlib
import json
import math
import re
from types import MappingProxyType

from .curriculum import CATEGORY_WEIGHTS

_SHA256 = re.compile(r"[0-9a-f]{64}")
_DATASET_FIELDS = {"manifest_sha256", "dataset_sha256", "purpose", "format"}
_RECORD_FIELDS = {"schema", "base_model_sha256", "source_dataset", "purpose",
                  "normalization", "topology", "category_weights", "layers",
                  "observations", "sha256"}


def _text(value: str, kind: str) -> str:
    if (type(value) is not str or not value.strip() or len(value) > 512
            or any(ord(character) < 32 for character in value)):
        raise ValueError(f"{kind} must be bounded non-empty text.")
    return value


def _digest(value: dict) -> str:
    content = json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)
    return hashlib.sha256(content.encode("utf-8")).hexdigest()


@dataclass
class _Category:
    mass: list[float]
    samples: int = 0
    tokens: int = 0


class ActivationProfiler:
    """Collect immutable sample identities and rank actually observed experts.

    Recording is intentionally single-writer. A duplicate sample/layer is an
    error, including a repeated observation during activation recomputation.
    select() returns up to its budget; unused experts never fill spare slots.
    """

    def __init__(self, topology: Mapping[str, int], *, base_model_sha256: str | None = None):
        if base_model_sha256 is not None and (type(base_model_sha256) is not str
                or not _SHA256.fullmatch(base_model_sha256)):
            raise ValueError("The base model identity must be a lowercase SHA-256 digest.")
        self._base_model_sha256 = base_model_sha256
        self._source_dataset = None
        if not isinstance(topology, Mapping) or not topology:
            raise ValueError("An explicit non-empty expert topology is required.")
        for layer, count in topology.items():
            _text(layer, "Layer")
            if type(count) is not int or not 1 <= count <= 65_536:
                raise ValueError("Each layer needs a positive bounded expert count.")
        self.topology = MappingProxyType(dict(sorted(topology.items())))
        self._stats = {
            layer: {category: _Category([0.0] * count) for category in CATEGORY_WEIGHTS}
            for layer, count in self.topology.items()
        }
        self._observations: dict[tuple[str, str], str] = {}
        self._sample_categories: dict[str, str] = {}

    @classmethod
    def from_record(cls, record: dict, *, expected_sha256: str) -> "ActivationProfiler":
        """Restore exact pinned calibration metadata, never replay its original data.

        The caller supplies the separately trusted canonical record digest. Pins
        establish identity and consistency; they cannot prove the truth of a
        dataset's lineage or that a claimed dispatch observation was executed.
        """
        if (type(expected_sha256) is not str or not _SHA256.fullmatch(expected_sha256)
                or type(record) is not dict or set(record) != _RECORD_FIELDS):
            raise ValueError("An exact calibration record and expected SHA-256 are required.")
        payload = {key: value for key, value in record.items() if key != "sha256"}
        try:
            checksum = _digest(payload)
        except (TypeError, ValueError, OverflowError, RecursionError) as error:
            raise ValueError("Calibration metadata must be canonical finite JSON values.") from error
        if record["sha256"] != checksum or checksum != expected_sha256:
            raise ValueError("Calibration metadata differs from its pinned canonical SHA-256.")
        if (record["schema"] != "alora-activation-profile-v1"
                or record["normalization"] != "positive-real-route-per-token/sample/category"
                or type(record["category_weights"]) is not dict
                or record["category_weights"] != dict(CATEGORY_WEIGHTS)
                or any(type(value) is not float for value in record["category_weights"].values())
                or type(record["topology"]) is not dict):
            raise ValueError("Calibration schema, normalization, weights or topology is unsupported.")
        restored = cls(record["topology"], base_model_sha256=record["base_model_sha256"])
        source = record["source_dataset"]
        if source is not None:
            if type(source) is not dict:
                raise ValueError("Calibration dataset binding must be a complete object.")
            restored.bind_dataset(source)
        if record["purpose"] != restored.purpose:
            raise ValueError("Calibration purpose differs from its immutable dataset binding.")
        observations = record["observations"]
        if type(observations) is not list:
            raise ValueError("Calibration observations must be a list.")
        observed_counts = {
            layer: {category: 0 for category in CATEGORY_WEIGHTS}
            for layer in restored.topology
        }
        for observation in observations:
            if type(observation) is not dict or set(observation) != {"layer", "sample_id", "category", "sha256"}:
                raise ValueError("Calibration observation fields differ from the schema.")
            layer, sample_id, category, pin = (observation[key]
                for key in ("layer", "sample_id", "category", "sha256"))
            if (type(layer) is not str or layer not in restored.topology
                    or type(category) is not str or category not in CATEGORY_WEIGHTS
                    or type(pin) is not str or not _SHA256.fullmatch(pin)):
                raise ValueError("Calibration observation references an invalid layer, category or digest.")
            _text(sample_id, "Sample identity")
            if (layer, sample_id) in restored._observations:
                raise ValueError("Calibration contains a duplicate sample/layer observation.")
            if restored._sample_categories.get(sample_id, category) != category:
                raise ValueError("Calibration sample categories conflict between layers.")
            restored._observations[(layer, sample_id)] = pin
            restored._sample_categories[sample_id] = category
            observed_counts[layer][category] += 1
        layers = record["layers"]
        if type(layers) is not dict or set(layers) != set(restored.topology):
            raise ValueError("Calibration statistics must cover exactly the declared layers.")
        fields = {"samples", "valid_tokens", "routing_mass_sum", "mean_routing_mass"}
        for layer, expert_count in restored.topology.items():
            categories = layers[layer]
            if type(categories) is not dict or set(categories) != set(CATEGORY_WEIGHTS):
                raise ValueError("Calibration statistics must cover all curriculum categories.")
            for category, stats in categories.items():
                if type(stats) is not dict or set(stats) != fields:
                    raise ValueError("Calibration statistics have invalid fields.")
                samples, tokens = stats["samples"], stats["valid_tokens"]
                mass, mean = stats["routing_mass_sum"], stats["mean_routing_mass"]
                if (type(samples) is not int or samples < 0 or type(tokens) is not int
                        or tokens < samples or (samples == 0 and tokens != 0)
                        or samples != observed_counts[layer][category]):
                    raise ValueError("Calibration counts disagree with its unique observations.")
                if (type(mass) is not list or len(mass) != expert_count
                        or any(type(value) is not float or not math.isfinite(value) or value < 0
                               for value in mass)):
                    raise ValueError("Calibration routing mass must be finite, nonnegative and correctly shaped.")
                try:
                    mass_sum = math.fsum(mass)
                except OverflowError as error:
                    raise ValueError("Calibration routing mass exceeds its observed count.") from error
                if (not math.isclose(mass_sum, samples, rel_tol=1e-10, abs_tol=1e-9)
                        or (samples == 0 and any(value != 0 for value in mass))):
                    raise ValueError("Calibration routing mass does not match its sample count.")
                if samples:
                    derived = [value / samples for value in mass]
                    if (type(mean) is not list or len(mean) != expert_count
                            or any(type(value) is not float or not math.isfinite(value) or value < 0
                                   for value in mean)
                            or mean != derived):
                        raise ValueError("Calibration mean routing mass is not exactly derived from its totals.")
                elif mean is not None:
                    raise ValueError("Unobserved categories cannot have a mean routing mass.")
                restored._stats[layer][category] = _Category(list(mass), samples, tokens)
        if restored.to_record() != record:
            raise ValueError("Calibration metadata does not round-trip in canonical observation order.")
        return restored

    @property
    def base_model_sha256(self) -> str | None:
        return self._base_model_sha256

    @property
    def source_dataset(self) -> dict | None:
        return None if self._source_dataset is None else dict(self._source_dataset)

    @property
    def purpose(self) -> str | None:
        return None if self._source_dataset is None else self._source_dataset["purpose"]

    def bind_dataset(self, identity: Mapping[str, str]) -> None:
        """Bind one independently verified dataset once; identical rebinds are harmless."""
        if not isinstance(identity, Mapping) or set(identity) != _DATASET_FIELDS:
            raise ValueError("A profile needs the complete ALORA dataset identity.")
        if (type(identity["purpose"]) is not str
                or identity["purpose"] not in {"software_test_only", "real_training"}
                or identity["format"] != "alora-pretokenized-jsonl-v1"
                or any(type(identity[key]) is not str or not _SHA256.fullmatch(identity[key])
                       for key in ("manifest_sha256", "dataset_sha256"))):
            raise ValueError("A profile dataset identity has an invalid purpose, format or digest.")
        if identity["purpose"] == "real_training" and self.base_model_sha256 is None:
            raise ValueError("Real-training profiles require an exact base model SHA-256.")
        if self._source_dataset is not None and dict(identity) != dict(self._source_dataset):
            raise ValueError("A profile cannot mix dataset identities or execution purposes.")
        if self._source_dataset is None:
            if self._observations:
                raise ValueError("Unbound observations cannot acquire dataset provenance retroactively.")
            self._source_dataset = MappingProxyType(dict(identity))

    def __deepcopy__(self, memo):
        # Both mappings contain validated immutable scalar values. Share those
        # read-only bindings while copying every mutable observation/statistic.
        memo[id(self.topology)] = self.topology
        if self._source_dataset is not None:
            memo[id(self._source_dataset)] = self._source_dataset
        clone = object.__new__(type(self))
        memo[id(self)] = clone
        clone.__dict__ = copy.deepcopy(self.__dict__, memo)
        return clone

    def record(
        self,
        layer: str,
        category: str,
        sample_id: str,
        expert_indices: list | tuple,
        routing_weights: list | tuple,
        token_mask: list | tuple,
    ) -> None:
        """Validate the entire observation before publishing any contribution."""
        if layer not in self.topology:
            raise ValueError("The observation names an unknown expert layer.")
        if category not in CATEGORY_WEIGHTS:
            raise ValueError("The observation names an unknown curriculum category.")
        _text(sample_id, "Sample identity")
        if (layer, sample_id) in self._observations:
            raise ValueError("This sample/layer has already been profiled.")
        if self._sample_categories.get(sample_id, category) != category:
            raise ValueError("A sample cannot change curriculum category between layers.")
        rows = (expert_indices, routing_weights, token_mask)
        if any(not isinstance(row, (list, tuple)) for row in rows):
            raise ValueError("Dispatch observations must use ordinary lists or tuples.")
        if not expert_indices or not (len(expert_indices) == len(routing_weights) == len(token_mask)):
            raise ValueError("Dispatch indices, weights and token mask must align.")
        expert_count = self.topology[layer]
        mass = [0.0] * expert_count
        used_tokens = 0
        width = None
        for indices, weights, include in zip(expert_indices, routing_weights, token_mask):
            if not isinstance(indices, (list, tuple)) or not isinstance(weights, (list, tuple)):
                raise ValueError("Each dispatch row must be a list or tuple.")
            if not indices or len(indices) != len(weights):
                raise ValueError("Each token needs aligned non-empty top-k indices and weights.")
            if width is None:
                width = len(indices)
            if len(indices) != width:
                raise ValueError("All dispatch rows must have the same top-k width.")
            if not (type(include) is bool or (type(include) is int and include in (0, 1))):
                raise ValueError("Token masks must contain booleans or integer zero/one.")
            selected = []
            seen = set()
            for expert, weight in zip(indices, weights):
                if type(expert) is not int or not 0 <= expert <= expert_count:
                    raise ValueError("Expert indices must be in topology, or its sentinel.")
                if type(weight) not in (int, float):
                    raise ValueError("Routing weights must be finite non-negative numbers.")
                try:
                    weight = float(weight)
                except (ValueError, OverflowError) as error:
                    raise ValueError("Routing weights must be finite non-negative numbers.") from error
                if not math.isfinite(weight) or weight < 0:
                    raise ValueError("Routing weights must be finite non-negative numbers.")
                if expert < expert_count:
                    if expert in seen:
                        raise ValueError("A real expert cannot occur twice in one top-k row.")
                    seen.add(expert)
                    if weight > 0:
                        selected.append((expert, weight))
            if include and selected:
                # Scale before summing so valid large finite values cannot overflow.
                maximum = max(weight for _, weight in selected)
                denominator = math.fsum(weight / maximum for _, weight in selected)
                for expert, weight in selected:
                    mass[expert] += (weight / maximum) / denominator
                used_tokens += 1
        if not used_tokens:
            raise ValueError("The sample has no unmasked positive real-expert routing.")
        pin = _digest({
            "layer": layer, "category": category, "sample_id": sample_id,
            "expert_indices": expert_indices, "routing_weights": routing_weights,
            "token_mask": token_mask,
        })
        stats = self._stats[layer][category]
        stats.mass = [old + contribution / used_tokens
                      for old, contribution in zip(stats.mass, mass)]
        stats.samples += 1
        stats.tokens += used_tokens
        self._observations[(layer, sample_id)] = pin
        self._sample_categories[sample_id] = category

    def select(self, experts_per_layer: int, require_all_categories: bool = True) -> dict[str, tuple[int, ...]]:
        """Rank category-weighted mean routing mass, breaking equal scores by ID."""
        if (type(experts_per_layer) is not int or experts_per_layer <= 0
                or any(experts_per_layer > count for count in self.topology.values())):
            raise ValueError("The expert budget must fit every declared layer.")
        if type(require_all_categories) is not bool:
            raise ValueError("Category completeness must be an explicit boolean.")
        result = {}
        for layer, expert_count in self.topology.items():
            stats = self._stats[layer]
            missing = [category for category, row in stats.items() if not row.samples]
            if len(missing) == len(CATEGORY_WEIGHTS):
                raise ValueError(f"Layer {layer} has no observations.")
            if require_all_categories and missing:
                raise ValueError(f"Layer {layer} lacks curriculum categories: {', '.join(missing)}")
            scores = [
                math.fsum(CATEGORY_WEIGHTS[category] * row.mass[expert] / row.samples
                          for category, row in stats.items() if row.samples)
                for expert in range(expert_count)
            ]
            observed = [expert for expert, score in enumerate(scores) if score > 0]
            ranked = sorted(observed, key=lambda expert: (-scores[expert], expert))
            result[layer] = tuple(ranked[:experts_per_layer])
        return result

    def to_record(self) -> dict:
        """Return detached metadata plus a digest binding all original observations."""
        record = {
            "schema": "alora-activation-profile-v1",
            "base_model_sha256": self.base_model_sha256,
            "source_dataset": self.source_dataset,
            "purpose": self.purpose,
            "normalization": "positive-real-route-per-token/sample/category",
            "topology": dict(self.topology),
            "category_weights": dict(CATEGORY_WEIGHTS),
            "layers": {
                layer: {
                    category: {
                        "samples": row.samples,
                        "valid_tokens": row.tokens,
                        "routing_mass_sum": list(row.mass),
                        "mean_routing_mass": (
                            [value / row.samples for value in row.mass] if row.samples else None
                        ),
                    }
                    for category, row in stats.items()
                }
                for layer, stats in self._stats.items()
            },
            "observations": [
                {"layer": layer, "sample_id": sample_id,
                 "category": self._sample_categories[sample_id], "sha256": pin}
                for (layer, sample_id), pin in sorted(self._observations.items())
            ],
        }
        record["sha256"] = _digest(record)
        return record
