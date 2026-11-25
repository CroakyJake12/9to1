"""Offline ALORA preparation commands; no model loading or device/service changes."""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path

from .curriculum import CATEGORY_PERCENTAGES, target_counts, validate_counts
from .data import TrainingDataset
from .profile import ActivationProfiler


def _json(path):
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("The input document exceeds 64 MiB")
    def strict(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON fields are refused")
            result[key] = value
        return result
    return json.loads(path.read_text(), object_pairs_hook=strict,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON")))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    curriculum = sub.add_parser("curriculum", help="Show the agreed category quotas")
    curriculum.add_argument("--examples", type=int, required=True)
    dataset = sub.add_parser("check-dataset", help="Verify pinned local pretokenized training data")
    dataset.add_argument("--manifest", type=Path, required=True)
    dataset.add_argument("--manifest-sha256", required=True)
    dataset.add_argument("--root", type=Path, required=True)
    dataset.add_argument("--vocab-size", type=int, required=True)
    dataset.add_argument("--max-tokens", type=int, required=True)
    dataset.add_argument("--software-test-only", action="store_true")
    plan = sub.add_parser("plan", help="Select experts from an actual retained activation profile")
    plan.add_argument("--profile", type=Path, required=True)
    plan.add_argument("--profile-sha256", required=True, help="Canonical sha256 in the profile record")
    plan.add_argument("--experts-per-layer", type=int, required=True)
    plan.add_argument("--rank", type=int, default=8)
    plan.add_argument("--alpha", type=float, default=16.0)
    plan.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    if args.command == "curriculum":
        result = {"examples": args.examples, "category_counts": target_counts(args.examples),
                  "category_percentages": dict(CATEGORY_PERCENTAGES)}
    elif args.command == "check-dataset":
        purpose = "software_test_only" if args.software_test_only else "real_training"
        loaded = TrainingDataset(args.manifest, args.root, expected_sha256=args.manifest_sha256,
                                 purpose=purpose, max_tokens=args.max_tokens, vocab_size=args.vocab_size)
        counts = {category: 0 for category in CATEGORY_PERCENTAGES}
        counts.update(Counter(row["category"] for row in loaded.rows()))
        if purpose == "real_training":
            validate_counts(counts)
        result = {**loaded.identity, "category_counts": counts,
                  "training_samples": sum(counts.values()), "validated": True}
    else:
        if not 1 <= args.rank <= 1024 or not math.isfinite(args.alpha) or args.alpha <= 0:
            raise ValueError("Rank and alpha must be finite positive bounded settings")
        profile = ActivationProfiler.from_record(_json(args.profile), expected_sha256=args.profile_sha256)
        if profile.purpose is None:
            raise ValueError("A training plan requires a dataset-bound profile")
        selected = profile.select(args.experts_per_layer, require_all_categories=True)
        result = {
            "schema": "alora-selected-expert-plan-v1",
            "purpose": profile.purpose, "base_model_sha256": profile.base_model_sha256,
            "source_dataset": profile.source_dataset, "profile_sha256": args.profile_sha256,
            "experts_per_layer": args.experts_per_layer,
            "rank": args.rank, "alpha": args.alpha,
            "selections": {path: list(experts) for path, experts in selected.items()},
            "router_frozen": True,
        }
        payload = (json.dumps(result, indent=2, sort_keys=True, allow_nan=False) + "\n").encode()
        # Exclusive creation preserves previous plans; it does not launch or register a worker.
        with args.output.open("xb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        result = {"plan_path": str(args.output), "sha256": hashlib.sha256(payload).hexdigest(),
                  "selected_experts": sum(len(experts) for experts in selected.values()),
                  "purpose": profile.purpose}
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, OSError, TypeError, KeyError) as error:
        raise SystemExit(f"ALORA: {error}")
