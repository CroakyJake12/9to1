#!/usr/bin/env python3
"""Exercise the live protection gate against isolated files, never real disks."""
from pathlib import Path
import subprocess
import tempfile
import unittest

recipe = Path(__file__).with_name('battery-test') / 'start-battery-test.sh'
guard = recipe.read_text().split('# Disk discovery', 1)[1].split('\nbus=\n', 1)[0]
guard = '# Disk discovery' + guard
rule = ('ACTION=="add|change", SUBSYSTEM=="block", ATTRS{model}=="THGJFJT2T85BAT0A*", '
        'RUN+="/sbin/blockdev --setro /dev/%k"\n')


class ProtectionTests(unittest.TestCase):
    def invoke(self, disk_ro=None, partition_ro=None, has_rule=True):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            rules = root / 'guard.rules'
            rules.write_text(rule if has_rule else '')
            if disk_ro is not None:
                disk = root / 'sys/class/block/sdb'
                (disk / 'device').mkdir(parents=True)
                (disk / 'device/model').write_text('THGJFJT2T85BAT0A\n')
                (disk / 'ro').write_text(str(disk_ro))
                if partition_ro is not None:
                    part = disk / 'sdb1'
                    part.mkdir()
                    (part / 'partition').write_text('1')
                    (part / 'ro').write_text(str(partition_ro))
            code = guard.replace('/etc/udev/rules.d/99-book4edge-internal-test.rules', str(rules))
            code = code.replace('/sys/class/block/', str(root / 'sys/class/block') + '/')
            return subprocess.run(['sh', '-eu', '-c', code], capture_output=True, text=True)

    def test_absent_ufs_does_not_disable_battery(self):
        self.assertEqual(self.invoke().returncode, 0)

    def test_missing_guard_refuses(self):
        self.assertNotEqual(self.invoke(has_rule=False).returncode, 0)

    def test_read_only_disk_and_partition_pass(self):
        self.assertEqual(self.invoke(1, 1).returncode, 0)

    def test_writable_disk_refuses(self):
        self.assertNotEqual(self.invoke(0, 1).returncode, 0)

    def test_writable_partition_refuses(self):
        self.assertNotEqual(self.invoke(1, 0).returncode, 0)


if __name__ == '__main__':
    unittest.main()
