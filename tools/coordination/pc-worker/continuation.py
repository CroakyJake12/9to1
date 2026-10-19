#!/usr/bin/env python3
"""Pinned-session PC continuation. Never creates sessions or changes models; recovery is explicitly gated.

The coordinator owns config.json and approved prompt files. This loop owns only
continuation-state.json, continuation-events.jsonl and continuation.lock.
"""
import argparse
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import os
import re
import socket
from email.utils import parsedate_to_datetime
from pathlib import Path
import subprocess
import tempfile
import time
from urllib.parse import urlparse
from urllib import error as urlerror, request as urlrequest


def read(path):
    return json.loads(Path(path).read_text())


def linux_boot_id():
    try:
        identity = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
        return identity if re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}', identity) else None
    except OSError:
        return None  # No verified boot identity means no cold-boot exception.


def save(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + '.tmp')
    with temporary.open('w') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    temporary.chmod(0o600)
    os.replace(temporary, path)


@contextlib.contextmanager
def lock(path):
    with Path(path).open('a') as stream:
        fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            fcntl.flock(stream, fcntl.LOCK_UN)


def retry_after(value, now):
    if value is None:
        return 0
    try:
        seconds = float(value)
        return max(0, seconds) if 0 <= seconds < float('inf') else 0
    except (TypeError, ValueError):
        try:
            return max(0, parsedate_to_datetime(value).timestamp() - now)
        except (TypeError, ValueError, OverflowError):
            return 0


def backoff(count, base, maximum):
    return min(maximum, base * 2 ** min(max(0, count - 1), 20))


class APIError(RuntimeError):
    def __init__(self, kind, operation=None, response_error_type=None):
        super().__init__('OpenCode API ' + kind)
        self.kind = kind
        self.operation = operation
        self.response_error_type = response_error_type


class NoRedirect(urlrequest.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Never forward the PC credential to a different endpoint.


class API:
    """Installed supported V2 CLI, pinned server; errors never echo secret text."""
    def __init__(self, config):
        self.config = config

    def request(self, operation, params=None, data=None):
        allowed = {'session.active', 'session.get', 'session.message.list',
                   'session.message.get', 'session.inbox.list', 'session.permission.list',
                   'session.form.list', 'model.list', 'agent.get', 'session.prompt',
                   'session.interrupt', 'session.inbox.cancel'}
        if operation not in allowed:
            raise ValueError('Unsupported operation')
        args = [self.config['opencode'], 'api', '--server', self.config['server'], operation]
        for key, value in (params or {}).items():
            args += ['--param', key + '=' + str(value)]
        if data is not None:
            args += ['--data', json.dumps(data)]
        env = os.environ.copy()
        env['OPENCODE_PASSWORD'] = read(self.config['service_config'])['password']
        try:
            # The installed CLI can exit before a large pipe write is drained.
            # A private, automatically closed regular file preserves the complete
            # supported CLI response without replaying a possibly mutating call.
            with tempfile.TemporaryFile(mode='w+', encoding='utf-8') as output:
                result = subprocess.run(args, cwd=self.config['directory'], env=env,
                                        stdout=output, stderr=subprocess.PIPE, text=True, timeout=30)
                output.seek(0)
                response_text = output.read()
        except subprocess.TimeoutExpired:
            raise APIError('transport', operation, 'TimeoutExpired') from None
        if result.returncode or 'Error:' in result.stderr:
            diagnostic = response_text + result.stderr  # Classified privately; never logged.
            if re.search(r'UnauthorizedError|ForbiddenError|\b(?:401|403)\b', diagnostic):
                raise APIError('authentication', operation)
            if re.search(r'InvalidRequestError|SessionNotFoundError|LocationNotFoundError', diagnostic):
                raise APIError('configuration', operation)
            raise APIError('transport', operation)
        try:
            response = json.loads(response_text) if response_text.strip() else {}
        except (ValueError, TypeError) as error:
            raise APIError('invalid_response', operation, type(error).__name__) from None
        read_shapes = {'session.active': dict, 'session.get': dict, 'session.message.list': list,
                       'session.message.get': dict, 'session.inbox.list': list, 'session.permission.list': list,
                       'session.form.list': list, 'model.list': list, 'agent.get': dict}
        if operation in read_shapes:
            if not isinstance(response, dict) or 'data' not in response:
                raise APIError('invalid_response', operation, 'MissingDataEnvelope')
            if not isinstance(response['data'], read_shapes[operation]):
                raise APIError('invalid_response', operation, 'InvalidDataShape')
        return response

    def probe_gateway(self, config, now):
        policy = config.get('recovery', {})
        if (config.get('gateway_base_url') != 'https://ai.9to1.uk/v1'
                or config.get('model', {}).get('providerID') != 'homepc'
                or config.get('model', {}).get('id') != 'strata-dynamic'):
            return {'kind': 'configuration'}
        try:
            credential = Path(policy['credential_file']).read_text().strip()
        except (OSError, KeyError):
            return {'kind': 'configuration'}
        if not credential or '\n' in credential or '\r' in credential:
            return {'kind': 'configuration'}
        try:
            req = urlrequest.Request('https://ai.9to1.uk/v1/models',
                                     headers={'Authorization': 'Bearer ' + credential, 'Accept': 'application/json'})
            with urlrequest.build_opener(NoRedirect()).open(req, timeout=10) as response:
                body = response.read(1024 * 1024 + 1)
                if len(body) > 1024 * 1024:
                    return {'kind': 'configuration'}
                data = json.loads(body)
            loaded = any(row.get('id') == 'strata-dynamic'
                         and row.get('status', {}).get('value') == 'loaded'
                         for row in data.get('data', []))
            return {'kind': 'loaded' if loaded else 'not_loaded'}
        except urlerror.HTTPError as error:
            kind = ('authentication' if error.code in (401, 403) else 'rate_limit' if error.code == 429
                    else 'unavailable' if error.code >= 500 or error.code in (408, 425)
                    else 'configuration')
            return {'kind': kind, 'status': error.code,
                    'retry_after_seconds': retry_after(error.headers.get('Retry-After'), now)}
        except (urlerror.URLError, TimeoutError, OSError):
            return {'kind': 'unavailable'}
        except (ValueError, KeyError, TypeError):
            return {'kind': 'configuration'}

    def local_service_unavailable(self):
        endpoint = urlparse(self.config['server'])
        try:
            with socket.create_connection((endpoint.hostname, endpoint.port), timeout=2):
                return False
        except ConnectionRefusedError:
            return True
        except OSError:
            return False  # Timeout/authentication ambiguity never authorizes service recovery.

    def opencode_process_absent(self):
        for entry in Path('/proc').iterdir():
            if not entry.name.isdigit():
                continue
            try:
                if entry.joinpath('comm').read_text().strip().startswith('opencode'):
                    return False
            except FileNotFoundError:
                continue
            except OSError:
                return False
        return True

    def start_service(self):
        result = subprocess.run([self.config['opencode'], 'service', 'start'],
                                capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise APIError('service_start')


def successful(message):
    created = message.get('time', {}).get('created')
    completed = message.get('time', {}).get('completed')
    if (message.get('type') != 'assistant' or not message.get('id')
            or not isinstance(created, (int, float)) or not isinstance(completed, (int, float))
            or completed < created or message.get('error')
            or message.get('finish') not in ('stop', 'tool-calls')):
        return False
    tools = [part for part in message.get('content', []) if part.get('type') == 'tool']
    if any(part.get('state', {}).get('status') not in ('completed', 'error') for part in tools):
        return False
    # A failed product test is executable follow-up work, not a provider failure.
    # Retained retry metadata is historical after a genuine completed response.
    return bool(tools) or (message.get('finish') == 'stop' and any(
        part.get('type') == 'text' and part.get('text', '').strip()
        for part in message.get('content', [])))


def free_model(model):
    costs = model.get('cost')
    return (model.get('enabled') is True and model.get('status') != 'deprecated'
            and isinstance(costs, list) and bool(costs)
            and all(c.get('input') == 0 and c.get('output') == 0
                    and c.get('cache', {}).get('read') == 0
                    and c.get('cache', {}).get('write') == 0 for c in costs))


class Continuation:
    def __init__(self, root, api=None, now=time.time, execute=False):
        self.root = Path(root)
        self.config_path = self.root / 'config.json'
        self.config = read(self.config_path)
        self.sid = self.config['session_id']
        self.model = self.config['model']
        self.directory = self.config['directory']
        endpoint = urlparse(self.config['server'])
        if (not self.sid.startswith('ses_') or self.model.get('id') != 'strata-dynamic'
                or 'beam' in self.model.get('providerID', '').lower()
                or endpoint.scheme != 'http' or endpoint.hostname != '127.0.0.1'
                or not endpoint.port or not Path(self.directory).is_absolute()):
            raise ValueError('Invalid pinned PC session/service/workspace identity')
        self.api = api or API(self.config)
        self.now = now
        self.execute = execute
        self.boot_id = linux_boot_id()
        self.state_path = self.root / 'continuation-state.json'
        self.state = read(self.state_path) if self.state_path.exists() else {
            'session_id': self.sid, 'model': self.model, 'directory': self.directory,
            'dispatched': [], 'retry_count': 0, 'api_failures': 0}
        if any(self.state.get(key) != value for key, value in (
                ('session_id', self.sid), ('model', self.model), ('directory', self.directory))):
            raise ValueError('Durable programme identity changed; coordinator reconciliation required')

    def persist(self, mode, **fields):
        if 'model' in fields:
            fields['observed_model'] = fields.pop('model')
        self.state.update(mode=mode, observed_at=self.now(), **fields)
        save(self.state_path, self.state)
        record = {'at': self.now(), 'mode': mode, **fields}
        signature = json.dumps({k: v for k, v in record.items() if k != 'at'}, sort_keys=True)
        if signature != getattr(self, '_last_record', None):
            log = self.root / 'continuation-events.jsonl'
            if log.exists() and log.stat().st_size > 1024 * 1024:
                os.replace(log, self.root / 'continuation-events.previous.jsonl')
            with log.open('a') as stream:
                stream.write(json.dumps(record) + '\n')
            log.chmod(0o600)
            self._last_record = signature
        return mode

    def reload(self):
        config = read(self.config_path)
        for key in ('session_id', 'directory', 'model', 'server', 'opencode', 'service_config',
                    'control_path', 'dispatch_lock_path'):
            if config.get(key) != self.config.get(key):
                raise ValueError('Pinned configuration identity changed')
        self.config = config
        if isinstance(self.api, API):
            self.api.config = config
        return read(self.config['control_path'])

    def call(self, operation, **params):
        return self.api.request(operation, {'sessionID': self.sid, **params})

    def latest(self, kind):
        data = self.call('session.message.list', type=kind, order='desc', limit=1)['data']
        return data[0] if data else {}

    def snapshot(self):
        session = self.call('session.get')['data']
        if session.get('id') != self.sid or session.get('location', {}).get('directory') != self.directory:
            raise ValueError('Session identity/workspace changed')
        snapshot = {'session': session, 'active': self.api.request('session.active')['data'],
                    'user': self.latest('user'), 'assistant': self.latest('assistant'),
                    'compaction': self.latest('compaction'),
                    'inbox': self.call('session.inbox.list')['data'],
                    'permissions': self.call('session.permission.list')['data'],
                    'forms': self.call('session.form.list')['data']}
        phase = self.inference_phase(snapshot)
        if phase.get('type') == 'compaction' and phase.get('reason') == 'auto' and phase.get('status') in ('running', 'failed'):
            try:
                agent = self.api.request('agent.get', {'agentID': 'compaction', 'location[directory]': self.directory})
                snapshot['compaction_agent'] = {'id': agent.get('data', {}).get('id'),
                    'model': agent.get('data', {}).get('model'), 'directory': agent.get('location', {}).get('directory')}
            except Exception as error:
                # Missing optional proof forbids recovery but must not block STOP.
                snapshot['compaction_agent'] = {'error_type': type(error).__name__}
        # Running modes need fresh evidence too; keep it separate from pinned
        # identity and dispatch receipts. Never retain arguments or output here.
        self.state['current_observation'] = {
            'observed_at': self.now(), 'session_id': self.sid, **self.evidence(snapshot),
            'active': self.sid in snapshot['active'],
            'tools': [{'id': part.get('id'), 'name': part.get('name'),
                       'status': part.get('state', {}).get('status')}
                      for part in snapshot['assistant'].get('content', []) if part.get('type') == 'tool']}
        return snapshot

    def evidence(self, snapshot):
        assistant = snapshot['assistant']
        phase = self.inference_phase(snapshot)
        return {'phase_id': phase.get('id'), 'phase_type': phase.get('type'),
                'phase_model_verified': self.phase_model_matches(snapshot),
                'compaction_proof_error_type': snapshot.get('compaction_agent', {}).get('error_type'),
                'phase_status': phase.get('status'), 'phase_created': phase.get('time', {}).get('created'),
                'phase_error_type': phase.get('error', {}).get('type'),
                'phase_error_status': phase.get('error', {}).get('status'),
                'assistant_id': assistant.get('id'), 'finish': assistant.get('finish'),
                'completed': assistant.get('time', {}).get('completed'),
                'error_type': assistant.get('error', {}).get('type'),
                'error_status': assistant.get('error', {}).get('status'),
                'retry_attempt': assistant.get('retry', {}).get('attempt'),
                'user_id': snapshot['user'].get('id'),
                'idle': snapshot['session'].get('time', {}).get('idle'),
                'outcome': snapshot['session'].get('outcome'),
                'model': snapshot['session'].get('model')}

    def halt(self, control, snapshot):
        # Only this pinned session is touched. Hold-only PAUSE merely parks dispatch.
        mode = control.get('mode')
        if not self.execute or control.get('hold_only') is True:
            return self.persist('paused' if mode == 'PAUSE' else 'stopped', observed_active=self.sid in snapshot['active'])
        owned = set(self.state['dispatched'])
        if self.state.get('pending'):
            owned.add(self.state['pending']['message_id'])
        for item in snapshot['inbox']:
            if item.get('id') in owned and self.reload().get('mode') in ('STOP', 'PAUSE'):
                self.api.request('session.inbox.cancel', {'sessionID': self.sid, 'inboxID': item['id']})
        if self.sid in snapshot['active'] and self.reload().get('mode') in ('STOP', 'PAUSE'):
            self.call('session.interrupt', resume='false')
        active = self.api.request('session.active')['data']
        return self.persist('paused' if mode == 'PAUSE' else 'stopped', observed_active=self.sid in active)

    def owner_resume_command(self, snapshot):
        # Opt-in applies only to an explicit short owner resume, never arbitrary
        # instructions or a Stop. The provider must still finish successfully.
        user = snapshot['user']
        raw = user.get('text', '')
        text = raw.strip().lower() if isinstance(raw, str) else ''
        return (self.config.get('follow_owner_selected_model') is True
                and user.get('type') == 'user' and text in ('continue', 'resume'))

    def admission_block(self, snapshot):
        if snapshot['session'].get('model') != self.model:
            return 'held_model_changed'
        approved = set(self.config.get('approved_user_message_ids', [])) | set(self.state['dispatched'])
        pending = self.state.get('pending')
        if pending:
            approved.add(pending['message_id'])
        if snapshot['user'].get('id') not in approved and not self.owner_resume_command(snapshot):
            return 'held_unreviewed_owner_input'
        if snapshot['permissions'] or snapshot['forms']:
            return 'requires_owner_input'
        if self.sid in snapshot['active']:
            return 'running'
        recovering_queue = (self.recovery_enabled()
                            and self.known_queue(snapshot)
                            and (self.owned_outage_matches(snapshot) or self.transient_terminal(snapshot)
                                 or self.outage_probe_candidate(snapshot)))
        if snapshot['inbox'] and not recovering_queue:
            return 'pending_inbox'
        for peer in self.config.get('other_programme_session_ids', []):
            if peer in snapshot['active']:
                return 'held_other_implementation_running'
        return None

    def known_queue(self, snapshot):
        approved = set(self.config.get('approved_user_message_ids', [])) | set(self.state['dispatched'])
        return all(item.get('type') == 'user' and item.get('id') in approved for item in snapshot['inbox'])

    def prompt_inputs(self, recovery):
        paths = [self.config['retry_prompt' if recovery else 'continuation_prompt']['path']]
        milestones = self.config.get('milestones', {})
        if milestones:
            paths += [milestones[key]['path'] for key in ('plan', 'spec', 'commit_policy')]
            paths += [milestones[key] for key in ('state_path', 'checkpoint_path')]
        fingerprint = [self.config, [hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in paths]]
        return hashlib.sha256(json.dumps(fingerprint, sort_keys=True).encode()).hexdigest()

    def prompt(self, recovery=False):
        source = self.config['retry_prompt' if recovery else 'continuation_prompt']
        raw = Path(source['path']).read_bytes()
        if hashlib.sha256(raw).hexdigest() != source['sha256'] or not raw.strip():
            raise ValueError('Approved continuation prompt identity changed')
        extra = self.milestone_reminder()
        text = ('' if self.state.get('checkpoint_commit_due') or self.state.get('focused_progress_due') else raw.decode('utf-8')) + extra
        return text, hashlib.sha256(text.encode()).hexdigest()

    def reconcile_pending(self, snapshot):
        pending = self.state.get('pending')
        if not pending:
            return None
        # A lost response is not permission to resend. Exact ID lookup can prove
        # admission; a missing/ambiguous result remains blocked for the coordinator.
        known = next((item for item in snapshot['inbox'] if item.get('id') == pending['message_id']), None)
        if known is None:
            try:
                known = self.call('session.message.get', messageID=pending['message_id'])['data']
            except Exception:
                return self.persist('held_ambiguous_dispatch', pending_message_id=pending['message_id'])
        if (known.get('id') != pending['message_id'] or known.get('type') != 'user'
                or known.get('metadata', known.get('payload', {}).get('metadata', {})).get('pc_continuation_boundary') != pending['boundary']):
            return self.persist('held_ambiguous_dispatch', pending_message_id=pending['message_id'])
        self.accept_dispatch(pending)
        return self.persist('dispatch_reconciled', message_id=pending['message_id'])

    def accept_dispatch(self, pending):
        if pending['message_id'] not in self.state['dispatched']:
            self.state['dispatched'].append(pending['message_id'])
        self.state['last_boundary'] = pending['boundary']
        self.state['retry_count'] = pending['retry_count']
        if pending['retry_count'] == 0:
            self.state['turns_since_commit'] = self.state.get('turns_since_commit', 0) + 1
        self.state['awaiting_user_id'] = pending['message_id']
        self.state.pop('pending', None)

    def tick(self):
        control = self.reload()
        # Control is read before even retrying an unavailable service.
        if control.get('mode') not in ('RUN', 'PAUSE', 'STOP'):
            return self.persist('held_invalid_control')
        parked = self.state.get('parked')
        if (control['mode'] == 'RUN' and parked
                and parked.get('epoch') == self.config.get('recovery_epoch', 0)):
            return self.persist(parked['mode'])
        self.state.pop('parked', None)
        if self.now() < self.state.get('next_probe', 0) and control['mode'] == 'RUN':
            return self.persist('api_backoff', retry_at=self.state['next_probe'])
        try:
            snapshot = self.snapshot()
            self.state.update(api_failures=0, next_probe=0,
                              last_healthy_service={'at': self.now(), 'active': list(snapshot['active']),
                                  'boot_id': self.boot_id,
                                  'control_hash': hashlib.sha256(Path(self.config['control_path']).read_bytes()).hexdigest()})
            if control['mode'] != 'RUN':
                return self.halt(control, snapshot)
            self.model = self.config['model']
            if self.config.get('follow_owner_selected_model') is True:
                selected = snapshot['session'].get('model')
                if not isinstance(selected, dict) or not selected.get('id') or not selected.get('providerID'):
                    return self.persist('held_invalid_selected_model')
                # Observe the owner's UI selection. Never write a model to OC.
                self.model = dict(selected)
            if self.config.get('enabled') is not True:
                return self.persist('disabled')
            marker = self.config.get('coordinator_marker_path')
            if marker and Path(marker).exists():
                return self.persist('requires_coordinator_marker', marker_path=marker)
            reconciled = self.reconcile_pending(snapshot)
            if reconciled:
                return reconciled
            blocked = self.admission_block(snapshot)
            if blocked:
                if blocked == 'running' and self.recovery_enabled(active=True):
                    return self.observe_active_outage(snapshot)
                return self.persist(blocked, **self.evidence(snapshot))
            assistant = snapshot['assistant']
            phase = self.inference_phase(snapshot)
            compaction = phase.get('type') == 'compaction'
            user = snapshot['user']
            created = phase.get('time', {}).get('created')
            if (not isinstance(created, (int, float)) or not isinstance(user.get('time', {}).get('created'), (int, float))
                    or created < user['time']['created']):
                return self.persist('awaiting_actual_response', **self.evidence(snapshot))
            awaiting = self.state.get('awaiting_user_id')
            if awaiting and user.get('id') != awaiting:
                # A new approved coordinator input supersedes our old boundary;
                # an older projection is never sufficient to dispatch again.
                if user.get('id') not in self.config.get('approved_user_message_ids', []) and not self.owner_resume_command(snapshot):
                    return self.persist('awaiting_dispatched_input', **self.evidence(snapshot))
            if compaction and phase.get('status') not in ('running', 'failed'):
                return self.persist('held_compaction_outcome', **self.evidence(snapshot))
            owner_selected_success = (self.config.get('follow_owner_selected_model') is True
                                      and not compaction and successful(assistant)
                                      and snapshot['session'].get('outcome') == 'succeeded')
            if not self.phase_model_matches(snapshot) and not owner_selected_success:
                return self.persist('held_unverified_compaction' if compaction else 'held_response_model_changed', **self.evidence(snapshot))
            outage_recovery = self.owned_outage_matches(snapshot)
            if snapshot['session'].get('outcome') == 'interrupted' and not outage_recovery:
                return self.persist('held_interrupted', **self.evidence(snapshot))
            terminal_compaction = compaction and self.transient_terminal(snapshot)
            if compaction and not outage_recovery and not (
                    terminal_compaction and self.recovery_enabled()):
                return self.persist('held_compaction_outcome', **self.evidence(snapshot))
            completed = assistant.get('time', {}).get('completed')
            if not outage_recovery and not terminal_compaction and (
                    not isinstance(completed, (int, float)) or completed < created):
                return self.persist('held_incomplete_response', **self.evidence(snapshot))
            # Failed compactions expose no completion timestamp. Their explicit
            # failed status and inactive failed session must agree at the gate.
            boundary_floor = created if terminal_compaction else completed
            if not outage_recovery and snapshot['session'].get('time', {}).get('idle', -1) < boundary_floor:
                return self.persist('awaiting_terminal_session_boundary', **self.evidence(snapshot))
            boundary = phase['id'] + (':outage:' + str(snapshot['session']['time']['idle']) if outage_recovery else '')
            if boundary == self.state.get('last_boundary'):
                return self.persist('awaiting_new_response', **self.evidence(snapshot))
            success = not compaction and successful(assistant) and snapshot['session'].get('outcome') == 'succeeded'
            if self.config.get('max_dispatches') is not None:
                limit = self.config['max_dispatches']
                if not isinstance(limit, int) or limit < 1:
                    return self.persist('held_invalid_dispatch_limit')
                if len(self.state['dispatched']) >= limit:
                    return self.persist('dispatch_limit_reached', **self.evidence(snapshot))
            recovery = not success
            if recovery and self.config.get('follow_owner_selected_model') is True and not self.recovery_enabled():
                return self.persist('held_terminal_failure', **self.evidence(snapshot))
            retry_count = 0 if success else self.state.get('retry_count', 0) + 1
            provider_recovery = recovery and self.recovery_enabled()
            if provider_recovery:
                held = self.recover_provider(snapshot, retry_count, outage_recovery)
                if held:
                    return held
            elif recovery:
                error = assistant.get('error', {})
                maximum = min(2, max(0, self.config.get('max_turn_retries', 0)))
                if (assistant.get('finish') != 'error' or not error
                        or error.get('type') not in self.config.get('retry_error_types', [])
                        or error.get('status') in (401, 403, 429) or retry_count > maximum):
                    return self.persist('held_terminal_failure', **self.evidence(snapshot))
                if self.state.get('failed_boundary') != boundary:
                    self.state['failed_boundary'] = boundary
                    self.state['retry_at'] = self.now() + min(1800, max(30, self.config.get('retry_base_seconds', 120)) * 2 ** (retry_count - 1))
                if self.now() < self.state['retry_at']:
                    return self.persist('provider_backoff', retry_at=self.state['retry_at'], **self.evidence(snapshot))
            self.state['observed_turn_boundary'] = boundary if success else None
            prompt_inputs = self.prompt_inputs(recovery)
            text, prompt_hash = self.prompt(recovery)
            if self.state.get('milestones_awaiting_review'):
                return self.persist('awaiting_independent_acceptance')
            if self.state.get('milestone_dependency_blocked'):
                return self.persist('held_dependency_blockers')
            models = self.api.request('model.list', {'location[directory]': self.directory})['data']
            model = next((row for row in models if row.get('providerID') == self.model['providerID']
                          and row.get('id') == self.model['id']), None)
            owner_pc = (self.config.get('pricing_basis') == 'owner_owned_pc'
                        and self.config.get('gateway_base_url') == 'https://ai.9to1.uk/v1'
                        and self.model.get('providerID') == 'homepc'
                        and model and model.get('enabled') is True
                        and model.get('status') != 'deprecated' and model.get('cost') == [])
            if (self.config.get('free_usage_verified') is not True or not model
                    or not (free_model(model) or owner_pc)):
                return self.persist('held_free_model_verification')
            if not self.execute:
                return self.persist('ready_observe_only', boundary=boundary, recovery=recovery)
            # All programme dispatchers must cooperate with the existing shared lock.
            with lock(self.config['dispatch_lock_path']):
                if (self.reload().get('mode') != 'RUN' or self.config.get('enabled') is not True
                        or (provider_recovery and not self.recovery_enabled())):
                    return self.persist('deferred_control_changed')
                fresh = self.snapshot()
                if (self.admission_block(fresh) or self.evidence(fresh) != self.evidence(snapshot)
                        or (terminal_compaction and (not self.transient_terminal(fresh)
                            or self.generation_fingerprint(fresh) != self.generation_fingerprint(snapshot)))
                        or self.prompt_inputs(recovery) != prompt_inputs
                        or (self.config.get('coordinator_marker_path') and Path(self.config['coordinator_marker_path']).exists())
                        or (success and not successful(fresh['assistant']))):
                    return self.persist('deferred_state_changed')
                message_id = 'msg_pc' + hashlib.sha256((self.sid + ':' + boundary).encode()).hexdigest()[:24]
                pending = {'message_id': message_id, 'boundary': boundary, 'retry_count': retry_count,
                           'prompt_sha256': prompt_hash, 'at': self.now()}
                self.persist('dispatch_intent', pending=pending)
                if (self.reload().get('mode') != 'RUN' or self.config.get('enabled') is not True
                        or (provider_recovery and not self.recovery_enabled())
                        or (self.config.get('coordinator_marker_path') and Path(self.config['coordinator_marker_path']).exists())):
                    self.state.pop('pending', None)
                    return self.persist('deferred_control_changed')
                result = self.api.request('session.prompt', {'sessionID': self.sid}, {
                    'id': message_id, 'text': text, 'delivery': 'queue', 'resume': True,
                    'metadata': {'pc_continuation_boundary': boundary, 'approved_prompt_sha256': prompt_hash}})
                if result.get('data', {}).get('id') != message_id:
                    return self.persist('held_ambiguous_dispatch', pending_message_id=message_id)
                self.accept_dispatch(pending)
                return self.persist('dispatched', message_id=message_id, boundary=boundary, recovery=recovery,
                                    prompt_sha256=prompt_hash, **self.evidence(snapshot))
        except BlockingIOError:
            return self.persist('held_other_dispatcher')
        except Exception as error:
            # No exception message/provider body, prompt, credential or tool output is logged.
            details = {'error_type': type(error).__name__}
            if isinstance(error, APIError):
                details.update(api_operation=error.operation, api_error_kind=error.kind,
                               response_error_type=error.response_error_type)
            if isinstance(error, APIError) and error.kind in ('authentication', 'configuration'):
                return self.park('held_local_api_' + error.kind, **details)
            if isinstance(error, (ValueError, KeyError, FileNotFoundError)):
                return self.park('held_configuration_unavailable', **details)
            count = min(1000000, self.state.get('api_failures', 0) + 1)
            self.state.update(api_failures=count, next_probe=self.now() + backoff(count, 5, 300))
            if (not self.state.get('pending')
                    or self.cold_boot_start_authorized(self.state.get('last_healthy_service', {}))):
                try:
                    self.recover_service(control)
                except Exception as recovery_error:
                    self.state['service_recovery_status'] = 'unavailable_' + type(recovery_error).__name__
            return self.persist('held_ambiguous_dispatch' if self.state.get('pending') else 'unavailable',
                                **details, consecutive_failures=count,
                                retry_at=self.state['next_probe'])

    def park(self, mode, **fields):
        self.state['parked'] = {'mode': mode, 'epoch': self.config.get('recovery_epoch', 0)}
        return self.persist(mode, **fields)

    def transient_terminal(self, snapshot):
        assistant = self.inference_phase(snapshot)
        error = assistant.get('error', {})
        status = error.get('status')
        types = self.config.get('recovery', {}).get('transient_error_types', [
            'TransportError', 'ConnectionError', 'NetworkError', 'TimeoutError', 'ReadTimeout',
            'FetchError', 'ProviderConnectionError', 'ECONNREFUSED', 'ECONNRESET', 'ETIMEDOUT'])
        transient = ((isinstance(status, int) and (status >= 500 or status in (408, 425, 429)))
                     or (status is None and error.get('type') in types))
        if assistant.get('type') == 'compaction':
            created = assistant.get('time', {}).get('created')
            idle = snapshot['session'].get('time', {}).get('idle')
            owner_created = snapshot['user'].get('time', {}).get('created')
            # An open error-type string or an old idle success is not evidence
            # of a provider failure. Only known transport/HTTP failures qualify.
            known_error = error.get('type') in types or error.get('type') in ('APIError', 'APICallError', 'HTTPError')
            trusted_transient = (status in (408, 425, 429, 500, 502, 503, 504, 521, 522, 523, 524, 530)
                                 or (status is None and error.get('type') in types))
            return (assistant.get('status') == 'failed' and self.phase_model_matches(snapshot)
                    and snapshot['session'].get('outcome') == 'failed' and self.sid not in snapshot['active']
                    and isinstance(created, (int, float)) and isinstance(idle, (int, float)) and idle >= created
                    and isinstance(owner_created, (int, float)) and created >= owner_created
                    and known_error and trusted_transient
                    and all(part.get('state', {}).get('status') in ('completed', 'error')
                            for part in snapshot['assistant'].get('content', []) if part.get('type') == 'tool'))
        return (snapshot['session'].get('outcome') != 'interrupted' and assistant.get('finish') == 'error'
                and bool(error) and transient and isinstance(assistant.get('time', {}).get('completed'), (int, float)))

    def outage_probe_candidate(self, snapshot):
        assistant = self.inference_phase(snapshot)
        error = assistant.get('error', {})
        allowed = self.config.get('recovery', {}).get('outage_probe_error_types', ['APIError', 'APICallError'])
        return (assistant.get('type') == 'assistant' and snapshot['session'].get('outcome') != 'interrupted' and assistant.get('finish') == 'error'
                and error.get('type') in allowed and error.get('status') is None
                and isinstance(assistant.get('time', {}).get('completed'), (int, float)))

    @staticmethod
    def endpoint_down(result):
        return result['kind'] == 'unavailable' and result.get('status') in (None, 502, 503, 504, 521, 522, 523, 524, 530)

    def recovery_enabled(self, active=False):
        return (self.config.get('enabled') is True
                and self.model.get('providerID') == 'homepc'
                and self.model.get('id') == 'strata-dynamic'
                and not (active and self.config.get('follow_owner_selected_model') is True)
                and self.config.get('recovery', {}).get('enabled') is True
                and (not active or self.config.get('recovery', {}).get('active_outage', {}).get('enabled') is True))

    def recover_provider(self, snapshot, retry_count, outage_recovery=False):
        assistant = self.inference_phase(snapshot)
        error = assistant.get('error', {})
        status = error.get('status')
        if status in (401, 403):
            return self.park('held_provider_authentication')
        if isinstance(status, int) and 400 <= status < 500 and status not in (408, 425, 429):
            return self.park('held_provider_configuration')
        policy = self.config['recovery']
        uncertain = self.outage_probe_candidate(snapshot) and not self.transient_terminal(snapshot)
        if not outage_recovery and not self.transient_terminal(snapshot) and not uncertain:
            return self.persist('held_terminal_failure', **self.evidence(snapshot))
        boundary = assistant['id'] + (':outage:' + str(snapshot['session']['time']['idle']) if outage_recovery else '')
        if self.state.get('failed_boundary') != boundary:
            self.state['failed_boundary'] = boundary
            delay = backoff(retry_count, max(30, policy.get('retry_base_seconds', 120)),
                            max(30, policy.get('retry_max_seconds', 1800)))
            # Structured session errors have no response headers in installed V2.
            # Preserve any explicit retry timestamp; gateway probes also honor HTTP Retry-After.
            retry_at = assistant.get('retry', {}).get('at', 0)
            self.state['retry_at'] = max(self.now() + delay,
                retry_at / 1000 if isinstance(retry_at, (int, float)) else 0,
                self.now() + retry_after(error.get('response', {}).get('headers', {}).get('Retry-After'), self.now()))
        wait_until = max(self.state.get('retry_at', 0), self.state.get('gateway_probe_at', 0))
        if self.now() < wait_until:
            return self.persist('provider_backoff', retry_at=wait_until, **self.evidence(snapshot))
        if self.reload().get('mode') != 'RUN':
            return self.persist('deferred_control_changed')
        result = self.api.probe_gateway(self.config, self.now())
        if self.reload().get('mode') != 'RUN' or not self.recovery_enabled():
            return self.persist('deferred_control_changed')
        self.state['last_gateway_probe'] = {'at': self.now(), **result}
        if result['kind'] in ('authentication', 'configuration'):
            return self.park('held_gateway_' + result['kind'])
        if result['kind'] != 'loaded':
            if uncertain and self.endpoint_down(result):
                self.state['terminal_outage_proof'] = {'boundary': boundary, 'at': self.now(),
                    'status': result.get('status'), 'error_type': error.get('type')}
            count = min(1000000, self.state.get('gateway_failures', 0) + 1)
            delay = max(result.get('retry_after_seconds', 0),
                        backoff(count, max(5, policy.get('probe_base_seconds', 30)),
                                max(30, policy.get('probe_max_seconds', 300))))
            self.state.update(gateway_failures=count, gateway_probe_at=self.now() + delay)
            return self.persist('waiting_for_pc_model', probe_kind=result['kind'], retry_at=self.state['gateway_probe_at'])
        self.state.update(gateway_failures=0, gateway_probe_at=0)
        if uncertain and self.state.get('terminal_outage_proof', {}).get('boundary') != boundary:
            return self.park('held_unclassified_provider_failure_while_healthy')
        return None

    @staticmethod
    def inference_phase(snapshot):
        assistant = snapshot['assistant']
        compaction = snapshot.get('compaction', {})
        created = compaction.get('time', {}).get('created')
        if (compaction.get('type') == 'compaction' and isinstance(created, (int, float))
                and created > assistant.get('time', {}).get('created', -1)):
            return compaction
        return assistant

    def phase_model_matches(self, snapshot):
        phase = self.inference_phase(snapshot)
        if phase.get('type') == 'assistant':
            return phase.get('model') == self.model
        agent = snapshot.get('compaction_agent', {})
        return (phase.get('reason') == 'auto' and phase.get('status') in ('running', 'failed')
                and snapshot['session'].get('model') == self.model
                and agent.get('id') == 'compaction' and agent.get('directory') == self.directory
                and agent.get('model') == self.model)

    @staticmethod
    def generation_fingerprint(snapshot):
        phase = Continuation.inference_phase(snapshot)
        return hashlib.sha256(json.dumps({key: phase.get(key) for key in
            ('id', 'type', 'time', 'content', 'finish', 'error', 'retry', 'status', 'reason', 'summary', 'recent')},
            sort_keys=True).encode()).hexdigest()

    def generation_only(self, snapshot):
        phase = self.inference_phase(snapshot)
        running = (phase.get('type') == 'assistant' and not phase.get('time', {}).get('completed')) or (
            phase.get('type') == 'compaction' and phase.get('reason') == 'auto' and phase.get('status') == 'running')
        return (running and self.phase_model_matches(snapshot)
                and self.known_queue(snapshot) and not snapshot['permissions'] and not snapshot['forms']
                and all(part.get('state', {}).get('status') in ('completed', 'error')
                        for part in snapshot['assistant'].get('content', []) if part.get('type') == 'tool'))

    def owned_outage_matches(self, snapshot):
        receipt = self.state.get('outage_interrupt', {})
        phase = self.inference_phase(snapshot)
        return (receipt.get('phase_id', receipt.get('assistant_id')) == phase.get('id')
                and receipt.get('phase_type', 'assistant') == phase.get('type')
                and self.phase_model_matches(snapshot)
                and receipt.get('status') == 'confirmed' and receipt.get('session_id') == self.sid
                and receipt.get('user_id') == snapshot['user'].get('id')
                and receipt.get('assistant_id') == snapshot['assistant'].get('id')
                and receipt.get('idle') == snapshot['session'].get('time', {}).get('idle')
                and snapshot['session'].get('outcome') == 'interrupted'
                and receipt.get('control_hash') == hashlib.sha256(Path(self.config['control_path']).read_bytes()).hexdigest())

    def observe_active_outage(self, snapshot):
        if (not self.generation_only(snapshot)
                or any(peer in snapshot['active'] for peer in self.config.get('other_programme_session_ids', []))
                or not self.phase_model_matches(snapshot)
                or self.inference_phase(snapshot).get('time', {}).get('created', -1) < snapshot['user'].get('time', {}).get('created', 0)):
            self.state.pop('active_outage_probe', None)
            return self.persist('running_tool_or_unqualified_generation')
        if self.state.get('outage_interrupt', {}).get('status') == 'intent':
            return self.persist('held_ambiguous_outage_interrupt')
        fingerprint = self.generation_fingerprint(snapshot)
        observation = self.state.get('active_outage_probe', {})
        if observation.get('fingerprint') != fingerprint:
            observation = {'fingerprint': fingerprint, 'unchanged_since': self.now(), 'failures': []}
            self.state['active_outage_probe'] = observation
        policy = self.config['recovery']['active_outage']
        stale = max(60, policy.get('stale_seconds', 120))
        if self.now() - observation['unchanged_since'] < stale or self.now() < observation.get('next_probe', 0):
            return self.persist('running_generation_observed')
        if self.reload().get('mode') != 'RUN':
            return self.persist('deferred_control_changed')
        probe = self.api.probe_gateway(self.config, self.now())
        observation['next_probe'] = self.now() + max(30, policy.get('probe_interval_seconds', 60), probe.get('retry_after_seconds', 0))
        if probe['kind'] in ('authentication', 'configuration'):
            return self.park('held_gateway_' + probe['kind'])
        if probe['kind'] not in ('unavailable', 'not_loaded'):
            observation['failures'] = []
            return self.persist('running_generation_gateway_' + probe['kind'], retry_at=observation['next_probe'])
        # Missing/not-loaded model alone is not proof the existing request is disconnected.
        if not self.endpoint_down(probe):
            observation['failures'] = []
            return self.persist('running_generation_gateway_uncertain', retry_at=observation['next_probe'])
        observation['failures'].append({'at': self.now(), 'status': probe.get('status')})
        observation['failures'] = observation['failures'][-5:]
        required = max(3, min(5, policy.get('unavailable_samples', 3)))
        if len(observation['failures']) < required:
            return self.persist('observed_pc_outage', samples=len(observation['failures']), retry_at=observation['next_probe'])
        if not self.execute:
            return self.persist('ready_outage_interrupt_observe_only')
        with lock(self.config['dispatch_lock_path']):
            if self.reload().get('mode') != 'RUN' or not self.recovery_enabled(active=True):
                return self.persist('deferred_control_changed')
            fresh = self.snapshot()
            if (self.admission_block(fresh) != 'running' or not self.generation_only(fresh)
                    or any(peer in fresh['active'] for peer in self.config.get('other_programme_session_ids', []))
                    or self.generation_fingerprint(fresh) != fingerprint
                    or fresh['user'].get('id') != snapshot['user'].get('id')):
                return self.persist('deferred_active_state_changed')
            receipt = {'status': 'intent', 'session_id': self.sid, 'user_id': fresh['user']['id'],
                       'assistant_id': fresh['assistant']['id'], 'phase_id': self.inference_phase(fresh)['id'],
                       'phase_type': self.inference_phase(fresh)['type'], 'fingerprint': fingerprint,
                       'network_observations': observation['failures'], 'at': self.now(),
                       'control_hash': hashlib.sha256(Path(self.config['control_path']).read_bytes()).hexdigest()}
            self.persist('outage_interrupt_intent', outage_interrupt=receipt)
            if self.reload().get('mode') != 'RUN' or not self.recovery_enabled(active=True):
                self.state.pop('outage_interrupt', None)
                return self.persist('deferred_control_changed')
            result = self.call('session.interrupt', resume='false')
            if result.get('interrupted') is not True:
                receipt['status'] = 'not_interrupted'
                return self.persist('outage_interrupt_noop', outage_interrupt=receipt)
            after = self.snapshot()
            if (self.sid in after['active'] or after['session'].get('outcome') != 'interrupted'
                    or after['user'].get('id') != receipt['user_id']
                    or after['assistant'].get('id') != receipt['assistant_id']
                    or self.inference_phase(after).get('id') != receipt['phase_id']
                    or self.inference_phase(after).get('type') != receipt['phase_type']
                    or not self.phase_model_matches(after)):
                return self.persist('held_ambiguous_outage_interrupt')
            receipt.update(status='confirmed', idle=after['session']['time'].get('idle'))
            return self.persist('outage_interrupted', outage_interrupt=receipt)

    def service_recovery_enabled(self):
        return (self.execute and self.config.get('enabled') is True
                and self.config.get('service_recovery', {}).get('enabled') is True
                and self.config.get('service_recovery', {}).get('exclusive_ownership_verified') is True)

    def cold_boot_start_authorized(self, last):
        return (self.config.get('service_recovery', {}).get('cold_boot_recovery') is True
                and self.boot_id is not None and last.get('boot_id') is not None
                and last['boot_id'] != self.boot_id
                and last.get('control_hash') == hashlib.sha256(Path(self.config['control_path']).read_bytes()).hexdigest())

    def recover_service(self, control):
        policy = self.config.get('service_recovery', {})
        if (not policy.get('enabled') or not self.execute or control.get('mode') != 'RUN'
                or self.config.get('enabled') is not True):
            return
        last = self.state.get('last_healthy_service')
        cold_boot = bool(last and self.cold_boot_start_authorized(last))
        if (policy.get('exclusive_ownership_verified') is not True or not last
                or set(last['active']) - {self.sid}
                or (self.now() - last['at'] > 600 and not cold_boot)
                or self.now() < self.state.get('service_start_after', 0)):
            self.state['service_recovery_status'] = 'held_ownership_or_backoff'
            return
        if not self.api.local_service_unavailable() or not self.api.opencode_process_absent():
            self.state['service_recovery_status'] = 'held_not_proven_absent'
            return
        # Supported start only. A failed API/authentication request never justifies a restart.
        with lock(self.config['dispatch_lock_path']):
            if (self.reload().get('mode') != 'RUN' or not self.service_recovery_enabled()
                    or (cold_boot and not self.cold_boot_start_authorized(last))):
                self.state['service_recovery_status'] = 'deferred_control_changed'
                return
            if cold_boot and (not self.api.local_service_unavailable() or not self.api.opencode_process_absent()):
                self.state['service_recovery_status'] = 'held_not_proven_absent'
                return
            self.state['service_start_after'] = self.now() + 300
            receipt = {'session_id': self.sid, 'kind': 'cold_boot' if cold_boot else 'recent_health',
                       'previous_boot_id': last.get('boot_id'), 'current_boot_id': self.boot_id, 'at': self.now()}
            self.persist('service_start_intent', service_start_receipt=receipt)
            if (self.reload().get('mode') != 'RUN' or not self.service_recovery_enabled()
                    or (cold_boot and not self.cold_boot_start_authorized(last))):
                self.state['service_recovery_status'] = 'deferred_control_changed'
                return
            self.api.start_service()
            self.state['service_recovery_status'] = 'supported_start_requested'
        # Do not infer that interrupted/incomplete model work was a crash: its exact
        # session outcome is re-read normally, and manual interruption remains held.

    def milestone_reminder(self):
        config = self.config.get('milestones')
        if not config:
            return ''
        def verified(entry):
            data = Path(entry['path']).read_bytes()
            if hashlib.sha256(data).hexdigest() != entry['sha256']:
                raise ValueError('Coordinator-owned milestone policy identity changed')
            return data
        plan = json.loads(verified(config['plan']))
        policy = verified(config['commit_policy']).decode('utf-8')
        verified(config['spec'])
        state = read(config['state_path'])
        rows = plan['milestones']
        current = state['current_milestone_id']
        ids = [row['id'] for row in rows]
        if len(set(ids)) != len(ids) or (current is not None and current not in ids):
            raise ValueError('Unknown or duplicate milestone identity')
        statuses = state.get('milestones', {})
        if any(dependency not in ids for row in rows for dependency in row.get('dependencies', [])):
            raise ValueError('Unknown milestone dependency')
        unfinished = [identity for identity in ids if statuses.get(identity, {}).get('implementation_status') != 'implemented']
        eligible = [row['id'] for row in rows if row['id'] in unfinished
                    and statuses.get(row['id'], {}).get('implementation_status') != 'blocked'
                    and all(statuses.get(dep, {}).get('implementation_status') == 'implemented'
                            or dep in statuses.get(row['id'], {}).get('dependency_evidence', {})
                            for dep in row.get('dependencies', []))]
        needs_selection = current is None and bool(eligible)
        self.state['milestones_awaiting_review'] = not unfinished
        self.state['milestone_dependency_blocked'] = bool(unfinished) and not eligible and current is None
        if needs_selection:
            current = eligible[0]
        next_id = next((identity for identity in eligible if identity != current), None)
        # Observe real git and durable checkpoint content at turn boundaries; never
        # trust an assistant's claim that it committed or completed acceptance.
        turns = self.state.get('turns_since_commit', 0)
        def git(*args):
            return subprocess.run(['git', *args], cwd=self.directory, capture_output=True,
                                  text=True, timeout=10, check=True).stdout
        head = git('rev-parse', 'HEAD').strip()
        raw_status = git('status', '--porcelain', '-z', '--untracked-files=normal')
        # Disposable evidence under work/ is not product dirt; tracked files there still count.
        status = '\0'.join(row for row in raw_status.split('\0') if not row.startswith('?? work/'))
        dirty = bool(status)
        diff_hash = hashlib.sha256((status + git('diff', '--binary', 'HEAD')).encode()).hexdigest()
        checkpoint = Path(config['checkpoint_path']).read_bytes()
        previous = self.state.get('checkpoint_head')
        if previous != head:
            if previous:
                git('merge-base', '--is-ancestor', previous, head)
            changed = git('diff-tree', '--root', '--no-commit-id', '--name-only', '-r', head).splitlines()
            if changed or previous is None:
                turns = 0
                self.state.update(checkpoint_head=head, last_commit_seen_at=self.now(), dirty_since=None)
        dirty_since = self.state.get('dirty_since')
        if dirty and dirty_since is None:
            dirty_since = self.now()
        if not dirty:
            dirty_since = None
        boundary = self.state.get('observed_turn_boundary')
        progress = self.state.get('progress_observation', {})
        if boundary and progress.get('boundary') != boundary:
            unchanged = progress.get('head') == head and progress.get('diff_sha256') == diff_hash
            self.state['unchanged_turns'] = self.state.get('unchanged_turns', 0) + 1 if unchanged else 0
            self.state['progress_observation'] = {'boundary': boundary, 'head': head, 'diff_sha256': diff_hash}
        self.state['focused_progress_due'] = self.state.get('unchanged_turns', 0) >= 3
        receipt_commit = statuses.get(current, {}).get('commit')
        self.state.update(turns_since_commit=turns, dirty_since=dirty_since,
                          checkpoint_observation={'at': self.now(), 'head': head, 'dirty': dirty,
                            'checkpoint_sha256': hashlib.sha256(checkpoint).hexdigest(),
                            'diff_sha256': diff_hash, 'milestone_receipt_matches_head': receipt_commit == head})
        limit = max(1, min(10, config.get('dirty_turn_limit', 3)))
        seconds = max(300, min(1800, config.get('dirty_seconds_limit', 1800)))
        due = dirty and (turns >= limit or self.now() - dirty_since >= seconds)
        self.state['checkpoint_commit_due'] = due
        focus = ('CHECKPOINT/COMMIT TURN ONLY: the dirty-work bound is reached. Do not expand implementation or advance '
                 'a milestone in this turn. Inspect actual staged/unstaged paths and ownership, run the bounded relevant '
                 'checks, make a scoped coherent or clearly labelled WIP commit with failures documented, and update the '
                 'durable checkpoint and milestone commit receipt. Never include another worker’s changes or secrets. '
                 'A concrete safety blocker must be recorded; never force clean or blanket git-add.\n') if due else ''
        if not due and self.state['focused_progress_due']:
            focus = ('BOUNDED PROGRESS TURN: several completed turns have left the observed HEAD/diff unchanged. '
                     'Choose one concrete dependency-ready requirement and perform one bounded diagnostic or implementation '
                     'step, retaining the actual result and next executable step. Do not produce another status-only reply. '
                     'Do not force an unsafe edit or call missing evidence acceptance.\n')
        return ('\n\nCOORDINATOR MILESTONE CONTINUATION\n' + focus
                + 'Current milestone: ' + str(current) + '; next dependency-eligible milestone: ' + str(next_id) + '.\n'
                + ('Use the approved helper next command to record the eligible current selection before implementation.\n' if needs_selection else '')
                + 'Unfinished milestone IDs: ' + ', '.join(unfinished) + '. Use the approved policy/helper to advance '
                + 'in plan order or skip a genuinely blocked lane for an independent eligible lane.\n'
                + 'Use the approved policy/helper to show the current milestone and next eligible entry from '
                + config['plan']['path'] + '; reread that entry’s canonical specification ranges in '
                + config['spec']['path'] + ', current state at ' + config['state_path']
                + ', and checkpoint at ' + config['checkpoint_path'] + '.\n'
                + 'Current git HEAD: ' + head + '; continuation turns without an observed new commit: '
                + str(turns) + '; bounded dirty-work cadence: ' + str(limit) + ' turns or ' + str(seconds) + ' seconds at a turn boundary.\n'
                + 'At a coherent validated checkpoint create a real new commit containing only owned reviewed files. '
                + 'If the cadence is reached, checkpoint/validate/commit owned coherent work before further expansion; '
                + 'explain a concrete blocker if a safe commit is impossible. Never blanket-add shared changes. '
                + 'Continue the next dependency-ready milestone in plan order; implementation completion is not independent acceptance.\n'
                + policy)



def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', default=str(Path(__file__).parent))
    parser.add_argument('--once', action='store_true')
    parser.add_argument('--execute', action='store_true', help='Permit dispatch only after coordinator activation')
    args = parser.parse_args()
    root = Path(args.root)
    root.mkdir(parents=True, exist_ok=True)
    try:
        with lock(root / 'continuation.lock'):
            worker = Continuation(root, execute=args.execute)
            while True:
                try:
                    worker.tick()
                except (OSError, ValueError, KeyError):
                    worker.persist('held_configuration_unavailable')
                if args.once or (worker.state.get('mode') == 'dispatch_limit_reached'
                                 and worker.config.get('exit_after_dispatch_limit') is True):
                    break
                time.sleep(5)
    except BlockingIOError:
        raise SystemExit('Existing continuation owner holds the lock; no action taken')


if __name__ == '__main__':
    main()
