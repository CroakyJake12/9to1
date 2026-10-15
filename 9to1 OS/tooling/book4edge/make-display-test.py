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
DISPLAY = '/soc@0/display-subsystem@ae00000/displayport-controller@aea0000'
PANEL = DISPLAY + '/aux-bus/panel'
TLMM = '/soc@0/pinctrl@f100000'
TOUCH = TLMM + '/ts0-default-state'


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


def create(source, output, controller_hpd=False, touchscreen=False):
    old = source.read_bytes()
    if hashlib.sha256(old).hexdigest() != EXPECTED_SOURCE:
        raise ValueError('DTB is not from the verified matching kernel build')
    before, header = properties(old)
    if before[(GPU, 'status')] != b'disabled\0' or before[('/sound', 'status')] != b'disabled\0':
        raise ValueError('Unexpected GPU or sound state')
    if controller_hpd:
        tlmm = before[('/soc@0/pinctrl@f100000', 'phandle')]
        if before[(PANEL, 'hpd-gpios')] != tlmm + struct.pack('>II', 119, 0):
            raise ValueError('Unexpected panel HPD GPIO')
        state = '/soc@0/pinctrl@f100000/edp0-hpd-default-state'
        if (before[(DISPLAY, 'pinctrl-0')] != before[(state, 'phandle')]
                or before[(state, 'pins')] != b'gpio119\0'
                or before[(state, 'function')] != b'edp0_hot\0'):
            raise ValueError('Controller does not own the expected HPD input')
    if touchscreen:
        if not controller_hpd:
            raise ValueError('Touchscreen test requires the physically verified panel fix')
        if before[(TLMM, 'gpio-reserved-ranges')] != struct.pack('>4I', 34, 2, 44, 4):
            raise ValueError('Unexpected reserved GPIO ranges')
        reset = TOUCH + '/reset-n-pins'
        wanted = {'pins': b'gpio48\0', 'function': b'gpio\0',
                  'output-high': b'', 'drive-strength': struct.pack('>I', 16)}
        if {name: value for (node, name), value in before.items() if node == reset} != wanted:
            raise ValueError('Unexpected touchscreen reset configuration')
        irq = '/soc@0/geniqup@ac0000/i2c@a80000/touchscreen@5d'
        if (before[(irq, 'interrupts-extended')] != before[(TLMM, 'phandle')] + struct.pack('>II', 34, 8)
                or before[(irq, 'pinctrl-0')] != before[(TOUCH, 'phandle')]
                or before[(TOUCH + '/int-n-pins', 'pins')] != b'gpio34\0'):
            raise ValueError('Unexpected touchscreen interrupt wiring')
    if output.exists():
        raise ValueError('Refusing to overwrite a display test')
    shutil.copyfile(source, output)
    subprocess.run(['fdtput', '-t', 's', str(output), GPU, 'status', 'okay'], check=True)
    if controller_hpd:
        # Follow the newer Samsung X1E board's panel setup: avoid a duplicate
        # GPIO request and wait the driver's specified maximum 200 ms after
        # panel power-on. Retain the controller's dedicated edp0_hot input.
        subprocess.run(['fdtput', '-d', str(output), PANEL, 'hpd-gpios'], check=True)
        subprocess.run(['fdtput', str(output), PANEL, 'no-hpd'], check=True)
    if touchscreen:
        # Match the newer Samsung X1E tree: GPIO34 is an input, GPIO35 and
        # GPIO44-47 stay reserved. Do not activate the old GPIO48 output.
        subprocess.run(['fdtput', '-t', 'i', str(output), TLMM, 'gpio-reserved-ranges',
                        '35', '1', '44', '4'], check=True)
        subprocess.run(['fdtput', '-r', str(output), TOUCH + '/reset-n-pins'], check=True)
    after, new_header = properties(output.read_bytes())
    expected = dict(before)
    expected[(GPU, 'status')] = b'okay\0'
    if controller_hpd:
        del expected[(PANEL, 'hpd-gpios')]
        expected[(PANEL, 'no-hpd')] = b''
    if touchscreen:
        expected[(TLMM, 'gpio-reserved-ranges')] = struct.pack('>4I', 35, 1, 44, 4)
        expected = {key: value for key, value in expected.items() if key[0] != TOUCH + '/reset-n-pins'}
    if after != expected or header != new_header:
        raise ValueError('Unexpected change beyond the selected display properties')
    report = {'originalDTBSHA256': EXPECTED_SOURCE,
              'displayDTBSHA256': hashlib.sha256(output.read_bytes()).hexdigest(),
              'onlyChangedProperty': GPU + '/status', 'newValue': 'okay',
              'soundStillDisabled': True, 'physicalDisplayVerified': False}
    if controller_hpd:
        del report['onlyChangedProperty']
        report['changedProperties'] = {GPU + '/status': 'okay', PANEL + '/hpd-gpios': 'removed', PANEL + '/no-hpd': 'present'}
        report['controllerHpdRetained'] = True
        report['panelMaximumPowerOnDelayMs'] = 200
        report['panelConfigurationSource'] = 'https://github.com/n1c0la84/linux-book4-edge/blob/6221fc7393c8cd04554df24c6853f50d61d6ab03/arch/arm64/boot/dts/qcom/x1e-samsung-galaxy-book4-edge.dtsi'
        report['reason'] = 'Physical boot: panel GPIO119 request conflicts with controller edp0_hot ownership'
    if touchscreen:
        report['changedProperties'][TLMM + '/gpio-reserved-ranges'] = [35, 1, 44, 4]
        report['removedNodes'] = [TOUCH + '/reset-n-pins']
        report['physicalTouchscreenVerified'] = False
        report['touchscreenReason'] = 'Physical desktop boot: GPIO34 reserved, i2c_hid_of 0-005d probe fails with EINVAL'
    output.with_name('display-test.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--controller-hpd', action='store_true', help='Remove the panel duplicate GPIO119 request; keep native controller HPD')
    parser.add_argument('--touchscreen', action='store_true', help='Free only touchscreen GPIO34 and remove the obsolete GPIO48 output')
    args = parser.parse_args()
    create(args.source, args.output, args.controller_hpd, args.touchscreen)
