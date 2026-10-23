#!/usr/bin/env python3
"""Root-owned affected-target regression, NOT a production dependency or activation tool.

After source QA and SDK/capacity admission, Root alone may run:
  python3 -B eng/build-patches/kokorosharp/verify_content_copy.py \
    --dotnet /actual/guarded/dotnet --stock-target /pinned/cache/buildTransitive/KokoroSharp.targets \
    --evidence /new/private/owning-evidence

The patch is for a NEW controlled 0.6.7 package extraction; retain the original package,
content, licences and cache. Pin actual archive+target+patch hashes and regenerate/verify
production NuGet imports before claiming activation. No project pipeline hook is added here.
Python is used only by this regression. The shipping MSBuild target has no Python dependency.
Linux inotify supplies actual asset-output write observation, in addition to diagnostic task logs.
Other platforms need their own owning no-write observation. Native/release acceptance is separate.
Inputs must be exclusively owned regular files: task path checks reject links/reparse/device
attributes and overlapping roots, but are not a portable inode/hardlink or FIFO detector and
cannot exclude concurrent native writers. Controlled ZIP extraction/admission must establish
those facts. Posthash detects persistent mutation; it cannot make copying a filesystem transaction.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys

STOCK_SHA = '51315994b68d354a04a35e068f29ee0aad4b04fc6703a55c649b4ac4c2c5ffaa'
FLOOR = 268435456


def apply_patch(before, patch):
    lines = before.splitlines(keepends=True)
    result, cursor, index = [], 0, 2
    parts = patch.splitlines(keepends=True)
    while index < len(parts):
        match = re.fullmatch(r'@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@[^\n]*\n', parts[index])
        if not match:
            raise ValueError('Unexpected donor patch hunk')
        start = int(match[1]) - 1
        if start < cursor:
            raise ValueError('Overlapping donor patch hunks')
        result.extend(lines[cursor:start]); cursor = start; index += 1
        consumed = produced = 0
        while index < len(parts) and not parts[index].startswith('@@ '):
            operation, value = parts[index][0], parts[index][1:]
            if operation not in ' +-':
                raise ValueError('Unsupported donor patch line')
            if operation in ' -':
                if cursor >= len(lines) or lines[cursor] != value:
                    raise ValueError('Donor target preimage differs')
                cursor += 1; consumed += 1
            if operation in ' +':
                result.append(value); produced += 1
            index += 1
        if (consumed, produced) != (int(match[2] or 1), int(match[4] or 1)):
            raise ValueError('Donor patch count differs')
    result.extend(lines[cursor:])
    return ''.join(result)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reserve():
    for path in ('/workspace', '/tmp'):
        if Path(path).exists():
            stat = os.statvfs(path)
            if stat.f_bavail * stat.f_frsize < FLOOR + 1048576:
                raise RuntimeError('Capacity floor/headroom refused; no SDK admitted')


class Writes:
    def __init__(self, root):
        if not sys.platform.startswith('linux'):
            raise RuntimeError('No-write observation requires Linux inotify; do not substitute mtime')
        self.libc = ctypes.CDLL(None, use_errno=True)
        self.fd = self.libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
        if self.fd < 0:
            raise OSError(ctypes.get_errno(), 'inotify_init1')
        # MODIFY, ATTRIB, CLOSE_WRITE, MOVED_FROM/TO, CREATE, DELETE and overflow.
        self.mask = 0x2 | 0x4 | 0x8 | 0x40 | 0x80 | 0x100 | 0x200 | 0x4000
        try:
            for path in [root, *(p for p in root.rglob('*') if p.is_dir())]:
                if self.libc.inotify_add_watch(self.fd, os.fsencode(path), self.mask) < 0:
                    raise OSError(ctypes.get_errno(), 'inotify_add_watch')
        except BaseException:
            self.close(); raise

    def close(self):
        if self.fd >= 0:
            os.close(self.fd); self.fd = -1

    def events(self):
        result = []
        while True:
            try:
                data = os.read(self.fd, 65536)
            except BlockingIOError:
                return result
            cursor = 0
            while cursor < len(data):
                watch, mask, cookie, size = struct.unpack_from('iIII', data, cursor)
                name = data[cursor + 16:cursor + 16 + size].rstrip(b'\0').decode(errors='replace')
                result.append(dict(watch=watch, mask=mask, cookie=cookie, name=name))
                cursor += 16 + size


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--stock-target', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    reserve()
    stock = args.stock_target.read_bytes()
    if len(stock) != 328 or hashlib.sha256(stock).hexdigest() != STOCK_SHA:
        raise RuntimeError('Pinned original target changed; preserve it and refuse')
    here = Path(__file__).resolve().parent
    donor_patch = (here / '0.6.7-content-sha256.patch').read_text()
    patched = apply_patch(stock.decode(), donor_patch)
    args.evidence.mkdir(parents=True, exist_ok=False)
    evidence = args.evidence.resolve()
    content = evidence / 'package/content'; content.mkdir(parents=True)
    (content / 'nested').mkdir()
    for relative, value in [('a.bin', b'alpha'), ('nested/voice.bin', b'voice-data'), ('LICENSE', b'fixture-only licence')]:
        (content / relative).write_bytes(value)
    target = evidence / 'package/buildTransitive/KokoroSharp.targets'
    target.parent.mkdir(); target.write_text(stock.decode().replace('<Copy ', '<Copy SkipUnchangedFiles="true" ', 1))
    output = evidence / 'output'; output.mkdir()
    outside = evidence / 'outside.bin'; outside.write_bytes(b'outside')
    rows = []

    def check(name, predicate, **detail):
        rows.append(dict(name=name, passed=bool(predicate), **detail))
        (evidence / 'results.json').write_text(json.dumps(rows, indent=2))
        if not predicate:
            raise AssertionError(name)

    def run(name, *, succeeds=True, entry='Build', output_root=None, expected_text=None):
        if not succeeds and not expected_text:
            raise ValueError('Negative regression requires its intended diagnostic')
        reserve()
        command = [str(args.dotnet.resolve()), 'msbuild', str(here / 'ContentCopyRegression.proj'),
                   '-nologo', '-m:1', '-nodeReuse:false', '-verbosity:diagnostic', '-target:' + entry,
                   '-p:KokoroTargets=' + str(target), '-p:OutputPath=' + str(output_root or output) + os.sep,
                   '-p:FixtureContent=' + str(content), '-p:FixtureOutside=' + str(outside)]
        process = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (evidence / (name + '.log')).write_text(process.stdout)
        check(name, (process.returncode == 0) == succeeds and (expected_text is None or expected_text in process.stdout),
              command=command, exit=process.returncode, expected_success=succeeds, expected_text=expected_text, log_sha256=hashlib.sha256(process.stdout.encode()).hexdigest())
        return process.stdout

    def current_assets_match():
        return all((output / path.relative_to(content)).is_file() and digest(path) == digest(output / path.relative_to(content))
                   for path in content.rglob('*') if path.is_file())

    def copy_started(log):
        # A skipped-task diagnostic is not task execution. Establish this observer on a real copy.
        return bool(re.search(r'Task "Copy"\s*\(TaskId:\d+\)', log))

    baseline_log = run('baseline-first')
    check('copy-task-observer-positive-control', copy_started(baseline_log))
    item = output / 'a.bin'; before = item.stat()
    item.write_bytes(b'XXXXX'); os.utime(item, ns=(before.st_atime_ns, before.st_mtime_ns))
    check('baseline-equal-metadata', item.stat().st_size == before.st_size and item.stat().st_mtime_ns == before.st_mtime_ns)
    run('baseline-timestamp-skip-red')
    check('baseline-demonstrates-corruption-not-repaired', digest(item) != digest(content / 'a.bin'))
    target.write_text(patched)
    run('hash-gate-repairs-equal-metadata-green'); check('all-current-assets-exact', current_assets_match())
    watcher = Writes(output)
    try:
        log = run('identical-repeat-no-copy')
        events = watcher.events()
    finally:
        watcher.close()
    check('actual-no-output-write-events', not events, observed_events=events)
    check('actual-zero-copy-task', 'KOKORO_HASH_GATE phase=Select candidates=0' in log and not copy_started(log))
    check('identical-all-assets-still-exact', current_assets_match())
    original = (content / 'a.bin').stat()
    (content / 'a.bin').write_bytes(b'bravo'); os.utime(content / 'a.bin', ns=(original.st_atime_ns, original.st_mtime_ns))
    check('changed-source-metadata-preserved', (content / 'a.bin').stat().st_size == original.st_size and (content / 'a.bin').stat().st_mtime_ns == original.st_mtime_ns)
    run('changed-source-same-size-time'); check('changed-source-exact', current_assets_match())
    (output / 'nested/voice.bin').unlink(); run('missing-partial-output-repair'); check('partial-repaired', current_assets_match())
    (content / 'new.bin').write_bytes(b'new'); run('new-source-copy'); check('new-copied', current_assets_match())
    (content / 'new.bin').unlink(); run('source-removal-retains-old-output'); check('removed-source-policy', (output / 'new.bin').read_bytes() == b'new' and current_assets_match())
    run('reject-duplicate', succeeds=False, entry='RejectDuplicate', expected_text='Duplicate Kokoro destination')
    run('reject-source-escape', succeeds=False, entry='RejectEscape', expected_text='Kokoro source escapes its admitted root')
    run('reject-source-output-alias', succeeds=False, output_root=content, expected_text='Kokoro source/output roots must not alias or overlap')
    (content / 'linked.bin').symlink_to(content / 'a.bin')
    run('reject-source-symlink', succeeds=False, expected_text='Kokoro copy refuses link/device path'); (content / 'linked.bin').unlink()
    item.unlink(); item.symlink_to(outside)
    run('reject-destination-symlink', succeeds=False, expected_text='Kokoro copy refuses link/device path'); item.unlink()
    run('restore-valid-after-rejections'); check('restored-exact', current_assets_match())
    run('reject-persistent-source-mutation', succeeds=False, entry='RejectMutation', expected_text='Kokoro copy/posthash failed or source changed during Build')
    check('mutation-retained-five-byte-length', (content / 'a.bin').stat().st_size == 5)
    print(json.dumps(dict(status='AFFECTED_TARGET_ONLY', checks=len(rows), evidence=str(evidence),
                          stock_target_sha256=STOCK_SHA, donor_patch_sha256=hashlib.sha256(donor_patch.encode()).hexdigest(),
                          production_import_activation='NOT VERIFIED', compile_product_native_release='NOT RUN')))


if __name__ == '__main__':
    main()
