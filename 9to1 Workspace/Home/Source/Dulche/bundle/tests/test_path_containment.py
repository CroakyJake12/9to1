from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.identity import IdentityError, resolve_within  # noqa: E402


class PathContainmentTests(unittest.TestCase):
    def _root(self) -> pathlib.Path:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name) / "root"
        root.mkdir(parents=True)
        return root

    def test_relative_path_resolves_within_root(self) -> None:
        root = self._root()
        resolved = resolve_within(root, "assets/models/m.gguf")
        self.assertEqual((root / "assets/models/m.gguf").resolve(), resolved)

    def test_absolute_path_is_rejected(self) -> None:
        root = self._root()
        with self.assertRaises(IdentityError):
            resolve_within(root, "/etc/passwd")

    def test_dotdot_is_rejected(self) -> None:
        root = self._root()
        with self.assertRaises(IdentityError):
            resolve_within(root, "../escape")

    def test_symlink_escape_is_rejected(self) -> None:
        root = self._root()
        outside = root.parent / "outside"
        outside.mkdir(parents=True)
        target = outside / "secret.bin"
        target.write_bytes(b"secret")
        link = root / "evil"
        link.symlink_to(target)
        with self.assertRaises(IdentityError):
            resolve_within(root, "evil")

    def test_symlink_within_root_is_allowed(self) -> None:
        root = self._root()
        (root / "assets").mkdir()
        real = root / "assets" / "real.bin"
        real.write_bytes(b"data")
        link = (root / "assets").joinpath("link.bin")
        link.symlink_to(real)
        resolved = resolve_within(root, "assets/link.bin")
        self.assertEqual(real.resolve(), resolved)


if __name__ == "__main__":
    unittest.main()
