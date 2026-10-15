#!/usr/bin/env python3
"""Check management packet handling and the no-overwrite rule without hardware."""
import contextlib
import importlib.util
import io
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('address', Path(__file__).with_name('runtime') / 'bluetooth-address.py')
address = importlib.util.module_from_spec(spec)
spec.loader.exec_module(address)


class Transport:
    def __init__(self, packets):
        self.packets, self.sent = list(packets), []

    def sendall(self, packet):
        self.sent.append(packet)

    def settimeout(self, _):
        pass

    def recv(self, _):
        return self.packets.pop(0)


def response(command, payload=b'', status=0, index=0):
    data = struct.pack('<HB', command, status) + payload
    return struct.pack('<HHH', 1, index, len(data)) + data


class AddressTests(unittest.TestCase):
    def setUp(self):
        self.output = contextlib.redirect_stdout(io.StringIO())
        self.output.__enter__()
        self.addCleanup(self.output.__exit__, None, None, None)

    def test_missing_address_restores_only_original_bytes(self):
        original = bytes.fromhex('665544332202')
        transport = Transport([response(address.READ_CONFIG, struct.pack('<HII', 29, 2, 2)),
                               response(address.SET_PUBLIC_ADDRESS, struct.pack('<I', 0))])
        self.assertEqual(address.configure(address.Management(transport), original), 'restored')
        self.assertEqual(transport.sent[-1], struct.pack('<HHH', 0x0039, 0, 6) + original)

    def test_existing_different_address_is_never_overwritten(self):
        transport = Transport([response(address.READ_CONFIG, struct.pack('<HII', 29, 2, 0)),
                               response(address.READ_INFO, bytes.fromhex('665544332204'))])
        self.assertEqual(address.configure(address.Management(transport), bytes.fromhex('665544332202')), 'configured')
        self.assertEqual([struct.unpack_from('<H', x)[0] for x in transport.sent], [0x0037, 0x0004])

    def test_other_missing_configuration_does_not_write(self):
        transport = Transport([response(address.READ_CONFIG, struct.pack('<HII', 29, 3, 3))])
        with self.assertRaises(address.ManagementError):
            address.configure(address.Management(transport), b'\x02' * 6)
        self.assertEqual(len(transport.sent), 1)

    def test_controller_error_propagates_without_write(self):
        transport = Transport([response(address.READ_CONFIG, status=0x11)])
        with self.assertRaisesRegex(address.ManagementError, 'status 0x11'):
            address.configure(address.Management(transport), b'\x02' * 6)
        self.assertEqual(len(transport.sent), 1)

    def test_async_events_and_other_controller_replies_are_ignored(self):
        transport = Transport([struct.pack('<HHH', 4, 0, 0),
                               response(address.READ_CONFIG, b'other', index=1),
                               response(address.READ_CONFIG, b'expected')])
        self.assertEqual(address.Management(transport).request(address.READ_CONFIG), b'expected')

    def test_truncated_response_fails(self):
        transport = Transport([response(address.READ_CONFIG, b'bad')[:-1]])
        with self.assertRaises(address.ManagementError):
            address.Management(transport).request(address.READ_CONFIG)


if __name__ == '__main__':
    unittest.main()
