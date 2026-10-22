import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("package_aggregate", Path(__file__).with_name("aggregate-package-journeys.py"))
aggregate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(aggregate)
HEAD = "37ba63a92a509013435d61af73cbe17e246167bf"


class ExactCandidateAggregateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.outcomes = {}
        for name, (filename, variable, status) in aggregate.GATES.items():
            self.outcomes[variable] = "success"
            self.write(filename, {"status": status, "source_head": HEAD,
                                  "acceptance_tools_head": HEAD,
                                  "installed_build_provenance": {"source_head": HEAD}})

    def write(self, name, raw):
        (self.root / name).write_text(json.dumps(raw))

    def result(self):
        return aggregate.aggregate(self.root, HEAD, self.outcomes)

    def test_successful_focused_receipts_remain_checkpoint_not_release(self):
        receipt = self.result()
        self.assertEqual(receipt["status"], "CHECKPOINT")
        self.assertFalse(receipt["release_ready"])
        self.assertFalse(receipt["automatic_full_application_acceptance"])
        self.assertEqual(receipt["owner_laptop"], "NOT RUN")

    def test_failed_outcome_cannot_be_hidden_by_continue_on_error_conclusion(self):
        self.outcomes["STARTUP_OUTCOME"] = "failure"
        self.outcomes["STARTUP_CONCLUSION"] = "success"
        receipt = self.result()
        self.assertEqual(receipt["status"], "BLOCKED")
        self.assertIn("STEP_NOT_SUCCESSFUL", [r["code"] for r in receipt["gates"][0]["failures"]])

    def test_failed_missing_or_stale_raw_receipts_never_pass(self):
        name = aggregate.GATES["browser"][0]
        raw = json.loads((self.root / name).read_text())
        for changes in [{"status": "FAIL"}, {"source_head": "0" * 40},
                        {"acceptance_tools_head": "0" * 40},
                        {"installed_build_provenance": {"source_head": "0" * 40}},
                        {"required_phase_failures": [{"phase": "normal-close"}]}]:
            with self.subTest(changes=changes):
                self.write(name, {**raw, **changes})
                self.assertEqual(self.result()["status"], "BLOCKED")
        (self.root / name).unlink()
        self.assertEqual(self.result()["gates"][2]["failures"][0]["kind"], "INFRASTRUCTURE")

    def test_corrupt_receipt_is_retained_as_infrastructure_failure(self):
        (self.root / aggregate.GATES["chat"][0]).write_text("invalid JSON")
        receipt = self.result()
        aggregate.write_report(receipt, self.root)
        written = json.loads((self.root / "combined-package-journey-receipt.json").read_text())
        self.assertEqual(written["status"], "BLOCKED")
        self.assertEqual(written["gates"][1]["failures"][0]["kind"], "INFRASTRUCTURE")
        self.assertIn("invalid", written["gates"][1]["failures"][0]["code"].lower())


if __name__ == "__main__":
    unittest.main()
