"""CPU-only authority/cold-phase checks; never use the production proof path."""
import concurrent.futures
import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import proof_authority as authority
from proof_safety import Refusal, STALE_NS, WORK_NS, TOTAL_NS

HOST = 'GPU-11111111-1111-1111-1111-111111111111'
GUEST = 'GPU-22222222-2222-2222-2222-222222222222'
COMMIT = 'a' * 40
ARGS = {'manifest_sha256': 'b' * 64, 'source_commit': 'c' * 40,
        'authorization_sha256': 'd' * 64, 'host_gpu_uuid': HOST}


class AuthorityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-authority-cpu-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'authorizations' / authority.PROOF_ID
        self.patcher = patch.object(authority, '_AUTHORITY_DIRECTORY', self.root)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def consume(self):
        result = authority.consume(**ARGS)
        return result, (self.root / 'CONSUMED.json').read_bytes()

    def publish(self, data):
        return authority.record_publication(consumed_commit_sha=COMMIT,
                                            observed_main_sha=COMMIT,
                                            remote_consumed_bytes=data)

    def claim(self, data, main=COMMIT, **overrides):
        owner = authority.process_identity()
        args = {'observed_main_sha': main, 'remote_consumed_bytes': data,
                'remote_dispatch_absent': True, 'owner_session': '0' * 32,
                'owner_boot_id': owner['boot_id'], 'controller_pid': owner['controller_pid'],
                'controller_start_ticks': owner['controller_start_ticks']}
        return authority.claim_dispatch(**(args | overrides))

    def test_exact_global_identity_and_policy_written_once(self):
        result, data = self.consume()
        record = json.loads(data)
        self.assertEqual(result['sha256'], hashlib.sha256(data).hexdigest())
        self.assertEqual(record['schema'], 'dulche.v100-consumed/v1')
        self.assertEqual(record['proof_id'], 'v100-tinyllama-one-proof-20261116')
        self.assertTrue(record['consumed'])
        self.assertTrue(record['existing_global_allowance_reused'])
        self.assertEqual(record['policy'], authority.POLICY)
        self.assertIsNone(record['guest_gpu_uuid'])
        self.assertEqual((self.root / 'CONSUMED.json').stat().st_mode & 0o777, 0o400)
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            authority.consume(**ARGS)
        self.assertEqual((self.root / 'CONSUMED.json').read_bytes(), data)

    def test_no_public_alternate_directory_or_proof_id(self):
        for extra in ({'directory': Path(self.temp.name) / 'another'}, {'proof_id': 'another'}):
            with self.subTest(extra=extra), self.assertRaises(TypeError):
                authority.consume(**ARGS, **extra)

    def test_invalid_metadata_does_not_create_consumption(self):
        for field, invalid in (('manifest_sha256', 'f' * 63), ('source_commit', '../bad'),
                               ('authorization_sha256', True), ('host_gpu_uuid', GUEST + ';run')):
            with self.subTest(field=field), self.assertRaises(Refusal):
                authority.consume(**(ARGS | {field: invalid}))
            self.assertFalse(self.root.exists())

    def test_concurrent_consumers_have_one_winner(self):
        def attempt(_):
            try:
                return authority.consume(**ARGS)['sha256']
            except Refusal:
                return None
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(attempt, range(16)))
        self.assertEqual(sum(result is not None for result in results), 1)
        authority.read_consumed()

    def test_existing_partial_record_blocks_consumption_and_dispatch(self):
        self.root.mkdir(parents=True)
        (self.root / 'CONSUMED.json').write_bytes(b'{"schema":')
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            authority.consume(**ARGS)
        with self.assertRaisesRegex(Refusal, 'partial_or_invalid'):
            self.claim(b'')
        self.assertEqual((self.root / 'CONSUMED.json').read_bytes(), b'{"schema":')

    def test_failed_partial_write_is_retained_and_refuses_retry(self):
        original_write = os.write
        calls = 0
        def short_then_fail(fd, data):
            nonlocal calls
            calls += 1
            if calls == 1:
                return original_write(fd, data[:12])
            raise OSError('fixture storage failure')
        with patch.object(authority.os, 'write', side_effect=short_then_fail):
            with self.assertRaisesRegex(OSError, 'fixture storage failure'):
                authority.consume(**ARGS)
        self.assertEqual((self.root / 'CONSUMED.json').stat().st_size, 12)
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            authority.consume(**ARGS)

    def test_file_and_directory_fsync_finish_before_consumption_returns(self):
        original = os.fsync
        calls = []
        def observed(fd):
            calls.append(os.fstat(fd).st_mode)
            return original(fd)
        with patch.object(authority.os, 'fsync', side_effect=observed):
            self.consume()
        self.assertGreaterEqual(len(calls), 4)  # Parent dirs, file, containing dir.

    def test_symlink_directory_or_consumed_leaf_is_refused(self):
        outside = Path(self.temp.name) / 'outside'
        outside.mkdir()
        self.root.parent.mkdir()
        self.root.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(Refusal):
            authority.consume(**ARGS)
        self.assertFalse((outside / 'CONSUMED.json').exists())
        self.root.unlink()
        self.root.mkdir()
        (self.root / 'CONSUMED.json').symlink_to(outside / 'absent')
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            authority.consume(**ARGS)
        self.assertFalse((outside / 'absent').exists())

    def test_no_dispatch_without_published_readback(self):
        _, data = self.consume()
        with self.assertRaises(FileNotFoundError):
            self.claim(data)
        self.assertFalse((self.root / 'DISPATCH-CLAIMED.json').exists())

    def test_publication_requires_matching_main_and_exact_bytes(self):
        _, data = self.consume()
        cases = [('e' * 40, data), (COMMIT, data + b' '), (COMMIT, data.decode())]
        for main, remote in cases:
            with self.subTest(main=main, remote_type=type(remote)), self.assertRaises(Refusal):
                authority.record_publication(consumed_commit_sha=COMMIT,
                                            observed_main_sha=main, remote_consumed_bytes=remote)
        self.assertFalse((self.root / 'PUBLICATION.json').exists())

    def test_publication_and_dispatch_are_separate_non_overwriting_records(self):
        _, data = self.consume()
        self.publish(data)
        published = (self.root / 'PUBLICATION.json').read_bytes()
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            self.publish(data)
        claim = self.claim(data)
        self.assertEqual(claim['record']['consumed_sha256'], hashlib.sha256(data).hexdigest())
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            self.claim(data)
        self.assertEqual((self.root / 'CONSUMED.json').read_bytes(), data)
        self.assertEqual((self.root / 'PUBLICATION.json').read_bytes(), published)

    def test_stale_offline_or_changed_publication_cannot_dispatch(self):
        _, data = self.consume()
        self.publish(data)
        for main, remote in [('e' * 40, data), (COMMIT, b'changed')]:
            with self.subTest(main=main), self.assertRaises(Refusal):
                self.claim(remote, main)
        p = self.root / 'PUBLICATION.json'
        p.chmod(0o600)
        record = json.loads(p.read_bytes());record['manifest_sha256'] = 'f' * 64
        p.write_text(json.dumps(record))
        with self.assertRaisesRegex(Refusal, 'publication_receipt_mismatch'):
            self.claim(data)

    def test_existing_remote_claim_refuses_a_fresh_local_checkout(self):
        _, data = self.consume();self.publish(data)
        with self.assertRaisesRegex(Refusal, 'canonical_dispatch_already_exists'):
            self.claim(data, remote_dispatch_absent=False)
        self.assertFalse((self.root / 'DISPATCH-CLAIMED.json').exists())

    def test_claim_rejects_wrong_process_start_or_boot(self):
        _, data = self.consume();self.publish(data)
        for override in ({'controller_start_ticks': 0}, {'owner_boot_id': '0' * 36}):
            with self.subTest(override=override), self.assertRaisesRegex(Refusal, 'controller_identity_changed'):
                self.claim(data, **override)

    def test_global_dispatch_publication_required_before_final_local_barrier(self):
        _, data = self.consume();self.publish(data);self.claim(data)
        dispatched = (self.root / 'DISPATCH-CLAIMED.json').read_bytes()
        with self.assertRaises(FileNotFoundError):
            authority.authorize_local_execution(owner_session='0' * 32, observed_main_sha='e' * 40,
                                                remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        with self.assertRaisesRegex(Refusal, 'published_dispatch_bytes_mismatch'):
            authority.record_dispatch_publication(dispatch_commit_sha='e' * 40, observed_main_sha='e' * 40,
                                                  remote_consumed_bytes=data, remote_dispatch_bytes=dispatched + b' ')
        authority.record_dispatch_publication(dispatch_commit_sha='e' * 40, observed_main_sha='e' * 40,
                                              remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        result = authority.authorize_local_execution(owner_session='0' * 32, observed_main_sha='e' * 40,
                                                      remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        self.assertEqual(result['record']['owner']['controller_pid'], os.getpid())
        with self.assertRaisesRegex(Refusal, 'record_already_exists'):
            authority.authorize_local_execution(owner_session='0' * 32, observed_main_sha='e' * 40,
                                                remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)

    def test_downloaded_claim_cannot_authorize_another_process_or_session(self):
        _, data = self.consume();self.publish(data);self.claim(data)
        dispatched = (self.root / 'DISPATCH-CLAIMED.json').read_bytes()
        authority.record_dispatch_publication(dispatch_commit_sha='e' * 40, observed_main_sha='e' * 40,
                                              remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        with self.assertRaisesRegex(Refusal, 'execution_process_is_not_claimed_owner'):
            authority.authorize_local_execution(owner_session='1' * 32, observed_main_sha='e' * 40,
                                                remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        other = authority.process_identity() | {'controller_pid': os.getpid() + 100}
        with patch.object(authority, 'process_identity', return_value=other):
            with self.assertRaisesRegex(Refusal, 'execution_process_is_not_claimed_owner'):
                authority.authorize_local_execution(owner_session='0' * 32, observed_main_sha='e' * 40,
                                                    remote_consumed_bytes=data, remote_dispatch_bytes=dispatched)
        self.assertFalse((self.root / 'EXECUTION-STARTED.json').exists())

    def test_modified_consumed_policy_or_uuid_refuses(self):
        _, data = self.consume()
        p = self.root / 'CONSUMED.json';p.chmod(0o600)
        for mutation in ('policy', 'guest_gpu_uuid'):
            record = json.loads(data)
            if mutation == 'policy': record['policy']['cold_start_seconds'] = 9
            else: record['guest_gpu_uuid'] = GUEST
            p.write_text(json.dumps(record))
            with self.assertRaises(Refusal):
                authority.read_consumed()


class ColdPolicyTests(unittest.TestCase):
    def setUp(self):
        self.start = 1_000_000_000
        self.policy = authority.ColdStartPolicy('fixture', self.start, HOST)

    def pin(self):
        self.policy.pin_guest_identity(uuid=GUEST, pci_id='10de:1db4', subsystem_id='10de:1214')

    def sample(self, now, sequence=0, temp=40):
        return {'session': 'fixture', 'sequence': sequence, 'acquired_ns': now,
                'gpus': {'v100': {'uuid': GUEST, 'temperature_c': temp},
                         'rtx5080': {'uuid': HOST, 'temperature_c': 45}}}

    def test_no_inference_before_first_valid_sample_and_exact_eight_second_boundary(self):
        self.assertFalse(self.policy.inference_permitted(self.start + 7_000_000_000))
        self.pin()
        now = self.start + authority.COLD_NS
        self.policy.accept(self.sample(now), now)
        self.assertTrue(self.policy.inference_permitted(now))
        self.assertEqual(self.policy.start_ns, self.start)
        self.assertEqual(self.policy.teardown_remaining_ns(now), 22_000_000_000)

    def test_cold_expiry_latches_without_spawning_or_resetting_clock(self):
        with self.assertRaisesRegex(Refusal, 'cold_start_deadline'):
            self.policy.tick(self.start + authority.COLD_NS + 1)
        with self.assertRaisesRegex(Refusal, 'cold_start_deadline'):
            self.pin()

    def test_wrong_or_repeated_guest_identity_latches(self):
        with self.assertRaisesRegex(Refusal, 'invalid_gpu_uuid'):
            self.policy.pin_guest_identity(uuid='unknown', pci_id='10de:1db4', subsystem_id='10de:1214')
        with self.assertRaisesRegex(Refusal, 'invalid_gpu_uuid'):
            self.pin()
        self.setUp();self.pin()
        with self.assertRaisesRegex(Refusal, 'already_pinned'):
            self.pin()

    def test_wrong_pci_and_unpinned_samples_refuse(self):
        with self.assertRaisesRegex(Refusal, 'unexpected_guest_pci_identity'):
            self.policy.pin_guest_identity(uuid=GUEST, pci_id='10de:2c02', subsystem_id='10de:1214')
        self.setUp()
        with self.assertRaisesRegex(Refusal, 'guest_identity_unpinned'):
            self.policy.accept(self.sample(self.start), self.start)

    def test_start_temperature_and_post_start_staleness_remain_strict(self):
        self.pin();now = self.start + 7_000_000_000
        with self.assertRaisesRegex(Refusal, 'v100_start_too_hot'):
            self.policy.accept(self.sample(now, temp=40.01), now)
        self.setUp();self.pin();self.policy.accept(self.sample(now), now)
        self.policy.tick(now + STALE_NS)
        with self.assertRaisesRegex(Refusal, 'telemetry_stale'):
            self.policy.tick(now + STALE_NS + 1)

    def test_old_first_sample_is_not_retimestamped_as_fresh(self):
        self.pin();now = self.start + authority.COLD_NS
        with self.assertRaisesRegex(Refusal, 'telemetry_stale'):
            self.policy.accept(self.sample(now - STALE_NS - 1), now)

    def test_work_total_and_thermal_limits_unchanged(self):
        self.pin();now = self.start + 7_000_000_000
        self.policy.accept(self.sample(now, temp=35), now)
        with self.assertRaisesRegex(Refusal, 'v100_thermal_limit'):
            self.policy.accept(self.sample(now + 250_000_000, sequence=1, temp=45), now + 250_000_000)
        self.setUp();self.pin();self.policy.accept(self.sample(now), now)
        with self.assertRaisesRegex(Refusal, 'work_deadline'):
            self.policy.tick(self.start + WORK_NS)
        self.assertEqual(self.policy.teardown_remaining_ns(self.start + WORK_NS), 5_000_000_000)
        self.assertEqual(self.policy.teardown_remaining_ns(self.start + TOTAL_NS), 0)

    def test_host_thermal_limit_and_clock_reversal_latch(self):
        self.pin();now = self.start + 1_000_000_000
        sample = self.sample(now);sample['gpus']['rtx5080']['temperature_c'] = 75
        with self.assertRaisesRegex(Refusal, 'rtx5080_thermal_limit'):
            self.policy.accept(sample, now)
        self.setUp();self.policy.tick(self.start + 1)
        with self.assertRaisesRegex(Refusal, 'monotonic_clock_invalid'):
            self.policy.tick(self.start)


if __name__ == '__main__':
    unittest.main()
