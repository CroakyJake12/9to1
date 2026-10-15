import json
import os
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat.broker import CompatibilityBroker, CompatibilityError
from compatibility.wine.haven_compat.chooser import choices, execute, main, wine_manifest
from compatibility.wine.haven_compat.runtime import RuntimeError, pe_arch, runtime_metadata
from compatibility.wine.haven_compat.winboat import rdp_command, validate_connection


def write_pe(path, machine=0x8664):
    data = bytearray(88)
    data[:2] = b'MZ'
    struct.pack_into('<I', data, 60, 64)
    data[64:68] = b'PE\0\0'
    struct.pack_into('<H', data, 68, machine)
    path.write_bytes(data)


def write_runtime(root, machine=183):
    (root / 'bin').mkdir(parents=True)
    elf = bytearray(20)
    elf[:6] = b'\x7fELF\x02\x01'
    struct.pack_into('<H', elf, 18, machine)
    (root / 'bin/wine').write_bytes(elf)
    (root / 'bin/wine').chmod(0o755)
    dlls = root / 'lib/wine/aarch64-windows'
    dlls.mkdir(parents=True)
    for name in ('libarm64ecfex.dll', 'libwow64fex.dll'):
        (dlls / name).touch()
    (root / 'runtime.json').write_text(json.dumps({
        'schemaVersion': 1, 'hostArchitecture': 'arm64', 'translation': 'hangover-fex',
        'windowsArchitectures': ['x86', 'x86_64', 'arm64', 'arm64ec']}))


class ExeChooserTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.exe = self.root / 'program with $(touch BAD); spaces.exe'
        write_pe(self.exe)
        self.broker = CompatibilityBroker(state_root=self.root / 'compat/apps', runtime_root=self.root / 'runtimes')

    def tearDown(self):
        self.temporary.cleanup()

    def test_pe_architectures_without_execution(self):
        for machine, architecture in ((0x14c, 'x86'), (0x8664, 'x86_64'), (0xaa64, 'arm64'), (0xa641, 'arm64ec')):
            write_pe(self.exe, machine)
            self.assertEqual(pe_arch(self.exe), architecture)

    def test_malformed_header_and_dll_refused(self):
        for data in (b'MZ', bytes(88), b'MZ' + bytes(86)):
            self.exe.write_bytes(data)
            with self.assertRaises(ValueError):
                pe_arch(self.exe)
        write_pe(self.exe)
        data = bytearray(self.exe.read_bytes())
        struct.pack_into('<H', data, 86, 0x2000)
        self.exe.write_bytes(data)
        with self.assertRaisesRegex(ValueError, 'DLL'):
            pe_arch(self.exe)

    def test_only_exact_selected_file_is_granted(self):
        manifest = wine_manifest(self.exe, 'wine-test')
        self.assertEqual(len(manifest.mounts), 1)
        self.assertEqual(manifest.mounts[0].source, str(self.exe))
        self.assertEqual(manifest.mounts[0].mode, 'ro')
        self.assertEqual(manifest.entrypoint, r'Z:\mnt\haven-share\program.exe')
        self.assertEqual(manifest.network, 'none')
        self.assertNotIn(' ', manifest.app_id)

    def test_unavailable_backend_does_not_launch(self):
        with patch.object(self.broker, 'launch') as launch:
            with self.assertRaisesRegex(ValueError, 'Missing'):
                execute('wine', self.exe, {}, self.broker, {'wine': {'available': False, 'reason': 'Missing'}})
            launch.assert_not_called()
        self.assertFalse((self.root / 'state').exists())

    def test_plan_does_not_launch_or_create_state(self):
        with patch('compatibility.wine.haven_compat.chooser.read_config', return_value={}), \
             patch('compatibility.wine.haven_compat.chooser.CompatibilityBroker', return_value=self.broker), \
             patch('builtins.print'), patch.object(self.broker, 'launch') as launch:
            self.assertEqual(main(['--plan', '--', str(self.exe)]), 0)
        launch.assert_not_called()
        self.assertFalse((self.root / 'state').exists())

    def test_arm64_requires_fex_and_matching_elf(self):
        root = self.root / 'runtime'
        write_runtime(root)
        self.assertEqual(runtime_metadata(root, 'aarch64')['translation'], 'hangover-fex')
        with self.assertRaisesRegex(RuntimeError, 'does not match'):
            runtime_metadata(root, 'x86_64')
        (root / 'lib/wine/aarch64-windows/libwow64fex.dll').unlink()
        with self.assertRaisesRegex(RuntimeError, 'missing'):
            runtime_metadata(root, 'aarch64')

    def test_x86_loader_cannot_masquerade_as_arm64(self):
        root = self.root / 'runtime'
        write_runtime(root, machine=62)
        with self.assertRaisesRegex(RuntimeError, 'native 64-bit'):
            runtime_metadata(root, 'aarch64')

    def test_arm64_plan_keeps_sandbox_and_selects_fex(self):
        write_runtime(self.broker.runtime_root / 'hangover')
        wayland = self.root / 'wayland-0'
        wayland.touch()
        with patch('compatibility.wine.haven_compat.runtime.platform.machine', return_value='aarch64'), \
             patch('compatibility.wine.haven_compat.broker.shutil.which', return_value='/usr/bin/bwrap'), \
             patch.dict(os.environ, {'XDG_RUNTIME_DIR': str(self.root), 'WAYLAND_DISPLAY': 'wayland-0'}):
            plan = self.broker.plan(wine_manifest(self.exe, 'hangover'))
        self.assertIn('--unshare-all', plan.argv)
        self.assertIn('libarm64ecfex.dll', plan.argv)
        self.assertIn('libwow64fex.dll', plan.argv)
        self.assertNotIn('--share-net', plan.argv)
        self.assertEqual(plan.argv[-3:], ('/usr/bin/python3', '/opt/9to1-wine-launch.py', r'Z:\mnt\haven-share\program.exe'))

    def test_windows_guest_connection_has_no_password_or_shell(self):
        command = rdp_command({'host': '127.0.0.1', 'port': 33890, 'username': 'User Name'}, Path('/tmp/9to1-abc'), '/usr/bin/xfreerdp3')
        self.assertIn('/from-stdin:force', command)
        self.assertIn('/cert:tofu', command)
        self.assertIn('-clipboard', command)
        self.assertIn(r'/app:program:\\tsclient\NineToOne\program.exe', command)
        self.assertFalse(any(item.startswith('/p:') for item in command))
        self.assertNotIn('cmd.exe', ' '.join(command))

    def test_invalid_guest_connection_refused(self):
        for host in ('/p:password', 'host;command', 'host\nother'):
            with self.assertRaises(ValueError):
                validate_connection({'host': host, 'port': 3389, 'username': 'User'})
        with self.assertRaises(ValueError):
            validate_connection({'host': 'localhost', 'port': True, 'username': 'User'})

    def test_unsupported_legacy_arm_runtime_is_unavailable(self):
        root = self.broker.runtime_root / 'legacy'
        (root / 'bin').mkdir(parents=True)
        (root / 'bin/wine').touch(mode=0o755)
        with patch('compatibility.wine.haven_compat.runtime.platform.machine', return_value='aarch64'):
            result = choices(self.exe, {}, self.broker)
        self.assertFalse(result['wine']['available'])
        self.assertIn('ARM64', result['wine']['reason'])


if __name__ == '__main__':
    unittest.main()
