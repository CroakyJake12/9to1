"""Bounded exact Sites restore-metadata observations; diagnostic only, never a new baseline."""
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import time

SITES_PROJECT = '9to1 Workspace/Sites/HavenOS.Sites.csproj'
CONTENT_CAP = 8 * 1024 * 1024
RECORD_CAP = 1024 * 1024
MAX_OPERATIONS = 256
MAX_CONTENTS = 256

def add_failure(failures, error):
    if all(error is not present for present in failures):
        failures.append(error)

def settle(failures):
    if len(failures) == 1:
        raise failures[0]
    if failures:
        raise BaseExceptionGroup('Original operation and independent metadata observation failures.', failures)

def identity(value):
    return {'device': value.st_dev, 'inode': value.st_ino, 'uid': value.st_uid,
            'gid': value.st_gid, 'mode': stat.S_IMODE(value.st_mode),
            'size': value.st_size, 'mtimeNs': value.st_mtime_ns, 'ctimeNs': value.st_ctime_ns}

def directory_identity(value):
    return {'device': value.st_dev, 'inode': value.st_ino, 'uid': value.st_uid,
            'gid': value.st_gid, 'mode': stat.S_IMODE(value.st_mode)}

def through_direct_parent(path, callback):
    """Bind each original directory edge; leaf work uses its retained parent fd."""
    path = Path(path)
    if not path.is_absolute() or '..' in path.parts or not path.name:
        raise ValueError('Exact metadata path is not a direct absolute leaf')
    handles = []
    witnesses = []
    failures = []
    returned = None

    def verify():
        for index, witness in enumerate(witnesses):
            current = os.fstat(witness['descriptor'])
            if not stat.S_ISDIR(current.st_mode) or directory_identity(current) != witness['identity']:
                raise ValueError('Original direct metadata directory handle changed')
            if index == 0:
                linked = os.stat('/', follow_symlinks=False)
            else:
                linked = os.stat(witness['name'], dir_fd=witness['parent'], follow_symlinks=False)
            if not stat.S_ISDIR(linked.st_mode) or directory_identity(linked) != witness['identity']:
                raise ValueError('Original direct metadata ancestor link changed or became indirect')

    try:
        parent = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        handles.append(parent)
        original = os.fstat(parent)
        witnesses.append({'descriptor': parent, 'identity': directory_identity(original), 'name': '/', 'parent': None})
        for name in path.parent.parts[1:]:
            linked = os.stat(name, dir_fd=parent, follow_symlinks=False)
            if not stat.S_ISDIR(linked.st_mode):
                raise ValueError('Original metadata ancestor is not a direct directory')
            child = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
            handles.append(child)
            opened = os.fstat(child)
            if not stat.S_ISDIR(opened.st_mode) or directory_identity(opened) != directory_identity(linked):
                raise ValueError('Original metadata ancestor changed while its handle was admitted')
            witnesses.append({'descriptor': child, 'identity': directory_identity(opened), 'name': name, 'parent': parent})
            parent = child
        verify()
        returned = callback(parent, path.name)
    except BaseException as error:
        add_failure(failures, error)
    try:
        verify()
    except BaseException as error:
        add_failure(failures, error)
    for descriptor in reversed(handles):
        try:
            os.close(descriptor)
        except BaseException as error:
            add_failure(failures, error)
    settle(failures)
    return returned

def write_exact(path, raw):
    def write_leaf(parent, name):
        descriptor = None
        written_identity = None
        failures = []
        try:
            descriptor = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC,
                                 0o400, dir_fd=parent)
            written_identity = directory_identity(os.fstat(descriptor))
            view = memoryview(raw)
            while view:
                count = os.write(descriptor, view)
                if count <= 0:
                    raise OSError('Exact metadata receipt write made no progress')
                view = view[count:]
            os.fsync(descriptor)
        except BaseException as error:
            add_failure(failures, error)
        if descriptor is not None:
            try:
                os.close(descriptor)
            except BaseException as error:
                add_failure(failures, error)
        read_descriptor = None
        try:
            read_descriptor = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
            first = os.fstat(read_descriptor)
            if not stat.S_ISREG(first.st_mode) or first.st_size != len(raw):
                raise ValueError('Exact retained metadata receipt size/type mismatch')
            if written_identity is not None and directory_identity(first) != written_identity:
                raise ValueError('Exact retained metadata receipt is not the SAME admitted output file')
            pieces = []
            length = 0
            while True:
                piece = os.read(read_descriptor, min(65536, len(raw) - length + 1))
                if not piece:
                    break
                pieces.append(piece)
                length += len(piece)
                if length > len(raw):
                    raise ValueError('Retained metadata receipt grew during readback')
            last = os.fstat(read_descriptor)
            present = os.stat(name, dir_fd=parent, follow_symlinks=False)
            if b''.join(pieces) != raw or identity(first) != identity(last) or identity(last) != identity(present):
                raise ValueError('Complete retained metadata receipt readback/identity mismatch')
        except BaseException as error:
            add_failure(failures, error)
        if read_descriptor is not None:
            try:
                os.close(read_descriptor)
            except BaseException as error:
                add_failure(failures, error)
        try:
            os.fsync(parent)
        except BaseException as error:
            add_failure(failures, error)
        settle(failures)
    return through_direct_parent(path, write_leaf)

class ExactSitesMetadata:
    def __init__(self, root, out, desktop_baseline):
        self.root = Path(root).resolve()
        if Path.cwd().resolve() != self.root:
            raise ValueError('Metadata producer cwd does not match original source root')
        out = Path(out)
        if out.resolve() != out or not out.is_relative_to(self.root):
            raise ValueError('Original metadata evidence output escapes source root or is indirect')
        self.output = out / 'forms-original-native-sites-metadata'
        through_direct_parent(self.output, lambda parent, name: os.mkdir(name, mode=0o700, dir_fd=parent))
        self.paths = []
        expected = {}
        projects = [row for row in desktop_baseline['projects'] if row['path'] == SITES_PROJECT]
        if not projects:
            raise ValueError('Sites metadata is absent from the SAME original Desktop baseline')
        for project in projects:
            output = Path(project['restoreOutputPath'])
            if output.is_absolute() or '..' in output.parts:
                raise ValueError('Original baseline metadata output escapes root')
            admitted = [row for row in project['metadata']
                        if Path(row['path']).name.endswith('.nuget.dgspec.json')
                        or Path(row['path']).name == 'project.nuget.cache']
            if len(admitted) != 2:
                raise ValueError('Expected complete Sites dgspec and cache baseline')
            for row in admitted:
                relative = Path(row['path'])
                if relative.is_absolute() or '..' in relative.parts or relative.parent != output:
                    raise ValueError('Metadata path does not match SAME evaluated baseline output')
                name = str(relative)
                if name in expected and expected[name] != row:
                    raise ValueError('Ambiguous original Sites metadata baseline')
                expected[name] = dict(row)
        self.paths = sorted(expected)
        self.expected = expected
        self.content_bytes = 0
        self.record_bytes = 0
        self.contents = {}
        self.sequence = 0
        before = self.capture()
        self.persist('0000-original-baseline', {
            'originalBaseline': [expected[name] for name in self.paths],
            'actualBeforeAnyNativePreparation': before,
            'qualification': 'Exact observed bytes and operation brackets; no reset, writer PID, SDK target or atomic/no-concurrent-writer claim.'
        })
        for row in before:
            original = expected[row['path']]
            if row['bytes'] != original['bytes'] or row['sha256'] != original['sha256']:
                raise ValueError('Actual Sites metadata already differs from original Desktop baseline')

    def read_exact(self, relative):
        path = self.root / relative
        if path.resolve() != path or not path.is_relative_to(self.root):
            raise ValueError('Original metadata path or parent became indirect')
        def read_leaf(parent, name):
            descriptor = None
            result = None
            failures = []
            try:
                descriptor = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
                before = os.fstat(descriptor)
                if not stat.S_ISREG(before.st_mode):
                    raise ValueError('Original metadata is not an ordinary file')
                if before.st_size > CONTENT_CAP:
                    raise ValueError('Whole metadata exceeds diagnostic content cap')
                pieces = []
                length = 0
                while True:
                    piece = os.read(descriptor, min(65536, before.st_size - length + 1))
                    if not piece:
                        break
                    pieces.append(piece)
                    length += len(piece)
                    if length > before.st_size:
                        raise ValueError('Original metadata changed length while being captured')
                after = os.fstat(descriptor)
                present = os.stat(name, dir_fd=parent, follow_symlinks=False)
                raw = b''.join(pieces)
                if identity(before) != identity(after) or identity(after) != identity(present) or len(raw) != before.st_size:
                    raise ValueError('Original metadata file identity or content witness changed during capture')
                digest = hashlib.sha256(raw).hexdigest()
                result = (raw, {'path': relative, 'bytes': len(raw), 'sha256': digest,
                                'identity': identity(after), 'capturedMonotonicNs': time.monotonic_ns()})
            except BaseException as error:
                add_failure(failures, error)
            if descriptor is not None:
                try:
                    os.close(descriptor)
                except BaseException as error:
                    add_failure(failures, error)
            settle(failures)
            return result
        return through_direct_parent(path, read_leaf)

    def capture(self):
        result = []
        for relative in self.paths:
            raw, row = self.read_exact(relative)
            key = row['sha256']
            content = self.output / (key + '.metadata')
            if key not in self.contents:
                if len(self.contents) >= MAX_CONTENTS or self.content_bytes + len(raw) > CONTENT_CAP:
                    raise ValueError('Complete diagnostic metadata content cap exceeded; no prefix retained')
                # Charge the whole attempted file and slot BEFORE creation. A failed
                # close/readback/fsync may leave evidence; its reservation is never reused.
                self.contents[key] = len(raw)
                self.content_bytes += len(raw)
                write_exact(content, raw)
            else:
                retained, _ = self.read_exact(str(content.relative_to(self.root)))
                if self.contents[key] != len(raw) or retained != raw:
                    raise ValueError('Previously retained whole metadata content changed')
            row['retainedContent'] = content.name
            result.append(row)
        return result

    def persist(self, name, value):
        raw = (json.dumps(value, indent=2, sort_keys=True) + '\n').encode()
        if self.record_bytes + len(raw) > RECORD_CAP:
            raise ValueError('Complete diagnostic operation record cap exceeded')
        # Preserve the charge even if creation/write/close/readback/fsync refuses.
        self.record_bytes += len(raw)
        write_exact(self.output / (name + '.json'), raw)

    def operation(self, label, argv, callback):
        if self.sequence >= MAX_OPERATIONS:
            raise ValueError('Complete metadata operation bracket cap exceeded')
        self.sequence += 1
        number = self.sequence
        record = {'operationId': number, 'label': label, 'argv': list(argv) if argv is not None else None,
                  'cwd': str(Path.cwd()), 'startedMonotonicNs': time.monotonic_ns(),
                  'exitCode': None, 'before': None, 'after': None, 'failures': []}
        failures = []
        returned = None
        admitted = False
        try:
            if Path.cwd().resolve() != self.root:
                raise ValueError('Metadata operation cwd changed')
            record['before'] = self.capture()
            admitted = True
            returned = callback()
            record['exitCode'] = returned if type(returned) is int else getattr(returned, 'returncode', None)
        except BaseException as error:
            add_failure(failures, error)
            if isinstance(error, subprocess.CalledProcessError):
                record['exitCode'] = error.returncode
        try:
            record['after'] = self.capture()
        except BaseException as error:
            add_failure(failures, error)
        record['admitted'] = admitted
        record['finishedMonotonicNs'] = None
        record['failureProjectionRefused'] = False
        try:
            record['finishedMonotonicNs'] = time.monotonic_ns()
        except BaseException as error:
            add_failure(failures, error)
        projected = []
        for original in tuple(failures):
            row = {'type': None, 'message': None}
            try:
                row['type'] = type(original).__name__
                row['message'] = str(original)
                if len(row['message']) > 4096:
                    row['message'] = None
                    raise ValueError('Complete diagnostic failure message exceeds cap; no prefix retained')
            except BaseException as error:
                record['failureProjectionRefused'] = True
                add_failure(failures, error)
            projected.append(row)
        record['failures'] = projected
        try:
            self.persist(format(number, '04d') + '-operation', record)
        except BaseException as error:
            add_failure(failures, error)
        settle(failures)
        return returned

    def query(self, label, argv):
        return self.operation(label, argv, lambda: subprocess.run(argv, capture_output=True, text=True))

    def guard(self, label, callback):
        return self.operation(label, None, callback)
