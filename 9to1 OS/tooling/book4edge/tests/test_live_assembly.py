import importlib.util
import io
import stat
import tarfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("live", Path(__file__).parents[1] / "assemble-live.py")
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)


class LiveAssemblyTests(unittest.TestCase):
    def test_cpio_roundtrip_preserves_linux_modes_and_symlinks(self):
        entries = live.add_parents({
            "usr/lib/modules/test/driver.ko": (stat.S_IFREG | 0o644, b"module\0payload"),
            "scripts/casper-bottom/hook": (stat.S_IFREG | 0o755, b"#!/bin/sh\n"),
            "usr/bin/tool": (stat.S_IFLNK | 0o777, b"busybox"),
        })
        encoded = live.cpio(entries)
        decoded, end = live.read_cpio(encoded)
        self.assertEqual(entries, decoded)
        self.assertEqual(len(encoded), end)
        self.assertEqual(end % 4, 0)

    def test_rejects_path_traversal_and_windows_paths(self):
        for name in ("/etc/passwd", "../../outside", "safe/../outside", "C:\\Windows\\file"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                live.safe_name(name)

    def test_rejects_truncated_cpio(self):
        archive = live.cpio({"file": (stat.S_IFREG | 0o644, b"longer body")})
        with self.assertRaises(ValueError):
            live.read_cpio(archive[:120])

    def test_ar_rejects_truncated_members(self):
        with self.assertRaises(ValueError):
            live.deb_members(b"!<arch>\n" + b"data.tar".ljust(58) + b"`\n")

    def test_module_package_requires_matching_release_and_dependencies(self):
        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w") as archive:
            info = tarfile.TarInfo("lib/modules/7.0.0-wrong/modules.dep")
            info.size = 1
            archive.addfile(info, io.BytesIO(b"\n"))
        def member(name, data):
            header = f"{name + '/':<16}{0:<12}{0:<6}{0:<6}{0o644:<8}{len(data):<10}`\n".encode()
            return header + data + b"\n" * (len(data) % 2)
        deb = b"!<arch>\n" + member("debian-binary", b"2.0\n") + member("data.tar", payload.getvalue())
        with self.assertRaisesRegex(ValueError, "matching modules.dep"):
            live.module_entries(deb)


if __name__ == "__main__":
    unittest.main()
