#!/usr/bin/env python3
"""Restore a missing radio address through Linux's Bluetooth management API.

Uses BlueZ's documented MGMT interface, not controller vendor commands. The
original address remains private. No pairing keys or persistent firmware change.
"""
import re
import socket
import struct
import sys
import time
from pathlib import Path

READ_INFO, READ_CONFIG, SET_PUBLIC_ADDRESS = 0x0004, 0x0037, 0x0039
PUBLIC_ADDRESS = 0x00000002


class ManagementError(RuntimeError):
    pass


class Management:
    def __init__(self, transport, index=0):
        self.transport = transport
        self.index = index

    def request(self, opcode, payload=b''):
        self.transport.sendall(struct.pack('<HHH', opcode, self.index, len(payload)) + payload)
        deadline = time.monotonic() + 4
        while time.monotonic() < deadline:
            self.transport.settimeout(max(0.01, deadline - time.monotonic()))
            packet = self.transport.recv(65535)
            if len(packet) < 6:
                raise ManagementError('Truncated Bluetooth management header')
            event, index, size = struct.unpack_from('<HHH', packet)
            if len(packet) != size + 6:
                raise ManagementError('Truncated Bluetooth management response')
            if index != self.index or event not in (1, 2):
                continue
            if size < 3:
                raise ManagementError('Incomplete Bluetooth command result')
            command, status = struct.unpack_from('<HB', packet, 6)
            if command != opcode:
                continue
            if status:
                raise ManagementError(f'Bluetooth command 0x{opcode:04x} returned status 0x{status:02x}')
            return packet[9:]
        raise TimeoutError('Bluetooth management response timed out')


def configure(management, original):
    config = management.request(READ_CONFIG)
    if len(config) != 10:
        raise ManagementError('Unexpected Bluetooth configuration response')
    _, supported, missing = struct.unpack('<HII', config)
    print(f'Radio configuration: supported=0x{supported:x}, missing=0x{missing:x}', flush=True)
    if missing & ~PUBLIC_ADDRESS:
        raise ManagementError('Radio requires configuration beyond its public address')
    if missing & PUBLIC_ADDRESS:
        if not supported & PUBLIC_ADDRESS:
            raise ManagementError('Controller does not support restoring its public address')
        management.request(SET_PUBLIC_ADDRESS, original)
        print('Restored this laptop original radio address for this boot.', flush=True)
        # Setting the address re-registers the controller. Verify after it settles.
        return 'restored'
    info = management.request(READ_INFO)
    if len(info) < 6 or info[:6] == b'\0' * 6:
        raise ManagementError('Configured controller has no usable address')
    if info[:6] == original:
        print('Original Bluetooth address is configured.', flush=True)
    else:
        print('Controller already has an address; leaving it unchanged.', flush=True)
    return 'configured'


def main():
    if 'book4edge.suite=1' not in Path('/proc/cmdline').read_text().split():
        raise ManagementError('Not the opted-in live driver test')
    if Path('/sys/class/dmi/id/board_name').read_text().strip() != 'NP960XMA-KB1UK':
        raise ManagementError('Unexpected laptop model')
    address = Path('/etc/book4edge/bluetooth-address').read_text().strip()
    if not re.fullmatch(r'(?:[0-9A-F]{2}:){5}[0-9A-F]{2}', address):
        raise ManagementError('Original radio address file is invalid')
    raw = bytes.fromhex(address.replace(':', ''))
    if raw in (b'\0' * 6, b'\xff' * 6) or raw[0] & 1:
        raise ManagementError('Original radio address is not unicast')
    original = raw[::-1]  # bdaddr_t uses Bluetooth's least-significant-byte order.
    deadline = time.monotonic() + 60
    last_error = 'Controller has not appeared'
    while time.monotonic() < deadline:
        device = Path('/sys/class/bluetooth/hci0/device')
        if device.exists() and 'serial' in str(device.resolve()):
            try:
                with socket.socket(socket.AF_BLUETOOTH, socket.SOCK_RAW, socket.BTPROTO_HCI) as control:
                    control.bind((0xffff, 3))  # HCI_DEV_NONE, HCI_CHANNEL_CONTROL
                    state = configure(Management(control), original)
                if state == 'configured':
                    return
            except (OSError, ManagementError) as error:
                last_error = str(error)
                print(last_error, flush=True)
        time.sleep(1)
    raise ManagementError('Bluetooth configuration did not complete: ' + last_error)


if __name__ == '__main__':
    try:
        main()
    except (OSError, ManagementError) as error:
        print(str(error), file=sys.stderr, flush=True)
        raise SystemExit(1)
