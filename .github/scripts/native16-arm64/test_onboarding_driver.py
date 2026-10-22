"""Regression against actual ARM CI accessibility evidence; no app is simulated."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("onboarding_driver", HERE / "onboarding-driver.py")
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


class ActualHomeSignatureTests(unittest.TestCase):
    def setUp(self):
        fixture = HERE / "fixtures" / "arm39698883180-home-accessibility.json"
        data = fixture.read_bytes()
        self.assertEqual(hashlib.sha256(data).hexdigest(),
                         "758996ae676924df9052d79b8b5b60b4838dbf3a764a8e739e30a19b062271b7")
        self.rows = json.loads(data)

    def test_actual_canonical_home_without_retired_chat_is_recognized(self):
        self.assertNotIn("Open Chat", [row.get("name") for row in self.rows])
        self.assertEqual(driver.OnboardingDriver.home_kind(self.rows), "APPS_HOME")

    def test_loading_or_incomplete_home_never_counts_as_ready(self):
        for missing in ["Search or enter web address", "Search apps", "Open Build", "Open Files", "Open Write", "Open Spaces"]:
            with self.subTest(missing=missing):
                rows = [row for row in self.rows if row.get("name") != missing]
                self.assertIsNone(driver.OnboardingDriver.home_kind(rows))

    def test_invisible_disabled_or_zero_extent_controls_do_not_prove_home(self):
        for key, value in [("showing", False), ("enabled", False), ("rectangle", [0, 0, 0, 0])]:
            with self.subTest(key=key):
                rows = copy.deepcopy(self.rows)
                for row in rows:
                    if row.get("name") == "Open Build":
                        row[key] = value
                self.assertIsNone(driver.OnboardingDriver.home_kind(rows))

    def test_retired_chat_does_not_replace_the_required_build_entry(self):
        rows = copy.deepcopy(self.rows)
        for row in rows:
            if row.get("name") == "Open Build":
                row["name"] = "Open Chat"
        self.assertIsNone(driver.OnboardingDriver.home_kind(rows))


if __name__ == "__main__":
    unittest.main()
