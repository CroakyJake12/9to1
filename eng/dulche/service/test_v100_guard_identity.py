"""Regression for the live -B guard command and rejection of unrelated commands."""

import unittest
from pathlib import Path

from v100_recover import guard_command_matches


class GuardCommandIdentityTests(unittest.TestCase):
    def test_real_launcher_variants_match_the_same_guard_and_config(self):
        path = Path('/private/v100-guard.json')
        tail = [b'-m', b'serve.thermal_guard', b'--config', bytes(path)]
        for flags in ([], [b'-B']):
            with self.subTest(flags=flags):
                self.assertTrue(guard_command_matches([b'/usr/bin/python3', *flags, *tail], path))

    def test_wrong_guard_config_or_extra_arguments_are_rejected(self):
        path = Path('/private/v100-guard.json')
        tail = [b'-m', b'serve.thermal_guard', b'--config', bytes(path)]
        cases = [
            [b'-c', b'print("serve.thermal_guard")', *tail],
            [b'-m', b'other.thermal_guard', b'--config', bytes(path)],
            [b'-m', b'serve.thermal_guard', b'--config', b'/other/config.json'],
            [*tail, b'--config', b'/other/config.json'],
            [b'-B', b'-B', *tail],
            [b'-I', *tail],
            [],
        ]
        for args in cases:
            with self.subTest(args=args):
                self.assertFalse(guard_command_matches([b'/usr/bin/python3', *args], path))


if __name__ == '__main__':
    unittest.main()
