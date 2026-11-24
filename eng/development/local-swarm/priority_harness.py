#!/usr/bin/env python3
"""Deadline-safe dispatcher for the existing guarded Dulche development harness.

Task ownership comes from a reviewed local manifest. Runtime/model loading remains
inside Dulche. Existing task histories, candidates and publication checks survive.
"""
import collections
import concurrent.futures
import copy
import datetime
import fcntl
import importlib.util
import json
import os
from pathlib import Path
import signal
import sys
import threading
import time
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parent
CONFIG = ROOT / 'priority-config.json'
TZ = ZoneInfo('Europe/London')
START_BY = datetime.datetime(2026, 11, 24, 21, 20, tzinfo=TZ).timestamp()
GROUPS = ('P0 Spaces and Assistants', 'P0 Dulche', 'P0 Build', 'P0 Files',
          'P1 productivity', 'P1 Training Lab UI')
INKLING = 'inkling-2-mini-strata-ud-q4-k-xl'


def load_harness():
    spec = importlib.util.spec_from_file_location('owned_swarm_harness', ROOT / 'harness.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_config(path=CONFIG):
    config = json.loads(path.read_text())
    if config.get('schema') != 1 or not isinstance(config.get('tasks'), list):
        raise ValueError('Invalid priority task manifest')
    ids = set()
    owners = set()
    for item in config['tasks']:
        if item.get('group') not in GROUPS:
            raise ValueError('Unknown priority group')
        task = item.get('task')
        if not isinstance(task, list) or len(task) != 6 or not all(isinstance(x, str) and x for x in task):
            raise ValueError('Each task needs id, source, test, project, filter and read scope')
        if task[0] in ids:
            raise ValueError('Duplicate task identity')
        ids.add(task[0])
        if not isinstance(item.get('ticket_id'), str) or not item['ticket_id'].startswith('Q'):
            raise ValueError('A canonical queue ticket is required')
        for path_name in task[1:4]:
            p = Path(path_name)
            if p.is_absolute() or '..' in p.parts or not path_name.startswith('9to1 Workspace/'):
                raise ValueError('Task path must remain in the approved source tree')
        for path_name in task[1:3]:
            if path_name in owners:
                raise ValueError('Priority tasks cannot share owned write paths')
            owners.add(path_name)
    config['tasks'].sort(key=lambda x: (GROUPS.index(x['group']), x.get('order', 0)))
    return config


def qualified_inkling(h):
    """A catalogue name alone is not inference qualification."""
    receipt_path = ROOT / 'inkling-qualification.json'
    try:
        receipt = json.loads(receipt_path.read_text())
        if receipt.get('model') != INKLING or receipt.get('passed') is not True:
            return None
        context = receipt.get('context_size')
        if type(context) is not int or context < 8192:
            return None
        row = h.safe_model(INKLING)
        if (row.get('id') != INKLING or row.get('ownership') != 'dulche'
                or row.get('engine') != 'Native Strata / Inkling-2-Mini'
                or row.get('gpu') != 'RTX 5080' or row.get('context', 0) < context
                or row.get('slots') != 1):
            return None
        run = h.api('GET', '/v1/agents/' + receipt['generation_run'])
        steps = run.get('completed_steps') or []
        content = (((steps[-1].get('choices') or [{}])[0].get('message') or {}).get('content') or '') if steps else ''
        if run.get('state') != 'completed' or not (run.get('output', '') or content).strip():
            return None
        if run.get('request', {}).get('model') != INKLING:
            return None
        return {'model': INKLING, 'context_budget': min(30000, context - 256), 'slots': 1,
                'coexistence_v100': receipt.get('coexistence_v100') is True}
    except (OSError, ValueError, KeyError, RuntimeError):
        return None


def loaded_models(h):
    inkling = qualified_inkling(h)
    if inkling:
        available = [inkling]
        if inkling.pop('coexistence_v100', False):
            try:
                h.safe_model(h.V100_MODEL)
                available.append({'model': h.V100_MODEL, 'context_budget': 30000, 'slots': 1})
            except (OSError, ValueError, RuntimeError):
                pass
        return available
    available = []
    for model in (h.MODEL, h.V100_MODEL):
        try:
            row = h.safe_model(model)
            available.append({'model': model, 'context_budget': 30000, 'slots': 1})
        except (OSError, ValueError, RuntimeError) as exc:
            h.event('priority_model_unavailable', model=model, reason=str(exc)[:400])
    return available


def prepare(h):
    """Called before the deadline, independently of the Inkling installation."""
    available = loaded_models(h)
    if available:
        h.atomic(ROOT / 'priority-ready.json', {'at': time.time(), 'models': available, 'ready': True})
        return available
    h.resource_check()
    if read_config().get('fallback_load_allowed') is not True:
        raise RuntimeError('No loaded fallback is available; runtime changes remain with the active runtime owner')
    h.event('deadline_fallback_load', model=h.MODEL, start_by=START_BY)
    h.api('POST', '/v1/models/' + h.MODEL + '/load', {}, timeout=30)
    until = time.monotonic() + 180
    while time.monotonic() < until and not h.STOP.is_set():
        available = loaded_models(h)
        if available:
            h.atomic(ROOT / 'priority-ready.json', {'at': time.time(), 'models': available, 'ready': True})
            return available
        h.STOP.wait(2)
    raise RuntimeError('Dulche has no qualified loaded model before the development deadline')


def task_ready(item, state):
    saved = state.get('tasks', {}).get(item['task'][0], {})
    return saved.get('state') not in ('verified_candidate', 'no_change', 'blocked', 'ownership_blocked')


def mapped_tickets(h, config):
    canonical = {x['ticket_id']: x for x in json.loads(h.QUEUE.read_text())['tickets']}
    tickets = dict(canonical)
    register = None
    for item in config['tasks']:
        ticket = copy.deepcopy(canonical[item['ticket_id']])
        if item.get('requirement_id'):
            selected = item['requirement_id']
            if selected not in ticket.get('row_ids', []):
                raise ValueError('Selected requirement does not belong to its allocated ticket')
            if register is None:
                register = json.loads((Path.home() / 'Documents/GitHub/9to1-night-20261124/atlas-v4-live-register-20261124/register.json').read_text())['rows']
            matches = [row for row in register if row.get('id') == selected]
            if len(matches) != 1:
                raise ValueError('Selected requirement must have one authoritative register row')
            row = matches[0]
            ticket['first_action'] = {key: row[key] for key in ('id', 'requirement', 'source', 'acceptance')}
            ticket['first_action']['acceptance_origin'] = 'Authoritative register row allocated to this ticket'
        ticket['ticket_id'] = item['task'][0]
        ticket['first_action']['priority_group'] = item['group']
        ticket['first_action']['scoped_work'] = item.get('focused_slice', '')
        ticket['first_action']['acceptance_limit'] = 'Scoped code and tests only; full native acceptance remains unverified.'
        tickets[item['task'][0]] = ticket
    return tickets


def run_task(h, model, item, base, tickets):
    h.LANE.model = model['model']
    h.LANE.priority_context_budget = model['context_budget']
    h.LANE.read_only_limit = 12
    h.update(item['task'][0], priority_group=item['group'], canonical_ticket=item['ticket_id'])
    h.event('priority_task_start', task=item['task'][0], ticket=item['ticket_id'],
            priority_group=item['group'], model=model['model'])
    h.process(tuple(item['task']), base, tickets)


def run_priority(h, config):
    state = json.loads(h.STATE.read_text()) if h.STATE.exists() else {
        'base': h.git('rev-parse', 'HEAD').stdout.strip(), 'branch': h.BRANCH, 'tasks': {},
        'cutoff': h.CUTOFF, 'deadline': h.DEADLINE}
    base = state['base']
    for item in config['tasks']:
        state['tasks'].setdefault(item['task'][0], {
            'state': 'queued', 'priority_group': item['group'], 'canonical_ticket': item['ticket_id']})
    state.update(priority_manifest=config.get('revision'), priority_started_at=time.time(), start_by=START_BY, cutoff=h.CUTOFF)
    h.atomic(h.STATE, state)
    h.event('resume', harness_version=3, dispatch_order=list(GROUPS), start_by=START_BY)
    h.reconcile_owned_runs()
    available = prepare(h)
    tickets = mapped_tickets(h, config)
    original_gen = h._gen

    def guarded_context(role, task, messages, limit=1800):
        messages = [dict(message) for message in messages]
        if messages and messages[0].get('role') == 'system':
            messages[0]['content'] += '\nPRIORITY TASK SCOPE: The requirement includes a scoped_work field. Implement that concrete slice; the broader acceptance remains unverified. Required additions to the actual owning API are allowed within the owned paths when exercised by meaningful tests. Do not invent duplicate owners or claim unavailable APIs already exist. Do not block a scoped change merely because the complete UI acceptance needs other owners.'
        budget = getattr(h.LANE, 'priority_context_budget', h.CONTEXT_BUDGET)
        if budget < h.CONTEXT_BUDGET:
            model = getattr(h.LANE, 'model', h.MODEL)
            count = h.api('POST', '/v1/models/' + model + '/count-tokens', {'messages': messages}, timeout=30)['input_tokens']
            if count + limit > budget:
                raise ValueError('context budget exceeded (' + str(count) + '+' + str(limit) + '; qualified limit ' + str(budget) + ')')
        return original_gen(role, task, messages, limit)

    h._gen = guarded_context
    stop = threading.Event()
    threading.Thread(target=h.monitor, args=(stop,), daemon=True).start()
    try:
        for group in GROUPS:
            config = read_config()
            tickets = mapped_tickets(h, config)
            with h.LOCK:
                current = json.loads(h.STATE.read_text())
                for item in config['tasks']:
                    current['tasks'].setdefault(item['task'][0], {'state': 'queued', 'priority_group': item['group'], 'canonical_ticket': item['ticket_id']})
                h.atomic(h.STATE, current)
            pending = collections.deque(item for item in config['tasks'] if item['group'] == group and task_ready(item, current))
            if not pending:
                continue
            h.event('priority_group_start', group=group, tasks=[x['task'][0] for x in pending])
            with concurrent.futures.ThreadPoolExecutor(max_workers=len(available)) as pool:
                running = {}
                idle = collections.deque(available)
                while pending or running:
                    while pending and idle and not h.STOP.is_set() and time.time() < h.CUTOFF - 600:
                        model = idle.popleft()
                        item = pending.popleft()
                        running[pool.submit(run_task, h, model, item, base, tickets)] = model
                    if not running:
                        break
                    done, _ = concurrent.futures.wait(running, timeout=1, return_when=concurrent.futures.FIRST_COMPLETED)
                    for future in done:
                        idle.append(running.pop(future))
                        future.result()
            for item in pending:
                h.update(item['task'][0], state='paused' if h.STOP.is_set() else 'queued_at_cutoff')
            if h.STOP.is_set() or time.time() >= h.CUTOFF - 600:
                break
    finally:
        stop.set()
        h._gen = original_gen
        h.atomic(ROOT / 'final-runtime.json', h.metrics())
        h.event('harness_finished', harness_version=3, stopped=h.STOP.is_set())


def main():
    h = load_harness()
    h.CUTOFF = h.DEADLINE - 120  # Preserve a two-minute quiet boundary before publication.
    config = read_config()
    if config.get('deployment_verified') is not True:
        raise RuntimeError('Priority manifest still needs PC source, ownership and mission validation; no work was launched')
    # The 22:00 publisher needs exact test mappings for both old and new candidates.
    merged = {task[0]: task for task in h.TASKS}
    merged.update({item['task'][0]: tuple(item['task']) for item in config['tasks']})
    h.TASKS = list(merged.values())
    signal.signal(signal.SIGTERM, lambda *_: h.STOP.set())
    signal.signal(signal.SIGINT, lambda *_: h.STOP.set())
    mode = sys.argv[1] if len(sys.argv) > 1 else 'run'
    if mode == 'prepare':
        prepare(h)
        return
    if mode not in ('run', 'publish'):
        raise ValueError('Expected run, prepare or publish')
    with (ROOT / ('publish.lock' if mode == 'publish' else 'run.lock')).open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if mode == 'publish':
            h.publish()
        else:
            run_priority(h, config)


if __name__ == '__main__':
    main()
