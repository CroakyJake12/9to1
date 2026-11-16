"""Fake QMP transport only; never sends a physical device_add to QEMU."""
import unittest
from unittest.mock import Mock
import qmp_attach as qmp


class AttachmentTests(unittest.TestCase):
    def test_exact_device_scope_and_no_second_attempt(self):
        client = Mock()
        client.execute.return_value = {'return': {}, 'id': 'dulche-v100-attach-once'}
        adapter = qmp.V100Attachment(client)
        adapter.attach_async(); adapter.thread.join(1)
        self.assertEqual(adapter.poll(), 'attached')
        request = client.execute.call_args.args[0]
        self.assertEqual(request['arguments'], {'driver':'vfio-pci','host':'0000:05:00.0',
                         'id':'v100','bus':'gpuroot','rombar':0})
        with self.assertRaises(qmp.Refusal): adapter.attach_async()
        self.assertEqual(client.execute.call_count, 1)

    def test_connection_failure_is_terminal_without_retry(self):
        client = Mock(); client.execute.side_effect = TimeoutError('fixture')
        adapter = qmp.V100Attachment(client)
        adapter.attach_async(); adapter.thread.join(1)
        self.assertEqual(adapter.poll(), 'failed')
        self.assertIn('TimeoutError', adapter.error)
        with self.assertRaises(qmp.Refusal): adapter.attach_async()
        self.assertEqual(client.execute.call_count, 1)


if __name__ == '__main__': unittest.main()
