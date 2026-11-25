"""Prepare a reproducible source ZIP for an operator to inspect and pin."""

import argparse
import hashlib
from pathlib import Path
import zipfile

from custody.artifacts import _open_regular, checked_path
from custody.integration_supervised_fixture import _ENTRIES, _WORKER

ENTRY = "checkpoint_fixture"
ENTRIES = tuple(entry for entry in _ENTRIES if entry != "custody/test_snapshot.py")
_CLI_FILES = (
    "worker_api/__init__.py", "worker_api/__main__.py", "worker_api/bridge.py",
    "worker_api/prepare_bundle.py", "worker_api/protocol.py", "worker_api/windows_host.py",
    "custody/__init__.py", "custody/artifacts.py", "custody/integration_supervised_fixture.py",
    "custody/provenance.py", "custody/registered_runtime.py", "custody/resources.py",
    "custody/supervisor.py", "custody/topology.py", "custody/windows_job.py",
)


def prepare_bundle(output: Path, *, cli: bool = False) -> str:
    checked_path(output.parent, directory=True)
    training = Path(__file__).resolve().parents[1]
    with zipfile.ZipFile(output, "x") as archive:
        for entry in _CLI_FILES if cli else ENTRIES[:-1]:
            with _open_regular(training / entry) as source:
                data = source.read(1024 * 1024 + 1)
            if len(data) > 1024 * 1024:
                raise ValueError("The reviewed source file exceeds the worker bundle limit.")
            archive.writestr(zipfile.ZipInfo(entry), data)
        if cli:
            archive.writestr(zipfile.ZipInfo("__main__.py"),
                             "from worker_api.__main__ import main\nraise SystemExit(main())\n")
        else:
            archive.writestr(zipfile.ZipInfo(ENTRIES[-1]), _WORKER)
    with _open_regular(output) as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cli", action="store_true")
    args = parser.parse_args()
    print(prepare_bundle(args.output, cli=args.cli))


if __name__ == "__main__":
    main()
