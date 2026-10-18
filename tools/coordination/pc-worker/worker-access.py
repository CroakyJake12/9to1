#!/usr/bin/env python3
"""Open or explicitly resume the one saved 9to1 worker; never create a programme."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
from urllib.parse import urlparse


class Held(RuntimeError):
    pass


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def gui_summary(result, control, url):
    return ('Worker: ' + ('active' if result.get('worker_active') else str(result.get('outcome') or result.get('status') or 'idle/unavailable'))
            + '\nControl: ' + str(result.get('control') or control)
            + ('\nPC model: ' + str(result['pc_model']) if 'pc_model' in result else '') + '\n' + url)


def load_module(path, expected):
    if digest(path) != expected:
        raise Held('Approved continuation code changed; review the access manifest.')
    spec = importlib.util.spec_from_file_location('worker_access_continuation', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Desktop:
    def unit(self, name, start=False):
        args = ['systemctl', '--user', 'start', name] if start else ['systemctl', '--user', 'is-active', name]
        result = subprocess.run(args, capture_output=True, text=True, timeout=20)
        if start and result.returncode:
            raise Held('The continuation service could not start. Open the recovery guide.')
        return result.returncode == 0

    def open(self, target):
        result = subprocess.run(['xdg-open', target], capture_output=True, text=True, timeout=15)
        if result.returncode:
            raise Held('The desktop could not open this address. Open the recovery guide.')


class Access:
    def __init__(self, manifest_path, module=None, api=None, desktop=None, now=time.time):
        self.manifest_path = Path(manifest_path)
        self.manifest = json.loads(self.manifest_path.read_text())
        self.manifest_hash = digest(self.manifest_path)
        m = self.manifest
        self.module = module or load_module(m['continuation_module'], m['continuation_sha256'])
        self.root = Path(m['loop_root'])
        self.access_state = self.manifest_path.with_name('worker-access-state.json')
        self.desktop, self.now = desktop or Desktop(), now
        self.worker = self.module.Continuation(self.root, api=api, execute=False, now=now)
        c = self.worker.config
        if any(c.get(k) != m['identity'].get(k) for k in ('session_id', 'model', 'directory', 'server')):
            raise Held('Saved worker identity changed; do not open another programme.')
        url, server = urlparse(m['worker_url']), urlparse(c['server'])
        if (url.scheme != 'http' or url.netloc != server.netloc or url.username or url.password
                or url.query or url.fragment or not url.path.endswith('/session/' + self.worker.sid)
                or m['unit'] != '9to1-pc-worker-continuation.service'
                or not Path(m['guide_path']).is_absolute()):
            raise Held('Invalid saved worker address or service identity.')

    def reload(self):
        if digest(self.manifest_path) != self.manifest_hash:
            raise Held('Access configuration changed during the operation.')
        control = self.worker.reload()
        state_path = self.root / 'continuation-state.json'
        if state_path.exists():
            self.worker.state = self.module.read(state_path)
            for key, value in (('session_id', self.worker.sid), ('directory', self.worker.directory), ('model', self.worker.model)):
                if self.worker.state.get(key) != value:
                    raise Held('Durable worker identity changed.')
        return control

    def record(self, **values):
        self.module.save(self.access_state, values)
        return values

    def status(self):
        control = self.reload()
        result = {'session_id': self.worker.sid, 'control': control.get('mode'),
                  'monitor_active': self.desktop.unit(self.manifest['unit']),
                  'monitor_status': self.worker.state.get('mode'), 'worker_url': self.manifest['worker_url']}
        try:
            snapshot = self.worker.snapshot()
            result.update(service='available', worker_active=self.worker.sid in snapshot['active'],
                          outcome=snapshot['session'].get('outcome'),
                          pending_requests=len(snapshot['permissions']) + len(snapshot['forms']))
        except Exception as error:
            result.update(service='unavailable', error_type=type(error).__name__)
        result['pc_model'] = self.worker.api.probe_gateway(self.worker.config, self.now()).get('kind', 'unavailable')
        return result

    def ensure_service(self, explicit_resume=False):
        control = self.reload()
        initial_control_hash = digest(self.worker.config['control_path'])
        try:
            self.worker.snapshot()
            return
        except Exception:
            pass
        c, api = self.worker.config, self.worker.api
        policy = c.get('service_recovery', {})
        prior_health = self.worker.state.get('last_healthy_service')
        if not prior_health or set(prior_health.get('active', [])) - {self.worker.sid}:
            raise Held('Prior service ownership is not verified; no shared service will be started.')
        if (control.get('mode') not in (('RUN', 'STOP', 'PAUSE') if explicit_resume else ('RUN',)) or c.get('enabled') is not True
                or policy.get('enabled') is not True or policy.get('exclusive_ownership_verified') is not True):
            raise Held('OpenCode is unavailable. Automatic start is not authorised in this control state.')
        if not api.local_service_unavailable() or not api.opencode_process_absent():
            raise Held('An existing or uncertain OpenCode service must not be restarted.')
        with self.module.lock(c['dispatch_lock_path']):
            fresh = self.reload()
            policy = self.worker.config.get('service_recovery', {})
            if (digest(self.worker.config['control_path']) != initial_control_hash
                    or fresh.get('mode') not in (('RUN', 'STOP', 'PAUSE') if explicit_resume else ('RUN',)) or self.worker.config.get('enabled') is not True
                    or policy.get('enabled') is not True or policy.get('exclusive_ownership_verified') is not True
                    or not api.local_service_unavailable() or not api.opencode_process_absent()):
                raise Held('Service ownership or control changed; no service was started.')
            self.module.save(self.manifest_path.with_name('worker-access-service.json'),
                             {'phase': 'intent', 'session_id': self.worker.sid, 'at': self.now()})
            # Persisted intent is followed by one last control check, never a shared restart.
            fresh = self.reload()
            policy = self.worker.config.get('service_recovery', {})
            if (digest(self.worker.config['control_path']) != initial_control_hash
                    or fresh.get('mode') not in (('RUN', 'STOP', 'PAUSE') if explicit_resume else ('RUN',)) or self.worker.config.get('enabled') is not True
                    or policy.get('enabled') is not True or policy.get('exclusive_ownership_verified') is not True):
                raise Held('Service start was cancelled by a control change.')
            api.start_service()
            self.module.save(self.manifest_path.with_name('worker-access-service.json'),
                             {'phase': 'requested', 'session_id': self.worker.sid, 'at': self.now()})
        # A start acknowledgement is not proof of session health.
        self.worker.snapshot()

    def open_worker(self):
        self.ensure_service()
        if self.reload().get('mode') == 'RUN':
            self.desktop.unit(self.manifest['unit'], start=True)
        self.desktop.open(self.manifest['worker_url'])
        return {'status': 'opened', 'worker_url': self.manifest['worker_url']}

    def clean_marker(self, intent):
        marker = Path(intent['marker_path'])
        if marker.exists() and digest(marker) == intent['marker_sha256']:
            marker.unlink()

    def reconcile(self, intent):
        if intent.get('kind') != 'resume' or intent.get('phase') == 'acknowledged':
            return None
        try:
            message = self.worker.call('session.message.get', messageID=intent['message_id'])['data']
        except Exception:
            raise Held('A previous resume has uncertain acknowledgement. It has not been sent again.') from None
        if (message.get('id') != intent['message_id'] or message.get('type') != 'user'
                or message.get('metadata', message.get('payload', {}).get('metadata', {})).get('pc_owner_resume') != intent['boundary']):
            raise Held('The previous resume could not be safely reconciled; no prompt was sent.')
        self.clean_marker(intent)
        self.record(**{**intent, 'phase': 'acknowledged'})
        return {'status': 'previous_resume_reconciled', 'message_id': intent['message_id']}

    def resume_gate(self, snapshot):
        w = self.worker
        if w.config.get('enabled') is not True:
            raise Held('The saved programme is disabled; it requires coordinator review.')
        parked = w.state.get('parked', {})
        recoverable_parks = {'held_configuration_unavailable', 'held_local_api_authentication', 'held_local_api_configuration',
                             'held_provider_authentication', 'held_provider_configuration',
                             'held_gateway_authentication', 'held_gateway_configuration'}
        if (parked and parked.get('epoch') == w.config.get('recovery_epoch', 0)
                and parked.get('mode') not in recoverable_parks):
            raise Held('The monitor has an unresolved diagnostic hold; coordinator review is required.')
        if self.now() < max(w.state.get('retry_at', 0), w.state.get('gateway_probe_at', 0)):
            raise Held('A provider backoff is still in effect. Wait before resuming.')
        if w.state.get('pending') or w.state.get('outage_interrupt', {}).get('status') == 'intent':
            raise Held('An earlier dispatch or interruption is ambiguous; open the recovery guide.')
        if (snapshot['session'].get('model') != w.model or snapshot['active']
                or snapshot['permissions'] or snapshot['forms']):
            raise Held('The worker, another session, or a pending request prevents a safe resume.')
        approved = set(w.config.get('approved_user_message_ids', [])) | set(w.state.get('dispatched', []))
        if (snapshot['user'].get('id') not in approved
                or any(item.get('type') != 'user' or item.get('id') not in approved for item in snapshot['inbox'])):
            raise Held('New owner instructions must be reviewed before resuming.')
        if any(p.get('state', {}).get('status') not in ('completed', 'error')
               for p in snapshot['assistant'].get('content', []) if p.get('type') == 'tool'):
            raise Held('An unfinished tool operation prevents a safe resume.')
        if snapshot['session'].get('outcome') not in ('interrupted', 'failed', 'succeeded'):
            raise Held('The saved session has no supported terminal state.')

    def approved_text(self):
        w = self.worker
        texts = []
        for name in ('continuation_prompt', 'retry_prompt'):
            item = w.config[name]
            raw = Path(item['path']).read_bytes()
            if hashlib.sha256(raw).hexdigest() != item['sha256'] or not raw.strip():
                raise Held('An approved continuation instruction changed.')
            texts.append(raw.decode('utf-8'))
        extra = w.milestone_reminder()
        if w.state.get('milestones_awaiting_review') or w.state.get('milestone_dependency_blocked'):
            raise Held('The milestone queue needs independent review; no new work was dispatched.')
        return ('The owner explicitly selected Resume saved work. Resume this exact existing programme, '
                'preserving its specification, corrections, checkpoint, ownership and acceptance requirements. '
                'A prior interruption is not completion. Do not create a new programme or switch models.\n\n'
                + '\n\n'.join(texts) + extra)

    def resume(self):
        # Explicit invocation is authorisation to change only mode/hold_only; all other owner fields survive.
        with self.module.lock(self.manifest_path.with_name('worker-access.lock')):
            self.ensure_service(explicit_resume=True)
            with self.module.lock(self.worker.config['dispatch_lock_path']):
                control = self.reload()
                if control.get('mode') not in ('RUN', 'STOP', 'PAUSE'):
                    raise Held('The control file is invalid.')
                prior = self.module.read(self.access_state) if self.access_state.exists() else {}
                reconciled = self.reconcile(prior)
                if reconciled:
                    if self.reload().get('mode') == 'RUN':
                        self.desktop.unit(self.manifest['unit'], start=True)
                    return reconciled
                snapshot = self.worker.snapshot()
                if self.worker.sid in snapshot['active']:
                    return {'status': 'already_active', 'message': 'The saved worker is already running; no prompt was added.'}
                self.resume_gate(snapshot)
                marker = Path(self.worker.config['coordinator_marker_path'])
                if marker.exists():
                    raise Held('The worker has a coordinator hold. Open the guide before resuming.')
                identity = json.dumps(self.worker.evidence(snapshot), sort_keys=True)
                boundary = hashlib.sha256((self.worker.sid + identity).encode()).hexdigest()
                message_id = 'msg_access' + boundary[:24]
                if prior.get('message_id') == message_id and prior.get('phase') == 'acknowledged':
                    return {'status': 'already_resumed', 'message_id': message_id}
                fingerprints = self.worker.prompt_inputs(True)
                control_hash = digest(self.worker.config['control_path'])
                text = self.approved_text()
                if self.worker.api.probe_gateway(self.worker.config, self.now()).get('kind') != 'loaded':
                    raise Held('The PC model is not ready. Nothing was resumed; try again after it finishes starting.')
                # No inference occurs in readiness/model catalog checks.
                models = self.worker.api.request('model.list', {'location[directory]': self.worker.directory})['data']
                model = next((m for m in models if m.get('id') == self.worker.model['id'] and m.get('providerID') == self.worker.model['providerID']), None)
                if not (self.worker.config.get('free_usage_verified') is True and model and model.get('enabled') is True
                        and model.get('status') != 'deprecated' and (self.module.free_model(model)
                            or (self.worker.config.get('pricing_basis') == 'owner_owned_pc'
                                and self.worker.config.get('gateway_base_url') == 'https://ai.9to1.uk/v1'
                                and model.get('cost') == []))):
                    raise Held('The approved free PC model could not be verified.')
                self.reload()
                fresh = self.worker.snapshot()
                self.resume_gate(fresh)
                if (fresh['inbox'] != snapshot['inbox'] or self.worker.evidence(fresh) != self.worker.evidence(snapshot)
                        or self.worker.prompt_inputs(True) != fingerprints
                        or digest(self.worker.config['control_path']) != control_hash or marker.exists()):
                    raise Held('Work or owner control changed during readiness checks; nothing was resumed.')
                marker_value = {'kind': 'explicit_owner_resume_pending', 'message_id': message_id,
                                'session_id': self.worker.sid, 'at': self.now()}
                # This exclusively-created marker makes the normal monitor wait without touching its state.
                with marker.open('x') as stream:
                    json.dump(marker_value, stream); stream.flush(); os.fsync(stream.fileno())
                marker.chmod(0o600)
                epoch_before = self.worker.config.get('recovery_epoch', 0)
                parked = self.worker.state.get('parked', {})
                epoch_after = epoch_before + 1 if parked and parked.get('epoch') == epoch_before else epoch_before
                intent = {'recovery_epoch_before': epoch_before, 'recovery_epoch_after': epoch_after,
                          'kind': 'resume', 'phase': 'intent', 'message_id': message_id, 'boundary': boundary,
                          'session_id': self.worker.sid, 'at': self.now(), 'marker_path': str(marker),
                          'marker_sha256': digest(marker), 'text_sha256': hashlib.sha256(text.encode()).hexdigest()}
                self.record(**intent)
                self.reload()
                if (digest(self.worker.config['control_path']) != control_hash
                        or self.worker.prompt_inputs(True) != fingerprints):
                    raise Held('Owner instructions changed after the intent; nothing was resumed.')
                config = self.module.read(self.worker.config_path)
                if config != self.worker.config:
                    raise Held('Owner configuration changed; the resume hold remains.')
                parked = self.worker.state.get('parked', {})
                if parked and parked.get('epoch') == config.get('recovery_epoch', 0):
                    config['recovery_epoch'] = intent['recovery_epoch_after']
                config['approved_user_message_ids'] = list(dict.fromkeys(config.get('approved_user_message_ids', []) + [message_id]))
                self.module.save(self.worker.config_path, config)
                if digest(self.worker.config['control_path']) != control_hash:
                    raise Held('Owner control changed; the resume hold remains.')
                updated_control = {**control, 'mode': 'RUN', 'hold_only': False}
                self.module.save(self.worker.config['control_path'], updated_control)
                expected_control = digest(self.worker.config['control_path'])
                current_control = self.reload()
                if (current_control != updated_control or self.worker.config != config or digest(self.worker.config['control_path']) != expected_control
                        or digest(marker) != intent['marker_sha256']):
                    raise Held('Control changed after the resume intent; the safety hold remains.')
                last = self.worker.snapshot()
                self.resume_gate(last)
                if last['inbox'] != snapshot['inbox'] or self.worker.evidence(last) != self.worker.evidence(snapshot):
                    raise Held('The saved session changed after the intent; no prompt was sent.')
                if self.reload() != updated_control or self.worker.config != config:
                    raise Held('Owner control changed before dispatch; no prompt was sent.')
                # Preserve ambiguous mutation intent. Never retry an uncertain session.prompt.
                result = self.worker.api.request('session.prompt', {'sessionID': self.worker.sid}, {
                    'id': message_id, 'text': text, 'delivery': 'queue', 'resume': True,
                    'metadata': {'pc_owner_resume': boundary, 'approved_prompt_sha256': intent['text_sha256']}})
                if result.get('data', {}).get('id') != message_id:
                    raise Held('Resume acknowledgement was uncertain. The safety hold remains; no automatic resend.')
                self.clean_marker(intent)
                self.record(**{**intent, 'phase': 'acknowledged'})
            self.desktop.unit(self.manifest['unit'], start=True)
            return {'status': 'resumed_existing_work', 'message_id': message_id}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', nargs='?', choices=('status', 'open', 'resume', 'guide'))
    parser.add_argument('--manifest', default=str(Path(__file__).with_name('worker-access.json')))
    args = parser.parse_args()
    command = args.command
    if command is None:
        if not shutil.which('zenity'):
            parser.error('Choose status, open, resume or guide (desktop chooser is unavailable).')
        result = subprocess.run(['zenity', '--list', '--title=9to1 worker', '--column=Action',
                                 'Open worker', 'Status', 'Resume saved work', 'Open guide'], capture_output=True, text=True)
        if result.returncode:
            return
        command = {'Open worker': 'open', 'Status': 'status', 'Resume saved work': 'resume', 'Open guide': 'guide'}.get(result.stdout.strip())
        if command is None:
            return
    try:
        if command == 'guide':
            manifest = json.loads(Path(args.manifest).read_text())
            Desktop().open(manifest['guide_path'])
            return
        access = Access(args.manifest)
        if command == 'status': result = access.status()
        elif command == 'open': result = access.open_worker()
        elif command == 'resume': result = access.resume()
        else:
            access.desktop.open(access.manifest['guide_path']); result = {'status': 'guide_opened'}
        if args.command is None and shutil.which('zenity'):
            summary = gui_summary(result, access.reload().get('mode'), access.manifest['worker_url'])
            subprocess.run(['zenity', '--info', '--title=9to1 worker', '--text=' + summary], capture_output=True)
        else:
            print(json.dumps(result, indent=2))
    except Exception as error:
        message = str(error) if isinstance(error, Held) else 'The saved worker could not be safely accessed (' + type(error).__name__ + '). Open the recovery guide.'
        if args.command is None and shutil.which('zenity'):
            subprocess.run(['zenity', '--error', '--title=9to1 worker', '--text=' + message], capture_output=True)
        else:
            print(message)
        raise SystemExit(1)


if __name__ == '__main__':
    main()
