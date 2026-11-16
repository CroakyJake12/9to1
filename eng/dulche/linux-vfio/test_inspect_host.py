"""CPU-only kernel-tree fixtures; never read a real GPU or launch an inspector."""
from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

import inspect_host


class InspectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-inspection-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sys = self.root / 'sys'
        self.devices = self.sys / 'bus/pci/devices'
        self.devices.mkdir(parents=True)

    def device(self, bdf, vendor, device, parent, group, driver=None):
        path = self.sys / 'devices' / parent / bdf
        path.mkdir(parents=True)
        for name, value in {'vendor': vendor, 'device': device,
                            'class': '0x030200' if vendor == '0x10de' else '0x010802',
                            'driver_override': '(null)'}.items():
            (path / name).write_text(value)
        (self.devices / bdf).symlink_to(path, target_is_directory=True)
        group_path = self.sys / 'kernel/iommu_groups' / str(group)
        (group_path / 'devices').mkdir(parents=True, exist_ok=True)
        (group_path / 'devices' / bdf).symlink_to(path, target_is_directory=True)
        (path / 'iommu_group').symlink_to(group_path, target_is_directory=True)
        if driver:
            driver_path = self.sys / 'bus/pci/drivers' / driver
            driver_path.mkdir(parents=True, exist_ok=True)
            (path / 'driver').symlink_to(driver_path, target_is_directory=True)
        return path

    def test_group_keeps_non_nvidia_dependency_and_reset_domain_keeps_all_descendants(self):
        bridge = 'pci0000:00/0000:00:1c.4'
        gpu = self.device('0000:05:00.0', '0x10de', '0x1db4', bridge, 18)
        self.device('0000:05:01.0', '0x025e', '0xf1ab', bridge, 18, 'nvme')
        self.device('0000:06:00.0', '0x10ec', '0x8125',
                    bridge + '/0000:05:02.0', 19, 'r8169')
        self.device('0000:01:00.0', '0x10de', '0x2c02',
                    'pci0000:00/0000:00:01.0', 14, 'nvidia')
        (gpu / 'reset_method').write_text('bus\n')
        rows = {row['bdf']: row for row in inspect_host.pci_devices(self.sys)}
        self.assertEqual(set(rows), {'0000:01:00.0', '0000:05:00.0'})
        target = rows['0000:05:00.0']
        self.assertEqual(target['group_members'], ['0000:05:00.0', '0000:05:01.0'])
        self.assertEqual(target['reset_domain_members'],
                         ['0000:05:00.0', '0000:05:01.0', '0000:06:00.0'])
        self.assertEqual(target['upstream_bridge'], '0000:00:1c.4')
        self.assertEqual(target['reset_methods'], ['bus'])
        self.assertEqual(target['iommu_group'], '18')
        self.assertIsNone(target['driver'])
        self.assertEqual(target['binding_state'], 'unbound')
        self.assertEqual(target['inspection_errors'], [])
        self.assertEqual(rows['0000:01:00.0']['driver'], 'nvidia')
        self.assertEqual(rows['0000:01:00.0']['binding_state'], 'bound')

    def test_dangling_or_invalid_driver_entry_is_unknown_not_unbound(self):
        gpu = self.device('0000:05:00.0', '0x10de', '0x1db4',
                          'pci0000:00/0000:00:1c.4', 18)
        driver = gpu / 'driver'
        driver.symlink_to(self.sys / 'missing-driver')
        row, = inspect_host.pci_devices(self.sys)
        self.assertEqual(row['binding_state'], 'unknown')
        self.assertIsNone(row['driver'])
        self.assertTrue(row['inspection_errors'])
        driver.unlink()
        driver.write_text('invalid regular-file entry')
        row, = inspect_host.pci_devices(self.sys)
        self.assertEqual(row['binding_state'], 'unknown')
        self.assertIsNone(row['driver'])
        self.assertTrue(row['inspection_errors'])

    def test_driver_permission_failure_is_unknown_not_unbound(self):
        self.device('0000:05:00.0', '0x10de', '0x1db4',
                    'pci0000:00/0000:00:1c.4', 18, 'vfio-pci')
        original = Path.lstat

        def denied_driver(path, *args, **kwargs):
            if path.name == 'driver':
                raise PermissionError('fixture binding access denied')
            return original(path, *args, **kwargs)

        with mock.patch.object(Path, 'lstat', denied_driver):
            row, = inspect_host.pci_devices(self.sys)
        self.assertEqual(row['binding_state'], 'unknown')
        self.assertIsNone(row['driver'])
        self.assertTrue(row['inspection_errors'])

    def test_missing_group_or_reset_capability_is_not_fabricated(self):
        gpu = self.device('0000:05:00.0', '0x10de', '0x1db4',
                          'pci0000:00/0000:00:1c.4', 18)
        (gpu / 'iommu_group').unlink()
        row, = inspect_host.pci_devices(self.sys)
        self.assertIsNone(row['iommu_group'])
        self.assertEqual(row['group_members'], [])
        self.assertEqual(row['reset_methods'], [])
        self.assertIsNone(row['reserved_regions'])

    def test_process_observation_uses_executable_identity_without_reading_arguments(self):
        proc = self.root / 'proc'
        for pid, comm, exe, cwd in (
            (12, 'codex', '/usr/lib/chatgpt/electron', '/home/user'),
            (9, 'python3', '/usr/bin/python3.12', '/work/9to1'),
            (30, 'unrelated', '/usr/bin/unrelated', '/home/user'),
        ):
            path = proc / str(pid)
            path.mkdir(parents=True)
            (path / 'comm').write_text(comm)
            (path / 'exe').symlink_to(exe)
            (path / 'cwd').symlink_to(cwd)
            (path / 'cmdline').write_bytes(b'overwritten argv0 --secret fixture-private-token\0')
        with mock.patch.object(Path, 'read_bytes', side_effect=AssertionError('must not read cmdline')):
            rows = inspect_host.observed_processes(proc)
        self.assertEqual([row['pid'] for row in rows], [9, 12])
        self.assertEqual(rows[1]['executable'], 'electron')
        self.assertNotIn('fixture-private-token', json.dumps(rows))
        self.assertEqual(set(rows[0]), {'pid', 'comm', 'executable', 'cwd'})

    def test_vanished_process_is_omitted_without_guessing_identity(self):
        path = self.root / 'proc/4'
        path.mkdir(parents=True)
        (path / 'comm').write_text('qemu-system-x86')
        self.assertEqual(inspect_host.observed_processes(path.parent), [])

    def test_failed_or_timed_out_metadata_command_retains_error_and_no_success(self):
        errors = []
        with mock.patch.object(inspect_host.subprocess, 'run',
                               return_value=subprocess.CompletedProcess(['fixture'], 2, '', 'denied')):
            self.assertIsNone(inspect_host.command(['fixture'], errors))
        with mock.patch.object(inspect_host.subprocess, 'run',
                               side_effect=subprocess.TimeoutExpired(['fixture'], 10)):
            self.assertIsNone(inspect_host.command(['fixture'], errors))
        self.assertEqual(len(errors), 2)
        self.assertIn('returned 2', errors[0])
        self.assertIn('unavailable', errors[1])

    def test_output_refuses_existing_file_and_existing_symlink(self):
        output = self.root / 'evidence.json'
        output.write_text('preserved evidence')
        link = self.root / 'linked.json'
        link.symlink_to(output)
        for destination in (output, link):
            with self.subTest(destination=destination), \
                 mock.patch.object(inspect_host, 'collect', return_value={'fixture': True}), \
                 mock.patch('sys.argv', ['inspect_host.py', '--output', str(destination)]):
                with self.assertRaises(FileExistsError):
                    inspect_host.main()
        self.assertEqual(output.read_text(), 'preserved evidence')
        self.assertTrue(link.is_symlink())

    def test_new_evidence_is_private_json(self):
        output = self.root / 'fresh.json'
        with mock.patch.object(inspect_host, 'collect', return_value={'fixture': True}), \
             mock.patch('sys.argv', ['inspect_host.py', '--output', str(output)]), \
             contextlib.redirect_stdout(io.StringIO()):
            inspect_host.main()
        self.assertEqual(json.loads(output.read_text()), {'fixture': True})
        self.assertEqual(output.stat().st_mode & 0o777, 0o600)


if __name__ == '__main__':
    unittest.main()
