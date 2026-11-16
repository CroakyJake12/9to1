"""CPU-only preparation controls. No backend, GPU, VM or network is invoked."""
import copy
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import prepare


NOW = dt.datetime(2026, 11, 16, 21, 0, tzinfo=dt.timezone.utc)


def fixture():
    return {
        'schema': '9to1.dulche.linux-readonly/v1',
        'scope': 'READ_ONLY_NO_GPU_INITIALIZATION',
        'observed_at': NOW.isoformat(),
        'host': {'cmdline': '', 'nvidia_driver': 'NVIDIA Open Kernel Module',
                 'kvm_accessible': True, 'mem_available_bytes': 80 * prepare.GIB,
                 'storage_free_bytes': 200 * prepare.GIB},
        'iommu': {'irq_remapping_enabled': True, 'unsafe_noiommu': 'N',
                  'unsafe_interrupts': 'N'},
        'devices': [
            {'bdf': '0000:05:00.0', 'vendor': '0x10de', 'device': '0x1db4',
             'subsystem_vendor': '0x10de', 'subsystem_device': '0x1214',
             'driver': None, 'binding_state': 'unbound', 'inspection_errors': [],
             'driver_override': '(null)', 'iommu_group': '18',
             'group_members': ['0000:05:00.0'], 'reset_methods': ['bus'],
             'reserved_regions': '0x00000000fee00000 0x00000000feefffff msi',
             'reset_domain_members': ['0000:05:00.0'], 'upstream_bridge': '0000:00:1c.4'},
            {'bdf': '0000:01:00.0', 'vendor': '0x10de', 'device': '0x2c02',
             'driver': 'nvidia', 'boot_vga': '1', 'iommu_group': '14',
             'group_members': ['0000:01:00.0', '0000:01:00.1'],
             'reset_domain_members': ['0000:01:00.0', '0000:01:00.1'],
             'upstream_bridge': '0000:00:01.0'}],
        'errors': [],
    }


class PlanTests(unittest.TestCase):
    def plan(self, data=None):
        return prepare.make_plan(data or fixture(), {'verified': False}, NOW)

    def test_complete_looking_input_cannot_authorize_execution(self):
        data = fixture()
        data.update(execution_enabled=True, system_approved=True, allowance='unspent')
        result = prepare.make_plan(data, {'verified': True}, NOW)
        self.assertFalse(result['execution_enabled'])
        self.assertFalse(result['allowance_consumed_by_preparation'])
        self.assertIn('EXPLICIT_SYSTEM_CHANGE_APPROVAL_REQUIRED', result['blockers'])
        self.assertIn('INDEPENDENT_ACTUAL_GUEST_STOP_PATH_NOT_PROVEN', result['blockers'])
        self.assertEqual(result['status'], 'PREPARED_DISABLED')

    def test_singleton_group_does_not_hide_shared_bus_reset_domain(self):
        data = fixture()
        data['devices'][0]['reset_domain_members'].append('0000:05:01.0')
        self.assertIn('RESET_DOMAIN_HAS_OTHER_DEVICES', self.plan(data)['blockers'])

    def test_shared_group_is_not_admitted(self):
        data = fixture()
        data['devices'][0]['group_members'].append('0000:05:01.0')
        self.assertIn('V100_GROUP_NOT_EXCLUSIVE', self.plan(data)['blockers'])

    def test_null_driver_is_not_evidence_of_unbound(self):
        for change in ({'binding_state': 'unknown'}, {'inspection_errors': ['denied']},
                       {'driver': 'vfio-pci'}):
            with self.subTest(change=change):
                data = fixture()
                data['devices'][0].update(change)
                self.assertIn('V100_UNBOUND_STATE_UNCONFIRMED', self.plan(data)['blockers'])

    def test_duplicate_gpu_or_malformed_bdf_is_refused(self):
        data = fixture()
        data['devices'].append(copy.deepcopy(data['devices'][0]))
        with self.assertRaises(prepare.Refusal):
            self.plan(data)
        data = fixture()
        data['devices'][0]['bdf'] = '0000:05:00.0;reboot'
        with self.assertRaises(prepare.Refusal):
            self.plan(data)

    def test_missing_or_malformed_bridge_is_refused_for_either_gpu(self):
        for index in (0, 1):
            for value in (None, '', '00:1c.4', '0000:00:1c.4 extra'):
                data = fixture()
                data['devices'][index]['upstream_bridge'] = value
                with self.subTest(index=index, value=value), self.assertRaises(prepare.Refusal):
                    self.plan(data)

    def test_reserved_regions_allow_only_well_formed_msi_ranges(self):
        self.assertNotIn('RESERVED_REGIONS_REQUIRE_REVIEW', self.plan()['blockers'])
        for value in (None, '', '0x1 0x2 direct', '0x2 0x1 msi', '0x1 0x2 msi extra',
                      '0x1 0x2 msi\n0x3 0x4 direct-relaxable'):
            data = fixture()
            data['devices'][0]['reserved_regions'] = value
            with self.subTest(value=value):
                self.assertIn('RESERVED_REGIONS_REQUIRE_REVIEW', self.plan(data)['blockers'])

    def test_old_future_and_naive_inspections_do_not_pass(self):
        for offset in (-301, 1):
            data = fixture()
            data['observed_at'] = (NOW + dt.timedelta(seconds=offset)).isoformat()
            self.assertIn('INSPECTION_NOT_CURRENT', self.plan(data)['blockers'])
        data['observed_at'] = NOW.replace(tzinfo=None).isoformat()
        with self.assertRaises(prepare.Refusal):
            self.plan(data)

    def test_unsafe_and_unknown_parameters_are_not_treated_as_safe(self):
        for value in (None, 'Y', '1', False):
            data = fixture()
            data['iommu']['unsafe_interrupts'] = value
            self.assertIn('UNSAFE_INTERRUPTS_MUST_BE_VERIFIED_FALSE', self.plan(data)['blockers'])
        for flag in ('pcie_acs_override=downstream', 'intremap=off', 'intel_iommu=off'):
            data = fixture()
            data['host']['cmdline'] = flag
            self.assertIn('UNSAFE_KERNEL_OVERRIDE_PRESENT', self.plan(data)['blockers'])

    def test_resource_bounds_reject_missing_booleans_low_and_huge_values(self):
        for value in (None, True, 1, 10 ** 1000):
            data = fixture()
            data['host']['mem_available_bytes'] = value
            self.assertIn('HOST_MEMORY_BUDGET_INSUFFICIENT', self.plan(data)['blockers'])
        data = fixture()
        data['host']['storage_free_bytes'] = 31 * prepare.GIB
        self.assertIn('STORAGE_RESERVE_INSUFFICIENT', self.plan(data)['blockers'])

    def test_production_store_and_private_transport_are_preserved(self):
        result = self.plan()
        self.assertFalse(result['storage']['guest_rpc_tensor_cache'])
        self.assertIsNone(result['storage']['guest_model_filesystem'])
        self.assertEqual(result['transport']['guest_rpc']['listen'], '127.0.0.1')
        self.assertEqual(result['transport']['ssh']['strict_host_key_checking'], 'yes')
        self.assertIsNone(result['transport']['ssh']['required_host_key_fingerprint'])
        self.assertEqual(result['backend_lock']['upstream']['commit'], prepare.COMMIT)
        self.assertEqual(result['backend_lock']['upstream']['rpc_protocol'], '10.0.0')


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_corrected_model_identity_is_used(self):
        self.assertEqual(prepare.MODEL_SHA256,
                         '9fecc3b3cd76bba89d504f29b616eedf7da85b96540e490ca5824d3f7d2776a0')
        self.assertEqual(prepare.MODEL_BYTES, 668788096)
        self.assertFalse(prepare.model_evidence(None, None)['verified'])

    def test_json_rejects_nonfinite_duplicate_and_nonobject(self):
        for raw in (b'{"a":NaN}', b'{"a":1,"a":2}', b'[]'):
            with self.subTest(raw=raw), self.assertRaises(prepare.Refusal):
                prepare.parse_json(raw)

    def test_read_rejects_symlink_fifo_and_oversized_inputs(self):
        source = self.root / 'data'
        source.write_bytes(b'abc')
        link = self.root / 'link'
        link.symlink_to(source)
        with self.assertRaises(OSError):
            prepare.read_regular_bytes(link)
        with self.assertRaises(prepare.Refusal):
            prepare.read_regular_bytes(source, 2)
        fifo = self.root / 'fifo'
        os.mkfifo(fifo)
        with self.assertRaises(prepare.Refusal):
            prepare.read_regular_bytes(fifo)

    def test_hash_is_bounded_and_wrong_size_is_refused(self):
        path = self.root / 'model'
        path.write_bytes(b'abc')
        self.assertEqual(prepare.hash_regular_file(path, 3), hashlib.sha256(b'abc').hexdigest())
        with self.assertRaises(prepare.Refusal):
            prepare.hash_regular_file(path, 4)

    def test_receipt_and_actual_bytes_must_match_and_original_must_exist(self):
        model, receipt, original = (self.root / x for x in ('model', 'receipt', 'original'))
        model.write_bytes(b'abc')
        original.write_bytes(b'original evidence fixture')
        digest = hashlib.sha256(b'abc').hexdigest()
        entry = {'bytes': 3, 'sha256': digest, 'source_receipt_path': str(original),
                 'source_receipt_sha256': hashlib.sha256(original.read_bytes()).hexdigest()}
        receipt.write_text(json.dumps(entry))
        with patch.object(prepare, 'MODEL_BYTES', 3), patch.object(prepare, 'MODEL_SHA256', digest):
            self.assertTrue(prepare.model_evidence(model, receipt)['verified'])
            model.write_bytes(b'abd')
            with self.assertRaises(prepare.Refusal):
                prepare.model_evidence(model, receipt)
            model.write_bytes(b'abc')
            original.write_bytes(b'changed evidence fixture')
            with self.assertRaises(prepare.Refusal):
                prepare.model_evidence(model, receipt)

    def test_fresh_output_cannot_overwrite_or_follow_symlink(self):
        path = self.root / 'plan.json'
        prepare.write_fresh(path, {'execution_enabled': False})
        original = path.read_bytes()
        with self.assertRaises(FileExistsError):
            prepare.write_fresh(path, {'execution_enabled': True})
        self.assertEqual(path.read_bytes(), original)
        link = self.root / 'link.json'
        link.symlink_to(path)
        with self.assertRaises(FileExistsError):
            prepare.write_fresh(link, {})

    def test_cli_plan_is_disabled_and_input_identity_is_recorded(self):
        path, output = self.root / 'inspection.json', self.root / 'plan.json'
        path.write_text(json.dumps(fixture()))
        result = subprocess.run([sys.executable, '-B', str(Path(prepare.__file__)),
                                 '--inspection', str(path), '--output', str(output)],
                                capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        data = json.loads(output.read_text())
        self.assertFalse(data['execution_enabled'])
        self.assertEqual(data['inspection_sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
        forbidden = subprocess.run([sys.executable, '-B', str(Path(prepare.__file__)),
                                    '--run'], capture_output=True, text=True, timeout=5)
        self.assertNotEqual(forbidden.returncode, 0)


if __name__ == '__main__':
    unittest.main()
