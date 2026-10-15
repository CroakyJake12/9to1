#!/usr/bin/env python3
"""Create a separate GPU-enabled DTB using dtc tools; never change firmware."""
import argparse
import hashlib
import json
import shutil
import struct
import subprocess
from pathlib import Path

EXPECTED_SOURCE = '23ffc8a4fb10d80143c966e791bee28aada6f7b90afb0578f1c53ab7121f4714'
GPU = '/soc@0/gpu@3d00000'


def properties(data):
    header = struct.unpack_from('>10I', data)
    magic, total, off_struct, off_strings, off_mem, version, compatible, cpu, string_size, struct_size = header
    if magic != 0xd00dfeed or total > len(data) or version != 17:
        raise ValueError('Unexpected FDT header')
    strings = data[off_strings:off_strings + string_size]
    pos, stack, result = off_struct, [], {}
    while pos < off_struct + struct_size:
        token = struct.unpack_from('>I', data, pos)[0]
        pos += 4
        if token == 1:
            end = data.index(b'\0', pos)
            stack.append(data[pos:end].decode())
            pos = (end + 4) & ~3
        elif token == 2:
            stack.pop()
        elif token == 3:
            size, nameoff = struct.unpack_from('>2I', data, pos)
            pos += 8
            name = strings[nameoff:strings.index(b'\0', nameoff)].decode()
            key = ('/'.join(stack), name)
            if key in result or pos + size > off_struct + struct_size:
                raise ValueError('Malformed FDT property')
            result[key] = data[pos:pos + size]
            pos = (pos + size + 3) & ~3
        elif token == 4:
            pass
        elif token == 9:
            break
        else:
            raise ValueError('Unknown FDT token')
    reservations = []
    while True:
        address, size = struct.unpack_from('>2Q', data, off_mem)
        off_mem += 16
        if not address and not size:
            break
        reservations.append((address, size))
    return result, (compatible, cpu, reservations)


def create(source, output):
    old = source.read_bytes()
    if hashlib.sha256(old).hexdigest() != EXPECTED_SOURCE:
        raise ValueError('DTB is not from the verified matching kernel build')
    before, header = properties(old)
    if before[(GPU, 'status')] != b'disabled\0' or before[('/sound', 'status')] != b'disabled\0':
        raise ValueError('Unexpected GPU or sound state')
    if output.exists():
        raise ValueError('Refusing to overwrite a display test')
    shutil.copyfile(source, output)
    subprocess.run(['fdtput', '-t', 's', str(output), GPU, 'status', 'okay'], check=True)
    after, new_header = properties(output.read_bytes())
    expected = dict(before)
    expected[(GPU, 'status')] = b'okay\0'
    if after != expected or header != new_header:
        raise ValueError('Unexpected change beyond the GPU status property')
    report = {'originalDTBSHA256': EXPECTED_SOURCE,
              'displayDTBSHA256': hashlib.sha256(output.read_bytes()).hexdigest(),
              'onlyChangedProperty': GPU + '/status', 'newValue': 'okay',
              'soundStillDisabled': True, 'physicalDisplayVerified': False}
    output.with_name('display-test.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    create(args.source, args.output)
