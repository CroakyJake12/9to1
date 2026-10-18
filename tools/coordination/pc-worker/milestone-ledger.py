#!/usr/bin/env python3
"""Durable sequential implementation ledger; never grants independent acceptance."""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import subprocess


def read(path):
    return json.loads(Path(path).read_text())


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(path, value):
    path = Path(path)
    tmp = path.with_suffix(path.suffix + '.tmp')
    with tmp.open('w') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(tmp, path)


def choose(plan, state):
    for milestone in plan['milestones']:
        row = state['milestones'][milestone['id']]
        if row['implementation_status'] not in ('pending', 'in_progress'):
            continue
        if all(state['milestones'][dep]['implementation_status'] == 'implemented'
               or dep in row.get('dependency_evidence', {})
               for dep in milestone.get('dependencies', [])):
            return milestone['id']
    return None


def git(directory, *args):
    return subprocess.check_output(['git', '-C', str(directory), *args], text=True).strip()


def passing_checks(checks):
    return bool(checks) and all(check.get('result') == 'PASS'
                               and check.get('exit_code') == 0
                               and isinstance(check.get('command'), str)
                               and check['command'].strip() for check in checks)


def require_committed_source(worker):
    if git(worker, 'diff', '--name-only') or git(worker, 'diff', '--cached', '--name-only'):
        raise ValueError('Commit coherent tracked changes before attributing evidence to HEAD')
    untracked = git(worker, 'ls-files', '--others', '--exclude-standard').splitlines()
    if any(not path.startswith('work/') for path in untracked):
        raise ValueError('Untracked source remains; reconcile without deleting it')


def update(root, action, record_path=None):
    root = Path(root).resolve()
    config = read(root / 'worker-loop/config.json')
    policy = config['milestones']
    plan_path = Path(policy['plan']['path'])
    if digest(plan_path) != policy['plan']['sha256']:
        raise ValueError('Plan identity changed; coordinator reconciliation required')
    if digest(policy['spec']['path']) != policy['spec']['sha256']:
        raise ValueError('Canonical specification identity changed')
    control = read(config['control_path'])
    if action not in ('status', 'show') and control.get('mode') != 'RUN':
        raise ValueError('Owner control is not RUN')
    plan = read(plan_path)
    state_path = Path(policy['state_path'])
    with state_path.with_suffix('.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        state = read(state_path)
        current = state.get('current_milestone_id')
        now = dt.datetime.now(dt.timezone.utc).isoformat()
        if action == 'show':
            return {'current': next((m for m in plan['milestones'] if m['id'] == current), None),
                    'state': state['milestones'].get(current),
                    'source': plan['canonical_spec'],
                    'policy': str(root / 'COMMIT-AND-MILESTONE-POLICY.md')}
        if action == 'status':
            return {'current_milestone_id': current,
                    'next_eligible': choose(plan, state),
                    'milestones': state['milestones']}
        if action == 'next':
            if current and state['milestones'][current]['implementation_status'] == 'in_progress':
                return {'current_milestone_id': current, 'reason': 'finish or explicitly block current slice first'}
            selected = choose(plan, state)
            state['current_milestone_id'] = selected
            if selected:
                state['milestones'][selected]['implementation_status'] = 'in_progress'
            state['updated_at'] = now
            save(state_path, state)
            return {'current_milestone_id': selected,
                    'reason': 'selected' if selected else 'no eligible implementation; acceptance/blockers remain'}
        if not record_path:
            raise ValueError('A durable JSON record is required')
        record_path = Path(record_path).resolve()
        worker = Path(config['directory']).resolve()
        if not record_path.is_relative_to(worker / 'work/pc-worker'):
            raise ValueError('Record must be in the worker evidence directory')
        record = read(record_path)
        if action == 'dependency':
            target = record.get('milestone_id')
            dependency = record.get('dependency_id')
            milestone = next((m for m in plan['milestones'] if m['id'] == target), None)
            if not milestone or dependency not in milestone.get('dependencies', []):
                raise ValueError('Unknown target or functional dependency')
            if record.get('spec_sha256') != policy['spec']['sha256'] or not record.get('summary'):
                raise ValueError('Scoped dependency receipt must identify its source and functional contract')
            checks = record.get('checks', [])
            evidence_paths = [Path(p).resolve() for p in record.get('evidence', [])]
            if not passing_checks(checks) or not evidence_paths:
                raise ValueError('Scoped dependency needs actual focused checks and evidence')
            if any(not p.is_file() or not p.stat().st_size for p in evidence_paths):
                raise ValueError('Dependency evidence is unavailable')
            if git(worker, 'branch', '--show-current') != plan['worker_branch']:
                raise ValueError('Unexpected implementation branch')
            require_committed_source(worker)
            receipt = {'at': now, 'commit': git(worker, 'rev-parse', 'HEAD'),
                       'record_path': str(record_path), 'record_sha256': digest(record_path),
                       'evidence': [{'path': str(p), 'sha256': digest(p)} for p in evidence_paths],
                       'scope': record['summary'], 'acceptance_status': 'NOT_RUN'}
            state['milestones'][target].setdefault('dependency_evidence', {})[dependency] = receipt
            state.setdefault('history', []).append({'action': action, 'milestone_id': target,
                                                    'dependency_id': dependency, **receipt})
            state['updated_at'] = now
            save(state_path, state)
            return {'milestone_id': target, 'dependency_id': dependency,
                    'result': 'scoped implementation prerequisite recorded; full acceptance unchanged'}
        if record.get('milestone_id') != current or current not in state['milestones']:
            raise ValueError('Record must name the exact current milestone')
        if record.get('spec_sha256') != policy['spec']['sha256']:
            raise ValueError('Record must identify the canonical specification')
        for key in ('summary', 'next_action', 'read_ranges'):
            if not record.get(key):
                raise ValueError('Missing record field: ' + key)
        branch = git(worker, 'branch', '--show-current')
        if branch != plan['worker_branch']:
            raise ValueError('Unexpected implementation branch')
        head = git(worker, 'rev-parse', 'HEAD')
        row = state['milestones'][current]
        if row['implementation_status'] == 'implemented':
            raise ValueError('Completed implementation cannot be rewritten; use a corrective milestone')
        evidence = []
        for value in record.get('evidence', []):
            path = Path(value).resolve()
            if not path.is_file() or not path.stat().st_size:
                raise ValueError('Evidence must be an existing nonempty file')
            evidence.append({'path': str(path), 'sha256': digest(path)})
        if action == 'finish':
            require_committed_source(worker)
            if not evidence or not record.get('checks'):
                raise ValueError('Actual smoke checks and saved evidence required')
            if not passing_checks(record['checks']):
                raise ValueError('Failed/unrun mandatory checks remain a checkpoint or blocker')
            manifest_path = Path(record.get('requirement_manifest', '')).resolve()
            if not manifest_path.is_relative_to(worker / 'work/pc-worker') or not manifest_path.is_file():
                raise ValueError('A scoped requirement manifest is required')
            manifest = read(manifest_path)
            if manifest.get('milestone_id') != current or manifest.get('spec_sha256') != policy['spec']['sha256']:
                raise ValueError('Manifest identity mismatch')
            requirements = manifest.get('requirements', [])
            if not requirements or any(item.get('implementation_status') != 'implemented' for item in requirements):
                raise ValueError('Scoped requirements remain unfinished; do not narrow the milestone')
            if record.get('unresolved_requirements'):
                raise ValueError('Unresolved mandatory requirements cannot be marked implemented')
            row.update(implementation_status='implemented', acceptance_status='PENDING_REVIEW',
                       requirement_manifest={'path': str(manifest_path), 'sha256': digest(manifest_path)})
        elif action == 'block':
            if not record.get('blocker') or not record.get('unblock_condition'):
                raise ValueError('Record exact blocker and unblock condition')
            row.update(implementation_status='blocked', blocker=record['blocker'],
                       unblock_condition=record['unblock_condition'])
        else:
            row['implementation_status'] = 'in_progress'
        row.update(commit=head, branch=branch, evidence=evidence, updated_at=now,
                   checkpoint={'path': str(record_path), 'sha256': digest(record_path)},
                   next_action=record['next_action'])
        event = {'at': now, 'action': action, 'milestone_id': current, 'commit': head,
                 'record_path': str(record_path), 'record_sha256': digest(record_path)}
        state.setdefault('history', []).append(event)
        if action in ('finish', 'block'):
            state['current_milestone_id'] = choose(plan, state)
            if state['current_milestone_id']:
                state['milestones'][state['current_milestone_id']]['implementation_status'] = 'in_progress'
        state['updated_at'] = now
        save(state_path, state)
        return event | {'next_milestone_id': state['current_milestone_id'],
                        'acceptance_status': row['acceptance_status']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('action', choices=['status', 'show', 'checkpoint', 'finish', 'block', 'next', 'dependency'])
    parser.add_argument('--record', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(update(args.root, args.action, args.record), indent=2))
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, str(error) + '\n')
