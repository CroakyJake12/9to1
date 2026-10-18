import copy
import hashlib
import importlib.util
import json
import subprocess
from unittest import mock
from urllib import error as urlerror
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('pc_continuation', Path(__file__).with_name('continuation.py'))
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)
MODEL = {'providerID': 'homepc', 'id': 'strata-dynamic', 'variant': 'high'}
SID = 'ses_test_pc'


def assistant(identity='msg_assistant_1', created=20, completed=30, finish='stop', **extra):
    return {'id': identity, 'type': 'assistant', 'time': {'created': created, 'completed': completed},
            'model': MODEL, 'finish': finish, 'content': [{'type': 'text', 'text': 'Checkpoint.'}], **extra}


class API:
    def __init__(self):
        self.session = {'id': SID, 'location': {'directory': '/project'}, 'model': MODEL,
                        'outcome': 'succeeded', 'time': {'idle': 40}}
        self.user = {'id': 'msg_owner', 'type': 'user', 'time': {'created': 10}}
        self.assistant = assistant()
        self.compaction = None
        self.compaction_model = copy.deepcopy(MODEL)
        self.active = {}
        self.inbox = []
        self.permissions = []
        self.forms = []
        self.calls = []
        self.messages = {}
        self.fail = None
        self.hook = None
        self.gateway_result = {'kind': 'loaded'}
        self.gateway_calls = 0
        self.service_absent = True
        self.process_absent = True
        self.service_starts = 0
        self.model = {**MODEL, 'enabled': True, 'cost': [{'input': 0, 'output': 0, 'cache': {'read': 0, 'write': 0}}]}

    def request(self, operation, params=None, data=None):
        self.calls.append((operation, params, data))
        if self.hook:
            self.hook(operation, params, data)
        if self.fail == operation:
            raise RuntimeError('Do not record API secret text')
        if operation == 'session.get': value = self.session
        elif operation == 'session.active': value = self.active
        elif operation == 'session.message.list':
            value = ([self.compaction] if self.compaction else []) if params['type'] == 'compaction' else [self.user if params['type'] == 'user' else self.assistant]
        elif operation == 'agent.get':
            return {'location': copy.deepcopy(self.session['location']), 'data': {'id': 'compaction', 'model': copy.deepcopy(self.compaction_model)}}
        elif operation == 'session.message.get': value = self.messages[params['messageID']]
        elif operation == 'session.inbox.list': value = self.inbox
        elif operation == 'session.permission.list': value = self.permissions
        elif operation == 'session.form.list': value = self.forms
        elif operation == 'model.list': value = [self.model]
        elif operation == 'session.prompt':
            value = {'id': data['id'], 'type': 'user', 'metadata': data['metadata'], 'time': {'created': 50}}
            self.inbox.append(value)
        elif operation == 'session.interrupt':
            self.active.pop(SID, None)
            self.session['outcome'] = 'interrupted'
            self.session['time']['idle'] += 1
            return {'interrupted': True}
        elif operation == 'session.inbox.cancel':
            self.inbox = [row for row in self.inbox if row['id'] != params['inboxID']]
            return {}
        else: raise AssertionError(operation)
        return {'data': copy.deepcopy(value)}

    def probe_gateway(self, config, now):
        self.gateway_calls += 1
        return copy.deepcopy(self.gateway_result)
    def local_service_unavailable(self): return self.service_absent
    def opencode_process_absent(self): return self.process_absent
    def start_service(self): self.service_starts += 1

    @property
    def prompts(self):
        return [call for call in self.calls if call[0] == 'session.prompt']

    def finish_dispatch(self, failed=False):
        self.user = self.inbox.pop(0)
        self.messages[self.user['id']] = self.user
        self.assistant = assistant('msg_reply_' + str(len(self.prompts)), created=60 * len(self.prompts), completed=60 * len(self.prompts) + 1)
        self.user['time']['created'] = 60 * len(self.prompts) - 1
        if failed:
            self.assistant.update(finish='error', error={'type': 'TransportError', 'status': 503, 'message': 'not logged'})
        self.session['outcome'] = 'failed' if failed else 'succeeded'
        self.session['time']['idle'] = self.assistant['time']['completed'] + 1


class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.control = self.root / 'control.json'
        self.control.write_text(json.dumps({'mode': 'RUN'}))
        prompt = self.root / 'approved.txt'
        prompt.write_text('Continue the same programme from its checkpoint and current specification; preserve memory safeguards; final reply is not acceptance.')
        self.config = {'session_id': SID, 'model': MODEL, 'directory': '/project', 'server': 'http://127.0.0.1:1234',
                       'opencode': '/verified/opencode', 'service_config': '/private/auth',
                       'control_path': str(self.control), 'dispatch_lock_path': str(self.root / 'dispatch.lock'),
                       'enabled': True, 'free_usage_verified': True, 'approved_user_message_ids': ['msg_owner'],
                       'other_programme_session_ids': ['ses_peer'],
                       'continuation_prompt': {'path': str(prompt), 'sha256': hashlib.sha256(prompt.read_bytes()).hexdigest()},
                       'retry_prompt': {'path': str(prompt), 'sha256': hashlib.sha256(prompt.read_bytes()).hexdigest()}}
        self.write_config()
        self.api = API()
        self.clock = 1000
        self.worker = self.new_worker()

    def tearDown(self): self.temp.cleanup()
    def write_config(self): (self.root / 'config.json').write_text(json.dumps(self.config))
    def new_worker(self, execute=True): return module.Continuation(self.root, api=self.api, now=lambda: self.clock, execute=execute)
    def test_success_dispatches_once_per_actual_response_across_restart(self):
        self.assertEqual('dispatched', self.worker.tick())
        self.api.inbox.clear()  # A stale projection still cannot authorize the same boundary twice.
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_next_successful_turn_continues_same_session(self):
        self.worker.tick(); self.api.finish_dispatch()
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual(2, len(self.api.prompts))
        self.assertTrue(all(p[1]['sessionID'] == SID for p in self.api.prompts))
    def test_only_one_followup_canary_finishes_without_third_prompt(self):
        self.config.update(max_dispatches=1, exit_after_dispatch_limit=True); self.write_config()
        self.worker.tick(); self.api.finish_dispatch()
        self.assertEqual('dispatch_limit_reached', self.worker.tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_active_or_pending_or_permission_or_form_never_dispatches(self):
        for field, value, expected in [('active', {SID: {'type': 'running'}}, 'running'),
                                      ('inbox', [{'id': 'msg_pending'}], 'pending_inbox'),
                                      ('permissions', [{}], 'requires_owner_input'), ('forms', [{}], 'requires_owner_input')]:
            old = getattr(self.api, field); setattr(self.api, field, value)
            self.assertEqual(expected, self.worker.tick()); setattr(self.api, field, old)
        self.assertFalse(self.api.prompts)
    def test_peer_running_blocks_shared_pc_dispatch(self):
        self.api.active = {'ses_peer': {'type': 'running'}}
        self.assertEqual('held_other_implementation_running', self.worker.tick())
        self.assertFalse(self.api.prompts)
    def test_unrelated_session_is_neither_read_nor_interrupted(self):
        self.api.active = {'ses_unrelated': {'type': 'running'}}
        self.worker.tick()
        self.assertTrue(all((p or {}).get('sessionID', SID) == SID for _, p, _ in self.api.calls))
    def test_idle_succeeded_after_error_does_not_mask_failure(self):
        self.api.assistant.update(finish='error', error={'type': 'TransportError', 'message': 'failed'})
        self.api.session['outcome'] = 'failed'; self.assertEqual('held_terminal_failure', self.worker.tick())
        self.api.session['outcome'] = 'succeeded'; self.assertEqual('held_terminal_failure', self.worker.tick())
        self.assertFalse(self.api.prompts)
    def test_old_retry_record_can_be_superseded_by_genuine_completed_response(self):
        self.api.assistant['retry'] = {'attempt': 2, 'at': 25, 'error': {'type': 'TransportError'}}
        self.assertEqual('dispatched', self.worker.tick())
    def test_incomplete_or_running_tool_never_dispatches(self):
        self.api.assistant['time'].pop('completed')
        self.assertEqual('held_incomplete_response', self.worker.tick())
        self.api.assistant = assistant(finish='tool-calls', content=[{'type': 'tool', 'state': {'status': 'running'}}])
        self.assertEqual('held_terminal_failure', self.worker.tick())
    def test_terminal_tool_failure_is_product_followup_not_provider_retry(self):
        self.api.assistant = assistant(finish='tool-calls', content=[{'type': 'tool', 'state': {'status': 'error'}}])
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual(0, self.worker.state['retry_count'])
    def test_interrupted_turn_is_held_never_crash_restarted(self):
        self.api.session['outcome'] = 'interrupted'
        self.assertEqual('held_interrupted', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_unknown_owner_stop_prompt_holds_even_with_succeeded_outcome(self):
        self.api.user['id'] = 'msg_owner_stop'
        self.assertEqual('held_unreviewed_owner_input', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_stop_cancels_only_owned_inbox_and_interrupts_exact_session(self):
        self.worker.tick(); self.api.inbox.append({'id': 'msg_other'})
        self.api.active = {SID: {'type': 'running'}, 'ses_unrelated': {'type': 'running'}}
        self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', self.worker.tick())
        self.assertEqual([{'id': 'msg_other'}], self.api.inbox)
        self.assertIn('ses_unrelated', self.api.active)
        self.assertFalse(self.worker.state['observed_active'])
    def test_hold_only_pause_has_no_mutating_calls(self):
        self.control.write_text(json.dumps({'mode': 'PAUSE', 'hold_only': True}))
        self.api.active = {SID: {'type': 'running'}}
        self.assertEqual('paused', self.worker.tick())
        self.assertFalse(any(c[0] in ('session.prompt', 'session.interrupt', 'session.inbox.cancel') for c in self.api.calls))
    def test_control_changed_in_final_window_prevents_prompt(self):
        original = self.worker.persist
        def persist(mode, **fields):
            result = original(mode, **fields)
            if mode == 'dispatch_intent': self.control.write_text(json.dumps({'mode': 'STOP'}))
            return result
        self.worker.persist = persist
        self.assertEqual('deferred_control_changed', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_free_cost_and_exact_model_are_required_no_switch_fallback(self):
        self.api.model['cost'][0]['output'] = 1
        self.assertEqual('held_free_model_verification', self.worker.tick())
        self.api.session['model'] = {'providerID': 'opencode', 'id': 'beam-free'}
        self.assertEqual('held_model_changed', self.worker.tick())
        self.assertFalse(any(c[0] == 'session.switchModel' for c in self.api.calls))
    def test_prompt_hash_change_blocks(self):
        Path(self.config['continuation_prompt']['path']).write_text('Unreviewed')
        self.assertEqual('held_configuration_unavailable', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_ambiguous_prompt_is_never_resent_and_can_reconcile_exact_id(self):
        def lose_response(operation, params, data):
            if operation == 'session.prompt':
                self.api.messages[data['id']] = {'id': data['id'], 'type': 'user', 'metadata': data['metadata']}
                raise TimeoutError('secret must not be logged')
        self.api.hook = lose_response
        self.assertEqual('held_ambiguous_dispatch', self.worker.tick())
        self.api.hook = None; self.clock += 10
        self.assertEqual('dispatch_reconciled', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
        self.assertNotIn('secret must', (self.root / 'continuation-events.jsonl').read_text())
    def test_ambiguous_missing_message_remains_held_without_retry(self):
        self.api.fail = 'session.prompt'; self.worker.tick(); self.clock += 10
        self.api.fail = None
        self.assertEqual('held_ambiguous_dispatch', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_provider_retries_bounded_backoff_persist_and_rate_limit_refused(self):
        self.config.update(max_turn_retries=2, retry_error_types=['TransportError'], retry_base_seconds=30); self.write_config()
        self.api.assistant.update(finish='error', error={'type': 'TransportError', 'status': 503})
        self.api.session['outcome'] = 'failed'
        self.assertEqual('provider_backoff', self.worker.tick())
        self.clock += 30; self.assertEqual('dispatched', self.worker.tick())
        self.api.finish_dispatch(failed=True); self.assertEqual('provider_backoff', self.worker.tick())
        self.clock += 60; self.assertEqual('dispatched', self.new_worker().tick())
        self.worker = self.new_worker(); self.api.finish_dispatch(failed=True)
        self.assertEqual('held_terminal_failure', self.worker.tick())
        self.api.assistant['error']['status'] = 429
        self.assertEqual('held_terminal_failure', self.worker.tick())
        self.assertEqual(2, len(self.api.prompts))
    def test_api_reads_backoff_and_stop_bypasses_exhausted_run_probe_budget(self):
        self.api.fail = 'session.get'
        for _ in range(5): self.worker.tick(); self.clock += 400
        self.assertEqual('unavailable', self.worker.tick())
        self.api.fail = None; self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', self.worker.tick())
    def test_target_date_does_not_automatically_stop_authorised_work(self):
        self.clock = 9999999999
        self.assertEqual('dispatched', self.worker.tick())
    def test_observe_default_does_not_dispatch(self):
        self.assertEqual('ready_observe_only', self.new_worker(execute=False).tick())
        self.assertFalse(self.api.prompts)
    def test_single_owner_and_dispatch_locks_prevent_overlap(self):
        with module.lock(self.root / 'continuation.lock'):
            with self.assertRaises(BlockingIOError):
                with module.lock(self.root / 'continuation.lock'): pass
        with module.lock(self.config['dispatch_lock_path']):
            self.assertEqual('held_other_dispatcher', self.worker.tick())
        self.assertFalse(self.api.prompts)
    def test_verified_owner_pc_empty_catalogue_pricing_is_supported_without_paid_fallback(self):
        self.api.model['cost'] = []
        self.assertEqual('held_free_model_verification', self.worker.tick())
        self.config.update(pricing_basis='owner_owned_pc', gateway_base_url='https://ai.9to1.uk/v1')
        self.write_config()
        self.assertEqual('dispatched', self.worker.tick())
    def test_local_coordinator_marker_holds_without_interpreting_its_text(self):
        marker = self.root / 'NEEDS_COORDINATOR.json'
        self.config['coordinator_marker_path'] = str(marker); self.write_config()
        marker.write_text('Incomplete or untrusted worker marker')
        self.assertEqual('requires_coordinator_marker', self.worker.tick())
        self.assertFalse(self.api.prompts)
        self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.api.active = {SID: {'type': 'running'}}
        self.assertEqual('stopped', self.worker.tick())
        self.assertNotIn(SID, self.api.active)
    def test_config_cannot_repin_programme(self):
        self.config['session_id'] = 'ses_different'; self.write_config()
        with self.assertRaises(ValueError): self.worker.tick()
        self.assertFalse(self.api.prompts)

    def enable_recovery(self, active=False):
        self.config.update(gateway_base_url='https://ai.9to1.uk/v1', recovery={
            'enabled': True, 'credential_file': str(self.root / 'private-key'),
            'retry_base_seconds': 30, 'probe_base_seconds': 5, 'probe_max_seconds': 30,
            'active_outage': {'enabled': active, 'stale_seconds': 60, 'probe_interval_seconds': 30}})
        self.write_config()
    def failed_provider(self, status=503):
        self.api.assistant.update(finish='error', error={'type': 'TransportError', 'status': status})
        self.api.session['outcome'] = 'failed'
    def active_generation(self):
        self.api.active = {SID: {'type': 'running'}}
        self.api.assistant = assistant(completed=None, finish=None)
        self.api.session['outcome'] = None
    def collect_outage(self):
        self.assertEqual('running_generation_observed', self.worker.tick())
        self.clock += 60
        self.assertEqual('observed_pc_outage', self.worker.tick())
        self.clock += 30
        self.assertEqual('observed_pc_outage', self.worker.tick())
        self.clock += 30
        return self.worker.tick()
    def test_read_outages_beyond_five_recover_automatically_without_duplicate_dispatch(self):
        self.api.fail = 'session.get'
        for _ in range(12):
            self.assertEqual('unavailable', self.worker.tick()); self.clock += 400
        self.api.fail = None
        self.assertEqual('dispatched', self.new_worker().tick())
        self.api.inbox.clear()
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_terminal_outage_probes_until_model_loaded_then_resumes_exact_session_once(self):
        self.enable_recovery(); self.failed_provider()
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'unavailable', 'status': 502}
        for _ in range(8):
            self.assertEqual('waiting_for_pc_model', self.worker.tick()); self.clock += 31
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.new_worker().tick())
        self.assertEqual(SID, self.api.prompts[0][1]['sessionID'])
        self.api.inbox.clear()
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_gateway_auth_parks_without_further_probes_until_epoch_changes_and_stop_wins(self):
        self.enable_recovery(); self.failed_provider(); self.worker.tick(); self.clock += 30
        self.api.gateway_result = {'kind': 'authentication', 'status': 401}
        self.assertEqual('held_gateway_authentication', self.worker.tick())
        requests = len(self.api.calls)
        self.clock += 10000
        self.assertEqual('held_gateway_authentication', self.new_worker().tick())
        self.assertEqual(1, self.api.gateway_calls); self.assertEqual(requests, len(self.api.calls))
        self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', self.worker.tick())
        self.control.write_text(json.dumps({'mode': 'RUN'})); self.config['recovery_epoch'] = 1; self.write_config()
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
    def test_local_auth_parks_but_stop_rechecks_local_service_without_restart(self):
        def reject(operation, params, data): raise module.APIError('authentication')
        self.api.hook = reject
        self.assertEqual('held_local_api_authentication', self.worker.tick())
        calls = len(self.api.calls); self.worker.tick(); self.assertEqual(calls, len(self.api.calls))
        self.api.hook = None; self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', self.worker.tick()); self.assertEqual(0, self.api.service_starts)
    def test_provider_and_probe_retry_after_are_honored(self):
        self.enable_recovery(); self.failed_provider(429)
        self.api.assistant['retry'] = {'at': (self.clock + 90) * 1000, 'attempt': 1}
        self.assertEqual('provider_backoff', self.worker.tick())
        self.clock += 89; self.worker.tick(); self.assertEqual(0, self.api.gateway_calls)
        self.clock += 1; self.api.gateway_result = {'kind': 'rate_limit', 'status': 429, 'retry_after_seconds': 600}
        self.assertEqual('waiting_for_pc_model', self.worker.tick())
        self.clock += 599; self.worker.tick(); self.assertEqual(1, self.api.gateway_calls)
        self.clock += 1; self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual(60, module.retry_after('Thu, 01 Jan 1970 00:02:00 GMT', 60))
        self.assertEqual(90, module.retry_after('90', 0))
    def test_tool_failure_is_not_retried_as_provider_failure_with_recovery_enabled(self):
        self.enable_recovery()
        self.api.assistant = assistant(finish='tool-calls', content=[{'type': 'tool', 'state': {'status': 'error'}}])
        self.assertEqual('dispatched', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
        self.assertEqual(0, self.worker.state['retry_count'])
    def test_terminal_auth_and_unknown_errors_do_not_probe_or_retry(self):
        self.enable_recovery(); self.failed_provider(401)
        self.assertEqual('held_provider_authentication', self.worker.tick())
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
    def test_active_outage_requires_three_network_observations_and_resumes_only_own_interrupt(self):
        self.enable_recovery(active=True); self.active_generation()
        self.api.gateway_result = {'kind': 'unavailable', 'status': 503}
        self.assertEqual('outage_interrupted', self.collect_outage())
        self.assertEqual(3, self.api.gateway_calls); self.assertFalse(self.api.prompts)
        receipt = self.worker.state['outage_interrupt']
        self.assertEqual('confirmed', receipt['status']); self.assertEqual(SID, receipt['session_id'])
        self.worker = self.new_worker()
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual(1, len([c for c in self.api.calls if c[0] == 'session.interrupt']))
        self.assertEqual(1, len(self.api.prompts))
    def test_known_pending_repair_is_preserved_once_across_owned_outage_resume(self):
        self.enable_recovery(active=True); self.active_generation()
        self.config['approved_user_message_ids'].append('msg_queued_repair'); self.write_config()
        self.api.inbox = [{'id': 'msg_queued_repair', 'type': 'user', 'payload': {'text': 'Approved repair'}}]
        self.api.gateway_result = {'kind': 'unavailable'}
        self.assertEqual('outage_interrupted', self.collect_outage())
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual(['msg_queued_repair', self.api.prompts[0][2]['id']], [row['id'] for row in self.api.inbox])
        self.assertEqual(1, len(self.api.prompts))
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_active_running_tool_unknown_input_and_permissions_never_interrupt_or_probe(self):
        self.enable_recovery(active=True); self.active_generation()
        self.api.gateway_result = {'kind': 'unavailable'}
        self.api.assistant['content'] = [{'type': 'tool', 'state': {'status': 'running'}}]
        for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.api.assistant['content'] = []
        self.api.inbox = [{'id': 'unknown', 'type': 'user'}]
        for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.api.inbox = []; self.api.permissions = [{}]
        self.assertEqual('requires_owner_input', self.worker.tick())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_active_auth_or_rate_limit_never_counts_as_outage(self):
        self.enable_recovery(active=True); self.active_generation()
        self.api.gateway_result = {'kind': 'rate_limit', 'retry_after_seconds': 400}
        self.worker.tick(); self.clock += 60
        self.assertEqual('running_generation_gateway_rate_limit', self.worker.tick())
        self.clock += 399; self.worker.tick(); self.assertEqual(1, self.api.gateway_calls)
        self.clock += 1; self.api.gateway_result = {'kind': 'authentication'}
        self.assertEqual('held_gateway_authentication', self.worker.tick())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_fresh_generation_activity_resets_outage_samples(self):
        self.enable_recovery(active=True); self.active_generation(); self.api.gateway_result = {'kind': 'unavailable'}
        self.worker.tick(); self.clock += 60; self.worker.tick()
        self.api.assistant['content'][0]['text'] = 'Actual new event'
        self.clock += 30; self.assertEqual('running_generation_observed', self.worker.tick())
        self.assertEqual([], self.worker.state['active_outage_probe']['failures'])
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_stop_at_outage_intent_prevents_interrupt_and_control_change_blocks_owned_resume(self):
        self.enable_recovery(active=True); self.active_generation(); self.api.gateway_result = {'kind': 'unavailable'}
        original = self.worker.persist
        def persist(mode, **fields):
            result = original(mode, **fields)
            if mode == 'outage_interrupt_intent': self.control.write_text(json.dumps({'mode': 'STOP'}))
            return result
        self.worker.persist = persist
        self.assertEqual('deferred_control_changed', self.collect_outage())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
        self.assertNotIn('outage_interrupt', self.worker.state)
    def test_manual_interruption_after_confirmed_outage_is_never_auto_resumed(self):
        self.enable_recovery(active=True); self.active_generation(); self.api.gateway_result = {'kind': 'unavailable'}
        self.assertEqual('outage_interrupted', self.collect_outage())
        self.api.session['time']['idle'] += 1  # A different interruption boundary is not ours.
        self.assertEqual('held_interrupted', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_ambiguous_outage_interrupt_is_not_resent(self):
        self.enable_recovery(active=True); self.active_generation(); self.api.gateway_result = {'kind': 'unavailable'}
        self.api.fail = 'session.interrupt'
        self.assertEqual('unavailable', self.collect_outage())
        self.api.fail = None; self.clock += 30
        self.assertEqual('held_ambiguous_outage_interrupt', self.new_worker().tick())
        self.assertEqual(1, len([c for c in self.api.calls if c[0] == 'session.interrupt']))
    def test_canary_dispatch_limit_includes_failed_followup(self):
        self.enable_recovery(); self.config['max_dispatches'] = 1; self.write_config()
        self.worker.tick(); self.api.finish_dispatch(failed=True)
        self.assertEqual('dispatch_limit_reached', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
    def test_scoped_service_start_requires_verified_absence_exclusivity_and_recent_health(self):
        self.config['service_recovery'] = {'enabled': True, 'exclusive_ownership_verified': True}; self.write_config()
        self.api.active = {SID: {'type': 'running'}}; self.worker.tick()
        self.api.fail = 'session.get'; self.api.process_absent = False
        self.assertEqual('unavailable', self.worker.tick()); self.assertEqual(0, self.api.service_starts)
        self.clock += 10; self.api.process_absent = True
        self.assertEqual('unavailable', self.worker.tick()); self.assertEqual(1, self.api.service_starts)
        self.clock += 20; self.worker.tick(); self.assertEqual(1, self.api.service_starts)
    def test_service_start_never_touches_unrelated_active_or_ambiguous_service(self):
        self.config['service_recovery'] = {'enabled': True, 'exclusive_ownership_verified': True}; self.write_config()
        self.api.active = {SID: {}, 'ses_unrelated': {}}; self.worker.tick(); self.api.fail = 'session.get'
        self.worker.tick(); self.assertEqual(0, self.api.service_starts)
        self.worker.state['last_healthy_service']['active'] = [SID]; self.clock += 10
        self.api.service_absent = False; self.worker.tick(); self.assertEqual(0, self.api.service_starts)
    def test_gateway_probe_uses_exact_authenticated_loaded_model_and_never_logs_credentials(self):
        self.enable_recovery(); key = self.root / 'private-key'; key.write_text('fixture-secret')
        api = module.API(self.config)
        response = mock.MagicMock(); response.__enter__.return_value = response
        response.read.return_value = json.dumps({'data': [{'id': 'strata-dynamic', 'status': {'value': 'loaded'}}]}).encode()
        opener = mock.Mock(); opener.open.return_value = response
        with mock.patch.object(module.urlrequest, 'build_opener', return_value=opener):
            self.assertEqual({'kind': 'loaded'}, api.probe_gateway(self.config, self.clock))
        req = opener.open.call_args.args[0]
        self.assertEqual('https://ai.9to1.uk/v1/models', req.full_url)
        self.assertEqual('Bearer fixture-secret', req.get_header('Authorization'))
        key.unlink(); self.assertEqual({'kind': 'configuration'}, api.probe_gateway(self.config, self.clock))
        self.assertIsNone(module.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://other.invalid'))
    def prepare_milestones(self):
        project = self.root / 'project'; project.mkdir()
        def git(*args):
            return subprocess.run(['git', '-C', str(project), *args], check=True, capture_output=True, text=True).stdout.strip()
        git('init'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid')
        (project / 'source.txt').write_text('base'); git('add', 'source.txt'); git('commit', '-m', 'Initial fixture')
        self.git = git; self.project = project
        plan = {'schema_version': 1, 'milestones': [
            {'id': 'R01', 'dependencies': []}, {'id': 'R02', 'dependencies': ['R01']}, {'id': 'R03', 'dependencies': []}]}
        state = {'current_milestone_id': 'R01', 'milestones': {
            'R01': {'implementation_status': 'in_progress', 'acceptance_status': 'NOT_RUN', 'commit': None},
            'R02': {'implementation_status': 'pending'}, 'R03': {'implementation_status': 'pending'}}}
        paths = {}
        for key, value in [('plan', json.dumps(plan)), ('spec', 'Canonical specification.'), ('commit_policy', 'Use approved helper. Preserve STOP/access guards. Scoped commits only.')]:
            path = self.root / (key + '.txt'); path.write_text(value)
            paths[key] = {'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
        self.milestone_state = self.root / 'milestones.json'; self.milestone_state.write_text(json.dumps(state))
        checkpoint = self.root / 'checkpoint.md'; checkpoint.write_text('Initial retained checkpoint.')
        self.config.update(directory=str(project), milestones={**paths, 'state_path': str(self.milestone_state),
            'checkpoint_path': str(checkpoint), 'dirty_turn_limit': 3, 'dirty_seconds_limit': 1800})
        self.write_config(); self.api.session['location']['directory'] = str(project); self.worker = self.new_worker()
    def test_three_dirty_turns_get_focused_commit_instruction_and_actual_commit_resets(self):
        self.prepare_milestones(); (self.project / 'source.txt').write_text('actual change')
        for _ in range(3): self.assertEqual('dispatched', self.worker.tick()); self.api.finish_dispatch()
        self.assertEqual('dispatched', self.worker.tick())
        text = self.api.prompts[-1][2]['text']
        self.assertIn('CHECKPOINT/COMMIT TURN ONLY', text); self.assertNotIn('Continue the same programme from its checkpoint', text)
        self.assertIn('Preserve STOP/access guards', text)
        self.git('add', 'source.txt'); self.git('commit', '-m', 'Coherent fixture change'); self.api.finish_dispatch()
        self.assertEqual('dispatched', self.worker.tick())
        self.assertNotIn('CHECKPOINT/COMMIT TURN ONLY', self.api.prompts[-1][2]['text'])
        self.assertEqual(self.git('rev-parse', 'HEAD'), self.worker.state['checkpoint_head'])
    def test_dirty_time_bound_empty_commit_does_not_erase_real_work_due(self):
        self.prepare_milestones(); (self.project / 'source.txt').write_text('actual change')
        self.worker.tick(); self.api.finish_dispatch(); self.clock += 1800
        self.git('commit', '--allow-empty', '-m', 'Empty receipt is not substantive')
        self.assertEqual('dispatched', self.worker.tick())
        self.assertIn('CHECKPOINT/COMMIT TURN ONLY', self.api.prompts[-1][2]['text'])
        self.assertLess(self.worker.state['dirty_since'], self.clock)
    def test_untracked_work_evidence_does_not_create_dirty_checkpoint_but_tracked_work_does(self):
        self.prepare_milestones(); work = self.project / 'work'; work.mkdir(); (work / 'evidence.txt').write_text('Raw fixture evidence')
        self.worker.tick(); self.assertFalse(self.worker.state['checkpoint_observation']['dirty'])
        self.git('add', 'work/evidence.txt'); self.git('commit', '-m', 'Owned tracked fixture')
        (work / 'evidence.txt').write_text('Changed tracked source'); self.api.finish_dispatch(); self.worker.tick()
        self.assertTrue(self.worker.state['checkpoint_observation']['dirty'])
    def test_milestone_dependencies_skip_blocked_lane_and_null_all_implemented_holds_acceptance(self):
        self.prepare_milestones()
        state = json.loads(self.milestone_state.read_text()); state['milestones']['R01']['implementation_status'] = 'blocked'
        self.milestone_state.write_text(json.dumps(state)); self.worker.tick()
        self.assertIn('next dependency-eligible milestone: R03', self.api.prompts[-1][2]['text'])
        for item in state['milestones'].values(): item.update(implementation_status='implemented', acceptance_status='PENDING_REVIEW')
        state['current_milestone_id'] = None; self.milestone_state.write_text(json.dumps(state)); self.api.finish_dispatch()
        self.assertEqual('awaiting_independent_acceptance', self.worker.tick())
        self.assertEqual(1, len(self.api.prompts)); self.assertNotIn('VERIFIED', self.milestone_state.read_text())
    def test_repeated_completed_no_change_turns_receive_concrete_diagnostic_instruction(self):
        self.prepare_milestones()
        for _ in range(4): self.worker.tick(); self.api.finish_dispatch()
        self.assertIn('BOUNDED PROGRESS TURN', self.api.prompts[-1][2]['text'])
        self.assertNotIn('CHECKPOINT/COMMIT TURN ONLY', self.api.prompts[-1][2]['text'])
        self.assertFalse(self.worker.state['checkpoint_observation']['dirty'])
    def test_prompt_observation_is_frozen_at_final_admission_boundary(self):
        self.prepare_milestones(); (self.project / 'source.txt').write_text('change')
        self.worker.tick(); self.api.finish_dispatch(); self.clock += 1799
        original = self.worker.milestone_reminder
        calls = []
        def reminder():
            calls.append(1); result = original(); self.clock += 2; return result
        self.worker.milestone_reminder = reminder
        self.assertEqual('dispatched', self.worker.tick()); self.assertEqual(1, len(calls))

    def test_terminal_provider_failure_can_resume_known_preserved_queue_but_never_unknown_queue(self):
        self.enable_recovery(); self.failed_provider()
        self.api.inbox = [{'id': 'msg_queued_repair', 'type': 'user'}]
        self.assertEqual('pending_inbox', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
        self.config['approved_user_message_ids'].append('msg_queued_repair'); self.write_config()
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual('msg_queued_repair', self.api.inbox[0]['id']); self.assertEqual(1, len(self.api.prompts))
    def test_real_process_restart_retains_outage_backoff_and_dispatch_boundary(self):
        self.enable_recovery()
        script = """import importlib.util,json,sys
from pathlib import Path
spec=importlib.util.spec_from_file_location('fixture',sys.argv[1])
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
api=fixture.API()
api.assistant.update(finish='error',error={'type':'TransportError','status':503})
api.session['outcome']='failed'
api.gateway_result={'kind':sys.argv[3]}
with fixture.module.lock(Path(sys.argv[2])/'continuation.lock'):
 worker=fixture.module.Continuation(sys.argv[2],api=api,now=lambda:float(sys.argv[4]),execute=True)
 result=worker.tick()
print(json.dumps({'mode':result,'prompts':len(api.prompts),'probes':api.gateway_calls,
 'session_ids':[item[1]['sessionID'] for item in api.prompts]}))
"""
        def run(kind, when):
            result = subprocess.run(['python3', '-c', script, str(Path(__file__).resolve()), str(self.root), kind, str(when)],
                                    capture_output=True, text=True, check=True, timeout=10)
            return json.loads(result.stdout)
        self.assertEqual('provider_backoff', run('unavailable', 1000)['mode'])
        self.assertEqual('waiting_for_pc_model', run('unavailable', 1030)['mode'])
        resumed = run('loaded', 1100)
        self.assertEqual('dispatched', resumed['mode']); self.assertEqual([SID], resumed['session_ids'])
        duplicate = run('loaded', 1200)
        self.assertEqual('awaiting_new_response', duplicate['mode']); self.assertEqual(0, duplicate['prompts'])
        self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', run('loaded', 1300)['mode'])

    def test_repeated_cloudflare_tunnel_unavailability_can_retire_only_proven_stale_generation(self):
        self.enable_recovery(active=True)
        for status in (521, 522, 523, 524, 530):
            with self.subTest(status=status):
                self.worker.state.pop('active_outage_probe', None)
                self.worker.state.pop('outage_interrupt', None)
                self.active_generation(); self.api.gateway_result = {'kind': 'unavailable', 'status': status}
                self.assertEqual('outage_interrupted', self.collect_outage())
        self.assertEqual(5, len([c for c in self.api.calls if c[0] == 'session.interrupt']))
        self.assertFalse(self.api.prompts)
    def test_generic_500_and_unloaded_model_do_not_prove_active_generation_is_disconnected(self):
        self.enable_recovery(active=True); self.active_generation()
        for probe in ({'kind': 'unavailable', 'status': 500}, {'kind': 'not_loaded'}):
            self.api.gateway_result = probe
            for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_global_recovery_disabled_never_probes_or_interrupts_active_generation(self):
        self.enable_recovery(active=True); self.config['recovery']['enabled'] = False; self.write_config()
        self.active_generation(); self.api.gateway_result = {'kind': 'unavailable', 'status': 530}
        for _ in range(5): self.assertEqual('running', self.worker.tick()); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))

    def test_unclassified_api_error_requires_observed_outage_then_one_recovery_and_healthy_repeat_parks(self):
        self.enable_recovery(); self.failed_provider()
        self.api.assistant['error'] = {'type': 'APICallError'}
        self.config['approved_user_message_ids'].append('msg_queued_repair'); self.write_config()
        self.api.inbox = [{'id': 'msg_queued_repair', 'type': 'user'}]
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'unavailable', 'status': 530}
        self.assertEqual('waiting_for_pc_model', self.worker.tick()); self.clock += 5
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick()); self.assertEqual(1, len(self.api.prompts))
        self.api.inbox.pop(0)  # Preserved coordinator input was processed first by the fake service.
        self.api.finish_dispatch(failed=True); self.api.assistant['error'] = {'type': 'APICallError'}
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 60
        self.assertEqual('held_unclassified_provider_failure_while_healthy', self.worker.tick())
        self.assertEqual(1, len(self.api.prompts))
    def test_unclassified_healthy_failure_and_auth_class_never_guess_provider_retry(self):
        self.enable_recovery(); self.failed_provider()
        self.api.assistant['error'] = {'type': 'AuthenticationError'}
        self.assertEqual('held_terminal_failure', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
        self.api.assistant['error'] = {'type': 'APIError'}
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.assertEqual('held_unclassified_provider_failure_while_healthy', self.worker.tick())
        self.assertFalse(self.api.prompts)

    def test_null_current_with_blocked_dependencies_holds_without_configuration_error(self):
        self.prepare_milestones(); state = json.loads(self.milestone_state.read_text())
        state['current_milestone_id'] = None
        state['milestones']['R01']['implementation_status'] = 'blocked'
        state['milestones']['R03']['implementation_status'] = 'blocked'
        self.milestone_state.write_text(json.dumps(state))
        self.assertEqual('held_dependency_blockers', self.worker.tick())
        self.assertFalse(self.api.prompts); self.assertNotIn('parked', self.worker.state)
    def test_scoped_dependency_receipt_admits_eligible_milestone_with_null_current_without_acceptance_upgrade(self):
        self.prepare_milestones(); state = json.loads(self.milestone_state.read_text())
        state['current_milestone_id'] = None
        state['milestones']['R01']['implementation_status'] = 'blocked'
        state['milestones']['R02']['dependency_evidence'] = {'R01': {'record_path': '/fixture/receipt', 'acceptance_status': 'NOT_RUN'}}
        self.milestone_state.write_text(json.dumps(state))
        self.assertEqual('dispatched', self.worker.tick())
        self.assertIn('Current milestone: R02', self.api.prompts[-1][2]['text'])
        self.assertIn('helper next command', self.api.prompts[-1][2]['text'])
        self.assertIsNone(json.loads(self.milestone_state.read_text())['current_milestone_id'])
        self.assertNotIn('VERIFIED', self.milestone_state.read_text())

    def test_outage_revocation_on_third_probe_or_persisted_intent_prevents_interrupt(self):
        self.enable_recovery(active=True)
        for phase in ('third_probe', 'intent'):
            for grant in ('enabled', 'recovery', 'active_outage'):
                with self.subTest(phase=phase, grant=grant), tempfile.TemporaryDirectory(dir=self.root) as case:
                    root = Path(case); config = copy.deepcopy(self.config)
                    config['dispatch_lock_path'] = str(root / 'dispatch.lock')
                    config_path = root / 'config.json'; config_path.write_text(json.dumps(config))
                    self.api = API(); self.worker = module.Continuation(root, api=self.api, now=lambda: self.clock, execute=True)
                    self.active_generation(); self.api.gateway_result = {'kind': 'unavailable'}
                    def revoke():
                        if grant == 'enabled': config['enabled'] = False
                        elif grant == 'recovery': config['recovery']['enabled'] = False
                        else: config['recovery']['active_outage']['enabled'] = False
                        config_path.write_text(json.dumps(config))
                    original_probe = self.api.probe_gateway
                    def probe(*args):
                        result = original_probe(*args)
                        if phase == 'third_probe' and self.api.gateway_calls == 3: revoke()
                        return result
                    self.api.probe_gateway = probe
                    original_persist = self.worker.persist
                    def persist(mode, **fields):
                        result = original_persist(mode, **fields)
                        if phase == 'intent' and mode == 'outage_interrupt_intent': revoke()
                        return result
                    self.worker.persist = persist
                    self.assertEqual('deferred_control_changed', self.collect_outage())
                    self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
                    self.assertNotIn('outage_interrupt', self.worker.state)
    def test_service_recovery_revocation_after_absence_or_persisted_intent_prevents_start(self):
        for phase in ('absence', 'intent'):
            for grant in ('enabled', 'service_recovery', 'exclusive_ownership_verified'):
                with self.subTest(phase=phase, grant=grant), tempfile.TemporaryDirectory(dir=self.root) as case:
                    root = Path(case); config = copy.deepcopy(self.config)
                    config['service_recovery'] = {'enabled': True, 'exclusive_ownership_verified': True}
                    config['dispatch_lock_path'] = str(root / 'dispatch.lock')
                    config_path = root / 'config.json'; config_path.write_text(json.dumps(config))
                    api = API(); worker = module.Continuation(root, api=api, now=lambda: self.clock, execute=True)
                    api.active = {SID: {'type': 'running'}}; self.assertEqual('running', worker.tick())
                    api.fail = 'session.get'
                    def revoke():
                        if grant == 'enabled': config['enabled'] = False
                        elif grant == 'service_recovery': config['service_recovery']['enabled'] = False
                        else: config['service_recovery']['exclusive_ownership_verified'] = False
                        config_path.write_text(json.dumps(config))
                    def absent():
                        if phase == 'absence': revoke()
                        return True
                    api.opencode_process_absent = absent
                    original_persist = worker.persist
                    def persist(mode, **fields):
                        result = original_persist(mode, **fields)
                        if phase == 'intent' and mode == 'service_start_intent': revoke()
                        return result
                    worker.persist = persist
                    self.assertEqual('unavailable', worker.tick())
                    self.assertEqual(0, api.service_starts)
                    self.assertEqual('deferred_control_changed', worker.state['service_recovery_status'])
    def test_provider_recovery_revocation_at_probe_or_dispatch_intent_prevents_prompt(self):
        self.enable_recovery()
        for phase in ('probe', 'intent'):
            with self.subTest(phase=phase), tempfile.TemporaryDirectory(dir=self.root) as case:
                root = Path(case); config = copy.deepcopy(self.config)
                config['dispatch_lock_path'] = str(root / 'dispatch.lock')
                config_path = root / 'config.json'; config_path.write_text(json.dumps(config))
                api = API(); api.assistant.update(finish='error', error={'type': 'TransportError', 'status': 503})
                api.session['outcome'] = 'failed'
                worker = module.Continuation(root, api=api, now=lambda: self.clock, execute=True)
                self.assertEqual('provider_backoff', worker.tick()); self.clock += 30
                def revoke():
                    config['recovery']['enabled'] = False; config_path.write_text(json.dumps(config))
                def probe(*args):
                    if phase == 'probe': revoke()
                    return {'kind': 'loaded'}
                api.probe_gateway = probe
                original_persist = worker.persist
                def persist(mode, **fields):
                    result = original_persist(mode, **fields)
                    if phase == 'intent' and mode == 'dispatch_intent': revoke()
                    return result
                worker.persist = persist
                self.assertEqual('deferred_control_changed', worker.tick())
                self.assertFalse(api.prompts); self.assertNotIn('pending', worker.state)

    def test_running_modes_persist_fresh_observation_without_advancing_dispatch_or_exposing_content(self):
        self.enable_recovery(active=True); self.active_generation()
        self.worker.state.update(assistant_id='old_assistant', user_id='old_user', last_boundary='unchanged_boundary')
        self.assertEqual('running_generation_observed', self.worker.tick())
        first = json.loads(self.worker.state_path.read_text())['current_observation']
        self.assertEqual('msg_assistant_1', first['assistant_id']); self.assertEqual('msg_owner', first['user_id'])
        self.api.user = {'id': 'msg_new_owner', 'type': 'user', 'time': {'created': 60}}
        self.config['approved_user_message_ids'].append('msg_new_owner'); self.write_config()
        self.api.assistant = assistant('msg_new_assistant', created=70, completed=None, finish=None, content=[
            {'type': 'tool', 'id': 'call_current', 'name': 'shell',
             'state': {'status': 'running', 'input': {'command': 'fixture-private-input'},
                       'content': 'fixture-private-output'}}])
        self.clock += 5
        self.assertEqual('running_tool_or_unqualified_generation', self.worker.tick())
        saved = json.loads(self.worker.state_path.read_text()); observed = saved['current_observation']
        self.assertEqual('msg_new_assistant', observed['assistant_id']); self.assertEqual('msg_new_owner', observed['user_id'])
        self.assertEqual(self.clock, observed['observed_at']); self.assertTrue(observed['active'])
        self.assertEqual([{'id': 'call_current', 'name': 'shell', 'status': 'running'}], observed['tools'])
        self.assertEqual(MODEL, saved['model']); self.assertEqual('unchanged_boundary', saved['last_boundary'])
        self.assertEqual([], saved['dispatched']); self.assertFalse(self.api.prompts)
        self.assertNotIn('fixture-private', json.dumps(observed))
        self.api.fail = 'session.get'; self.clock += 5
        self.assertEqual('unavailable', self.worker.tick())
        self.assertEqual(observed, json.loads(self.worker.state_path.read_text())['current_observation'])

    def active_compaction(self):
        self.enable_recovery(active=True)
        self.api.active = {SID: {'type': 'running'}}; self.api.session['outcome'] = None
        self.api.compaction = {'id': 'msg_auto_compaction', 'type': 'compaction', 'time': {'created': 35},
            'status': 'running', 'reason': 'auto', 'summary': 'Partial summary', 'recent': 'Retained recent work'}
    def test_compaction_owned_outage_recovers_exact_phase_without_inventing_model_or_completion(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable', 'status': 530}
        self.assertEqual('outage_interrupted', self.collect_outage())
        receipt = self.worker.state['outage_interrupt']
        self.assertEqual('msg_auto_compaction', receipt['phase_id']); self.assertEqual('compaction', receipt['phase_type'])
        self.assertEqual('msg_assistant_1', receipt['assistant_id'])
        self.assertNotIn('model', self.api.compaction); self.assertNotIn('completed', self.api.compaction['time'])
        self.worker = self.new_worker()
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
        self.assertIn('msg_auto_compaction:outage:', self.worker.state['last_boundary'])
        self.api.inbox.clear()
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts)); self.assertTrue(self.api.prompts[0][2]['resume'])
        self.assertEqual('compaction', self.worker.state['current_observation']['phase_type'])
        self.assertFalse(any(c[0] == 'session.compact' for c in self.api.calls))
    def test_compaction_progress_resets_outage_proof_and_healthy_endpoint_does_not_interrupt(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable', 'status': 503}
        self.worker.tick(); self.clock += 60; self.assertEqual('observed_pc_outage', self.worker.tick())
        self.api.compaction['summary'] = 'Actual new exposed summary progress'
        self.clock += 30; self.assertEqual('running_generation_observed', self.worker.tick())
        self.assertEqual([], self.worker.state['active_outage_probe']['failures'])
        self.clock += 60; self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('running_generation_gateway_loaded', self.worker.tick())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls)); self.assertFalse(self.api.prompts)
    def test_compaction_older_than_new_assistant_or_new_owner_never_overrides_them(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        self.api.assistant = assistant('msg_new_assistant', created=40, completed=50)
        for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.assertEqual('assistant', self.worker.state['current_observation']['phase_type'])
        self.api.assistant = assistant(); self.api.user['time']['created'] = 40
        for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_compaction_running_tool_or_pending_permission_never_probes_or_interrupts(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        self.api.assistant['content'] = [{'type': 'tool', 'state': {'status': 'running'}}]
        for _ in range(5): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.api.assistant['content'] = []; self.api.permissions = [{'id': 'permission'}]
        self.assertEqual('requires_owner_input', self.worker.tick())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_compaction_requires_explicit_effective_agent_model_and_auto_reason(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        for model in (None, {'providerID': 'other', 'id': 'paid'}, {**MODEL, 'variant': 'different'}):
            self.api.compaction_model = model
            for _ in range(4): self.worker.tick(); self.clock += 1000
        self.api.compaction_model = MODEL; self.api.compaction['reason'] = 'manual'
        for _ in range(4): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_compaction_change_or_agent_model_change_during_third_probe_blocks_interrupt(self):
        for change in ('phase', 'model'):
            with self.subTest(change=change), tempfile.TemporaryDirectory(dir=self.root) as case:
                self.active_compaction(); config = copy.deepcopy(self.config)
                config['dispatch_lock_path'] = str(Path(case) / 'dispatch.lock')
                (Path(case) / 'config.json').write_text(json.dumps(config))
                self.worker = module.Continuation(case, api=self.api, now=lambda: self.clock, execute=True)
                self.api.gateway_calls = 0; self.api.gateway_result = {'kind': 'unavailable'}
                original_probe = self.api.probe_gateway
                def probe(*args):
                    result = original_probe(*args)
                    if self.api.gateway_calls == 3:
                        if change == 'phase': self.api.assistant = assistant('msg_newer', created=40, completed=None, finish=None)
                        else: self.api.compaction_model = {**MODEL, 'variant': 'changed'}
                    return result
                self.api.probe_gateway = probe
                self.assertEqual('deferred_active_state_changed', self.collect_outage())
                self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
                self.api = API()
    def test_compaction_manual_stop_and_unknown_owner_never_auto_recover(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        self.api.active.clear(); self.api.session.update(outcome='interrupted')
        self.assertEqual('held_interrupted', self.worker.tick()); self.assertFalse(self.api.prompts)
        self.api.active = {SID: {'type': 'running'}}; self.api.session['outcome'] = None
        self.api.user['id'] = 'msg_owner_stop'
        self.assertEqual('held_unreviewed_owner_input', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
    def test_compaction_stop_between_durable_intent_and_call_prevents_interrupt(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        original = self.worker.persist
        def persist(mode, **fields):
            result = original(mode, **fields)
            if mode == 'outage_interrupt_intent': self.control.write_text(json.dumps({'mode': 'STOP'}))
            return result
        self.worker.persist = persist
        self.assertEqual('deferred_control_changed', self.collect_outage())
        self.assertNotIn('outage_interrupt', self.worker.state)
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
    def test_unowned_unknown_compaction_failure_or_completion_never_replays_or_claims_success(self):
        self.active_compaction(); self.api.active.clear()
        self.api.compaction.update(status='failed', error={'type': 'UnknownError', 'message': 'Fixture unknown failure', 'status': 503})
        self.api.session['outcome'] = 'failed'
        self.assertEqual('held_compaction_outcome', self.worker.tick())
        self.api.session['outcome'] = 'succeeded'  # A later idle event cannot erase failed phase evidence.
        self.assertEqual('held_compaction_outcome', self.worker.tick())
        self.api.compaction.pop('error'); self.api.compaction.update(status='completed', model=MODEL)
        self.assertEqual('held_compaction_outcome', self.worker.tick())
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
        self.assertNotIn('checkpoint_observation', self.worker.state)
    def test_changed_compaction_id_after_owned_interruption_cannot_reuse_old_recovery_receipt(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        self.assertEqual('outage_interrupted', self.collect_outage())
        self.api.compaction['id'] = 'msg_different_compaction'
        self.assertEqual('held_interrupted', self.worker.tick()); self.assertFalse(self.api.prompts)

    def test_peer_start_on_third_compaction_outage_probe_blocks_fresh_interrupt(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        original = self.api.probe_gateway
        def probe(*args):
            result = original(*args)
            if self.api.gateway_calls == 3: self.api.active['ses_peer'] = {'type': 'running'}
            return result
        self.api.probe_gateway = probe
        self.assertEqual('deferred_active_state_changed', self.collect_outage())
        self.assertFalse(any(c[0] == 'session.interrupt' for c in self.api.calls))
        self.assertFalse(self.api.prompts); self.assertNotIn('outage_interrupt', self.worker.state)
    def test_compaction_known_queue_survives_owned_recovery_but_unknown_inbox_blocks_proof(self):
        self.active_compaction(); self.api.gateway_result = {'kind': 'unavailable'}
        self.api.inbox = [{'id': 'msg_queued_repair', 'type': 'user', 'payload': {'text': 'Approved existing work'}}]
        for _ in range(4): self.worker.tick(); self.clock += 1000
        self.assertEqual(0, self.api.gateway_calls)
        self.config['approved_user_message_ids'].append('msg_queued_repair'); self.write_config()
        self.assertEqual('outage_interrupted', self.collect_outage())
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual('msg_queued_repair', self.api.inbox[0]['id'])
        self.assertEqual(2, len(self.api.inbox)); self.assertEqual(1, len(self.api.prompts))
        self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts))

    def test_unavailable_compaction_model_proof_blocks_recovery_but_does_not_block_explicit_stop(self):
        self.active_compaction(); self.api.active['ses_unrelated'] = {'type': 'running'}
        self.api.gateway_result = {'kind': 'unavailable'}
        def reject_proof(operation, params, data):
            if operation == 'agent.get': raise module.APIError('authentication')
        self.api.hook = reject_proof
        self.assertEqual('running_tool_or_unqualified_generation', self.worker.tick())
        observed = self.worker.state['current_observation']
        self.assertFalse(observed['phase_model_verified']); self.assertEqual('APIError', observed['compaction_proof_error_type'])
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
        self.control.write_text(json.dumps({'mode': 'STOP'}))
        self.assertEqual('stopped', self.worker.tick())
        self.assertNotIn(SID, self.api.active); self.assertIn('ses_unrelated', self.api.active)
        self.assertEqual(1, len([c for c in self.api.calls if c[0] == 'session.interrupt']))

    def failed_compaction(self, error_type='APIError', status=503):
        self.active_compaction(); self.api.active.clear(); self.api.session['outcome'] = 'failed'
        self.api.compaction = {'id': 'msg_failed_compaction', 'type': 'compaction', 'time': {'created': 35},
            'status': 'failed', 'reason': 'auto', 'error': {'type': error_type, 'message': 'Fixture-private error'}}
        if status is not None: self.api.compaction['error']['status'] = status
    def test_terminal_compaction_503_waits_for_loaded_gateway_and_resumes_exact_session_once_after_restart(self):
        self.failed_compaction(); self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.api.gateway_result = {'kind': 'unavailable', 'status': 530}
        self.assertEqual('waiting_for_pc_model', self.worker.tick()); self.clock += 31
        self.api.gateway_result = {'kind': 'loaded'}; self.worker = self.new_worker()
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual('msg_failed_compaction', self.worker.state['last_boundary'])
        self.assertEqual(SID, self.api.prompts[0][1]['sessionID']); self.assertTrue(self.api.prompts[0][2]['resume'])
        self.api.inbox.clear(); self.assertEqual('awaiting_new_response', self.new_worker().tick())
        self.assertEqual(1, len(self.api.prompts)); self.assertNotIn('model', self.api.compaction)
        self.assertNotIn('completed', self.api.compaction['time'])
        self.assertFalse(any(c[0] in ('session.compact', 'session.interrupt') for c in self.api.calls))
        self.assertNotIn('Fixture-private', self.worker.state_path.read_text())
    def test_terminal_compaction_exact_transport_type_without_status_is_supported(self):
        self.failed_compaction('ConnectionError', None)
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.assertEqual('dispatched', self.worker.tick()); self.assertEqual(1, self.api.gateway_calls)
    def test_terminal_compaction_unknown_auth_configuration_and_cancel_errors_remain_held(self):
        for error_type, status in (('UnknownError', 503), ('APIError', None), ('TransportError', 401),
                ('APIError', 403), ('HTTPError', 400), ('HTTPError', 501), ('AbortError', 503),
                ('AuthenticationError', 503), ('PermissionDeniedError', 503)):
            with self.subTest(error_type=error_type, status=status):
                self.failed_compaction(error_type, status)
                self.assertEqual('held_compaction_outcome', self.worker.tick())
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
    def test_terminal_compaction_requires_failed_idle_boundary_and_does_not_accept_synthetic_success(self):
        self.failed_compaction()
        for outcome, idle in (('succeeded', 40), ('interrupted', 40), (None, 40), ('failed', 34), ('failed', None)):
            with self.subTest(outcome=outcome, idle=idle):
                self.api.session.update(outcome=outcome); self.api.session['time']['idle'] = idle
                expected = 'held_interrupted' if outcome == 'interrupted' else 'held_compaction_outcome'
                self.assertEqual(expected, self.worker.tick())
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
    def test_terminal_compaction_model_manual_reason_and_running_tools_cannot_recover(self):
        self.failed_compaction(); self.api.compaction['reason'] = 'manual'
        self.assertEqual('held_unverified_compaction', self.worker.tick())
        self.api.compaction['reason'] = 'auto'; self.api.compaction_model = None
        self.assertEqual('held_unverified_compaction', self.worker.tick())
        self.api.compaction_model = MODEL
        self.api.assistant['content'] = [{'type': 'tool', 'state': {'status': 'running'}}]
        self.assertEqual('held_compaction_outcome', self.worker.tick())
        self.assertEqual(0, self.api.gateway_calls); self.assertFalse(self.api.prompts)
    def test_terminal_compaction_known_queue_is_preserved_and_unknown_queue_blocks(self):
        self.failed_compaction(); self.api.inbox = [{'id': 'msg_repair', 'type': 'user'}]
        self.assertEqual('pending_inbox', self.worker.tick()); self.assertEqual(0, self.api.gateway_calls)
        self.config['approved_user_message_ids'].append('msg_repair'); self.write_config()
        self.assertEqual('provider_backoff', self.worker.tick()); self.clock += 30
        self.assertEqual('dispatched', self.worker.tick())
        self.assertEqual('msg_repair', self.api.inbox[0]['id']); self.assertEqual(2, len(self.api.inbox))
        self.assertEqual('awaiting_new_response', self.new_worker().tick()); self.assertEqual(1, len(self.api.prompts))
    def test_terminal_compaction_rate_limit_probe_delay_is_honored(self):
        self.failed_compaction('HTTPError', 429); self.worker.tick(); self.clock += 30
        self.api.gateway_result = {'kind': 'rate_limit', 'status': 429, 'retry_after_seconds': 600}
        self.assertEqual('waiting_for_pc_model', self.worker.tick()); self.clock += 599
        self.assertEqual('provider_backoff', self.worker.tick()); self.assertEqual(1, self.api.gateway_calls)
        self.clock += 1; self.api.gateway_result = {'kind': 'loaded'}
        self.assertEqual('dispatched', self.worker.tick()); self.assertEqual(2, self.api.gateway_calls)
    def test_terminal_compaction_fresh_phase_tools_model_owner_peer_or_idle_change_blocks_dispatch(self):
        changes = {
            'phase': lambda: self.api.compaction.update(id='msg_other_compaction'),
            'error': lambda: self.api.compaction['error'].update(message='Changed failure payload'),
            'tool': lambda: self.api.assistant.update(content=[{'type': 'tool', 'state': {'status': 'running'}}]),
            'model': lambda: setattr(self.api, 'compaction_model', {**MODEL, 'variant': 'changed'}),
            'user': lambda: self.api.user.update(id='msg_owner_stop'),
            'peer': lambda: self.api.active.update(ses_peer={'type': 'running'}),
            'manual_cancel': lambda: self.api.session.update(outcome='interrupted'),
            'idle': lambda: self.api.session['time'].update(idle=41),
            'newer_assistant': lambda: setattr(self.api, 'assistant', assistant('msg_newer', created=36, completed=39)),
        }
        for name, change in changes.items():
            with self.subTest(change=name):
                self.api = API(); self.worker.state_path.unlink(missing_ok=True); self.worker = self.new_worker()
                self.failed_compaction(); self.worker.tick(); self.clock += 30
                original = self.api.probe_gateway
                def probe(*args):
                    result = original(*args); change(); return result
                self.api.probe_gateway = probe
                self.assertEqual('deferred_state_changed', self.worker.tick()); self.assertFalse(self.api.prompts)
    def test_terminal_compaction_stop_after_persisted_prompt_intent_prevents_dispatch(self):
        self.failed_compaction(); self.worker.tick(); self.clock += 30
        original = self.worker.persist
        def persist(mode, **fields):
            result = original(mode, **fields)
            if mode == 'dispatch_intent': self.control.write_text(json.dumps({'mode': 'STOP'}))
            return result
        self.worker.persist = persist
        self.assertEqual('deferred_control_changed', self.worker.tick()); self.assertFalse(self.api.prompts)
        self.assertNotIn('pending', self.worker.state); self.assertEqual('stopped', self.worker.tick())


if __name__ == '__main__': unittest.main(verbosity=2)
