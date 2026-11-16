"""Operator-pinned Python and executable source for a supervised attempt."""

from __future__ import annotations

from dataclasses import asdict, dataclass
import hashlib
import io
from pathlib import Path
import re
import stat
import sys
import zipfile

from .artifacts import CustodyError, _copy, _open_regular, checked_path, relative_parts

_SOURCE_BYTES = 1024 * 1024
_MODULE = re.compile(r"[A-Za-z_][A-Za-z_0-9]{0,63}")
_DISTRIBUTION = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}")
_SHA256 = re.compile(r"[a-f0-9]{64}")


def _checksum(value: str) -> bool:
    return type(value) is str and _SHA256.fullmatch(value) is not None


@dataclass(frozen=True, slots=True)
class PackagePin:
    distribution: str
    module: str
    version: str
    root_index: int


@dataclass(frozen=True, slots=True)
class RegisteredRuntime:
    executable: Path
    sha256: str
    dependency_roots: tuple[Path, ...]
    packages: tuple[PackagePin, ...]

    def verified_record(self, forbidden_roots: tuple[Path, ...]) -> dict:
        if not _checksum(self.sha256) or self.executable.suffix.lower() != ".exe":
            raise CustodyError("An exact Windows Python executable pin is required.")
        if any(self.executable.is_relative_to(excluded) for excluded in forbidden_roots):
            raise CustodyError("Python must be installed independently of the checkout and attempts.")
        with _open_regular(self.executable) as executable:
            digest, _ = _copy(executable, None, 128 * 1024 * 1024)
        if digest != self.sha256:
            raise CustodyError("The Python executable does not match its approved hash.")
        if any(self.executable.parent.glob("*._pth")):
            raise CustodyError("The registered installation may not override Python startup paths.")
        if (type(self.dependency_roots) is not tuple or len(self.dependency_roots) > 8
                or type(self.packages) is not tuple or len(self.packages) > 64):
            raise CustodyError("The operator runtime registry exceeds its bounds.")
        seen_roots = set()
        for root in self.dependency_roots:
            checked_path(root, directory=True)
            if (root.name.lower() != "site-packages" or root in seen_roots
                    or any(root.is_relative_to(excluded) for excluded in forbidden_roots)):
                raise CustodyError("The registered dependency directory is not an independent site-packages root.")
            seen_roots.add(root)
        seen_modules = set()
        for pin in self.packages:
            if (not isinstance(pin, PackagePin) or not _DISTRIBUTION.fullmatch(pin.distribution)
                    or not _MODULE.fullmatch(pin.module) or pin.module in seen_modules
                    or pin.module in sys.stdlib_module_names
                    or pin.module in {"sitecustomize", "usercustomize"}
                    or not pin.version or len(pin.version) > 128
                    or type(pin.root_index) is not int or not 0 <= pin.root_index < len(self.dependency_roots)):
                raise CustodyError("A registered dependency pin is invalid.")
            seen_modules.add(pin.module)
        return {
            "python": str(self.executable), "python_sha256": digest,
            "dependency_roots": [str(root) for root in self.dependency_roots],
            "packages": [asdict(pin) for pin in self.packages],
        }


def _source_modules(entries: tuple[str, ...]) -> set[str]:
    if type(entries) is not tuple or not 0 < len(entries) <= 128:
        raise CustodyError("A bounded source archive manifest is required.")
    modules = set()
    for entry in entries:
        if type(entry) is not str or len(entry) > 240:
            raise CustodyError("A bounded source archive entry is required.")
        *parents, filename = entry.split("/")
        if not filename.endswith(".py") or any(not _MODULE.fullmatch(part) for part in (*parents, filename[:-3])):
            raise CustodyError("Source archives may only contain Python modules.")
        for index in range(len(parents)):
            init = "/".join((*parents[:index + 1], "__init__.py"))
            if init not in entries:
                raise CustodyError("Source archive packages require their own initializers.")
        modules.add((parents or [filename[:-3]])[0])
    if len({entry.casefold() for entry in entries}) != len(entries):
        raise CustodyError("Source archive entries must be unique.")
    return modules


@dataclass(frozen=True, slots=True)
class RegisteredWorker:
    relative_bundle: str
    sha256: str
    entry_module: str
    source_entries: tuple[str, ...]

    def verified_bundle(self, root: Path, reserved_modules: set[str]) -> bytes:
        modules = _source_modules(self.source_entries)
        forbidden = sys.stdlib_module_names | reserved_modules | {"sitecustomize", "usercustomize"}
        if modules & forbidden or self.entry_module.replace(".", "/") + ".py" not in self.source_entries:
            raise CustodyError("The worker entry or a source archive import conflicts with the runtime.")
        source = root.joinpath(*relative_parts(self.relative_bundle))
        with _open_regular(source) as stream:
            data = stream.read(_SOURCE_BYTES + 1)
        if (not _checksum(self.sha256) or len(data) > _SOURCE_BYTES
                or hashlib.sha256(data).hexdigest() != self.sha256):
            raise CustodyError("The worker source archive does not match its approved hash.")
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                files = archive.infolist()
                if (len(files) != len(self.source_entries)
                        or {entry.filename for entry in files} != set(self.source_entries)
                        or sum(entry.file_size for entry in files) > _SOURCE_BYTES
                        or any(stat.S_IFMT(entry.external_attr >> 16) not in {0, stat.S_IFREG}
                               for entry in files)):
                    raise CustodyError("The ZIP source archive does not match its approved manifest.")
                if archive.testzip() is not None:
                    raise CustodyError("The ZIP source archive is damaged.")
        except (zipfile.BadZipFile, NotImplementedError, RuntimeError) as exc:
            raise CustodyError("The registered worker must be a readable ZIP source archive.") from exc
        return data


# This fixed stdlib bootstrap runs inside the owned Job. In particular, neither
# importing dependencies nor adding their search roots happens before the barrier.
BOOTSTRAP = r"""
import sys
if sys.stdin.buffer.read(1) != b'G':
    raise SystemExit(125)

if not (sys.flags.isolated and sys.flags.no_site and sys.flags.dont_write_bytecode):
    raise RuntimeError('Required Python isolation flags are missing.')
import nt
base = sys.base_prefix.rstrip('\\/').casefold()
exe_directory = sys.executable.replace('/', '\\').rsplit('\\', 1)[0].casefold()
stdlib_zip = 'python' + str(sys.version_info.major) + str(sys.version_info.minor) + '.zip'
startup_paths = {base, exe_directory, base + '\\lib', base + '\\dlls', base + '\\' + stdlib_zip}
if any(nt._getfullpathname(path).rstrip('\\/').casefold() not in startup_paths or not path
       for path in sys.path):
    raise RuntimeError('Unregistered Python startup import path.')

import hashlib
import importlib.metadata
import importlib.util
import json
import os
from pathlib import Path
import re
import runpy

control, runtime_hash, bundle_name, bundle_hash, entry, request, pause = sys.argv[1:]
with Path(control).open('rb') as stream:
    runtime_bytes = stream.read(1024 * 1024 + 1)
if len(runtime_bytes) > 1024 * 1024 or hashlib.sha256(runtime_bytes).hexdigest() != runtime_hash:
    raise RuntimeError('The attempt runtime does not match its approved record.')
runtime = json.loads(runtime_bytes)
bundle = Path(bundle_name)
python = Path(sys.executable)
if python != Path(runtime['python']):
    raise RuntimeError('The running Python is different from the registered executable.')
with python.open('rb') as stream:
    if hashlib.file_digest(stream, 'sha256').hexdigest() != runtime['python_sha256']:
        raise RuntimeError('The running Python does not match its approved hash.')
with bundle.open('rb') as stream:
    if hashlib.file_digest(stream, 'sha256').hexdigest() != bundle_hash:
        raise RuntimeError('The attempt source archive does not match its approved hash.')

roots = [Path(root).resolve(strict=True) for root in runtime['dependency_roots']]
stdlib_count = len(sys.path)
sys.path.extend(str(root) for root in roots)
def normalize_distribution(name):
    return re.sub(r'[-_.]+', '-', name).lower()

for pin in runtime['packages']:
    root = roots[pin['root_index']]
    canonical_name = normalize_distribution(pin['distribution'])
    candidates = [
        item for item in importlib.metadata.distributions(path=[str(root)])
        if item.name and normalize_distribution(item.name) == canonical_name
    ]
    spec = importlib.util.find_spec(pin['module'])
    if (len(candidates) != 1 or candidates[0].version != pin['version']
            or importlib.metadata.version(pin['distribution']) != pin['version']
            or spec is None or not spec.origin
            or not Path(spec.origin).resolve(strict=True).is_relative_to(root)):
        raise RuntimeError('Registered package identity mismatch: ' + pin['module'])

sys.path.insert(stdlib_count, str(bundle))
spec = importlib.util.find_spec(entry.split('.', 1)[0])
if spec is None or not spec.origin or not Path(spec.origin).is_relative_to(bundle):
    raise RuntimeError('The worker entry does not load from the attempt source archive.')
sys.argv = [entry, request, pause]
cpu_only = runtime['device_uuid'] is None
expected_mask = '-1' if cpu_only else runtime['device_uuid']
if os.environ.get('CUDA_VISIBLE_DEVICES') != expected_mask:
    raise RuntimeError('The process GPU visibility differs from its admission.')
registered_torch = any(pin['module'] == 'torch' for pin in runtime['packages'])
if cpu_only and not registered_torch:
    class UnregisteredTorch:
        def find_spec(self, fullname, path, target=None):
            if fullname == 'torch':
                raise ModuleNotFoundError('Torch is not registered for this CPU worker.', name=fullname)
    sys.meta_path.insert(0, UnregisteredTorch())
if cpu_only and registered_torch:
    import torch

    # Match the standalone HF CPU test contract without importing its backend.
    def refuse_cuda_init(*args, **kwargs):
        refuse_cuda_init.dulche_guard_calls += 1
        raise RuntimeError('CUDA initialization is forbidden in this CPU worker.')

    refuse_cuda_init.dulche_guard_calls = 0

    def cpu_receipt(name):
        initialized = torch.cuda.is_initialized()
        refusals = refuse_cuda_init.dulche_guard_calls
        path = Path(control).with_name(name)
        with path.open('x', encoding='utf-8') as output:
            json.dump({'schema': 1, 'cuda_mask': os.environ.get('CUDA_VISIBLE_DEVICES'),
                       'cuda_initialized': initialized, 'init_refusals': refusals}, output)
            output.flush()
            os.fsync(output.fileno())
        if initialized or refusals or os.environ.get('CUDA_VISIBLE_DEVICES') != '-1':
            raise RuntimeError('The CPU execution requires masked, uninitialized CUDA.')

    cpu_receipt('cpu-runtime-start.json')
    if hasattr(torch._C, '_cuda_init'):
        torch._C._cuda_init = refuse_cuda_init
    try:
        torch.set_default_device('cpu')
        runpy.run_module(entry, run_name='__main__', alter_sys=True)
    finally:
        cpu_receipt('cpu-runtime-finish.json')
else:
    runpy.run_module(entry, run_name='__main__', alter_sys=True)
"""
