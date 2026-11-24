import os
import socket
from threading import Thread
from time import monotonic
import unittest

from .artifacts import CustodyError
from .v8_guest_rendezvous import relay_weight_channel


class GuestWeightRelayTests(unittest.TestCase):
    def test_real_unix_pipe_duplex_relay_holds_original_bytes_across_chunks(self):
        guest, relay = socket.socketpair()
        rx, producer = os.pipe()
        consumer, tx = os.pipe()
        sent = bytes(range(256)) * 1025
        errors = []

        def send():
            with os.fdopen(producer, "wb") as f:
                f.write(sent)

        def echo():
            with guest:
                while data := guest.recv(4096):
                    guest.sendall(data)
                guest.shutdown(socket.SHUT_WR)

        def bridge():
            try:
                with relay, os.fdopen(rx, "rb", buffering=0) as inbound, os.fdopen(tx, "wb", buffering=0) as outbound:
                    relay_weight_channel(relay, inbound, outbound, deadline=monotonic() + 3,
                                         still_owned=lambda: True)
            except BaseException as e:
                errors.append(e)

        threads = [Thread(target=f, daemon=True) for f in (send, echo, bridge)]
        for t in threads:
            t.start()
        with os.fdopen(consumer, "rb") as f:
            self.assertEqual(f.read(), sent)
        for t in threads:
            t.join(3)
            self.assertFalse(t.is_alive())
        self.assertEqual(errors, [])

    def test_revoked_relay_does_not_read_or_forward(self):
        guest, relay = socket.socketpair()
        rx, producer = os.pipe()
        consumer, tx = os.pipe()
        try:
            with guest, relay, os.fdopen(rx, "rb", buffering=0) as inbound, os.fdopen(
                    tx, "wb", buffering=0) as outbound:
                with self.assertRaisesRegex(CustodyError, "lost"):
                    relay_weight_channel(relay, inbound, outbound, deadline=monotonic() + 3,
                                         still_owned=lambda: False)
                self.assertTrue(os.get_blocking(inbound.fileno()))
                self.assertTrue(os.get_blocking(outbound.fileno()))
        finally:
            os.close(producer)
            os.close(consumer)
