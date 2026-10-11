// Policy values are installed from private configuration, never this public file.
export function selectShift(policy, availability, at = new Date()) {
  if (policy.timezone !== 'Europe/London') throw new Error('Expected Europe/London policy');
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-GB', {
    timeZone: policy.timezone, weekday: 'long', year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  }).formatToParts(at).map(p => [p.type, p.value]));
  const date = `${parts.year}-${parts.month}-${parts.day}`;
  const rule = policy.date_overrides?.[date] ?? policy.weekdays?.[parts.weekday];
  if (!rule) throw new Error(`No policy for ${parts.weekday}`);
  const minute = Number(parts.hour) * 60 + Number(parts.minute);
  const toMinute = t => {
    if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(t ?? '')) throw new Error('Invalid schedule time');
    return Number(t.slice(0,2)) * 60 + Number(t.slice(3));
  };
  const start = toMinute(rule.local_start), end = toMinute(rule.local_end);
  if (start >= end) throw new Error('Overnight local windows are forbidden');
  const within = !rule.disabled && minute >= start && minute < end;
  const available = availability.home === true && availability.paused !== true && availability.healthy === true;
  const local = within && available;
  return { shift: local ? 'local' : 'cloud', date, day: parts.weekday,
    local_time: `${parts.hour}:${parts.minute}:${parts.second}`,
    window: `${rule.local_start}-${rule.local_end}`, within_window: within,
    reason: !within ? 'Outside permitted window' : availability.paused ? 'Paused' : !availability.home ? 'Away' : !availability.healthy ? 'Runner unhealthy or offline' : 'Home and healthy',
    next_boundary: within ? rule.local_end : minute < start ? rule.local_start : 'Next day', timezone: policy.timezone };
}

export function normalizePaths(paths) {
  if (!Array.isArray(paths) || !paths.length || paths.length > 200) throw new Error('Owned paths required');
  return [...new Set(paths.map(p => {
    if (typeof p !== 'string' || !p || p.startsWith('/') || /[:\\\0]/.test(p) || p.split('/').some(x => x === '..' || x === '.')) throw new Error('Unsafe owned path');
    return p.replace(/\/+$/, '') + '/';
  }))];
}
export const pathsOverlap = (a, b) => a.some(x => b.some(y => x.toLowerCase().startsWith(y.toLowerCase()) || y.toLowerCase().startsWith(x.toLowerCase())));

export function initialState() {
  return { version: 1, fence: 0, lease: null, tasks: [], handovers: [], events: [], policy: null,
    presence: { home: false, paused: false, healthy: false, seen_at: 0 }, checkpoint_requested: false };
}

function refuse(message) { const e = new Error(message); e.status = 409; throw e; }
export function transition(s, action, p, actor, now) {
  const emit = (event, details = {}) => { s.events.push({ at: new Date(now).toISOString(), event, actor, ...details }); s.events = s.events.slice(-500); };
  const valid = () => {
    if (!s.lease || s.lease.worker !== actor || s.lease.fence !== p.fence || s.lease.expires_at <= now) refuse('Lease absent, expired or fenced');
    return s.lease;
  };
  const expected = () => {
    if (!s.policy) refuse('Private policy not configured');
    return selectShift(s.policy, { ...s.presence, healthy: s.presence.healthy && now - s.presence.seen_at < 90000 }, new Date(now)).shift;
  };
  if (action === 'policy') {
    if (actor !== 'local') refuse('Operator required');
    selectShift(p.policy, { home: false, healthy: false }, new Date(now)); s.policy = p.policy; emit('POLICY_UPDATED');
  } else if (action === 'presence') {
    if (actor !== 'local') refuse('Local operator required');
    for (const key of ['home','paused','healthy']) if (typeof p[key] === 'boolean') s.presence[key] = p[key];
    s.presence.seen_at = now; emit('PRESENCE', { home: s.presence.home, paused: s.presence.paused });
  } else if (action === 'acquire') {
    if (actor !== expected()) refuse('Requested worker is outside its permitted shift');
    if (s.presence.paused) refuse('Relay paused');
    if (s.lease && s.lease.expires_at > now) refuse('An exclusive lease already exists');
    if (s.lease) for (const t of s.tasks.filter(t => t.status === 'WORKING')) t.status = 'RECOVERY_REQUIRED';
    const fence = ++s.fence; s.lease = { worker: actor, fence, acquired_at: now, heartbeat_at: now, expires_at: now + 90000 };
    s.checkpoint_requested = false; emit('LEASE_ACQUIRED', { fence });
  } else if (action === 'heartbeat') {
    const l = valid(); l.heartbeat_at = now; l.expires_at = now + 90000;
    if (expected() !== actor || s.presence.paused) s.checkpoint_requested = true;
  } else if (action === 'enqueue') {
    if (actor !== 'local') refuse('Operator required');
    if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(p.task?.id ?? '')) refuse('Invalid task ID');
    if (s.tasks.some(t => t.id === p.task.id)) refuse('Task ID exists');
    const paths = normalizePaths(p.task.paths);
    if (!p.task.objective || !p.task.source?.sha?.match(/^[0-9a-f]{40}$/) || !p.task.source.branch || !Array.isArray(p.task.tests) || !p.task.tests.length) refuse('Task requires objective, exact source and tests');
    s.tasks.push({ ...p.task, paths, status: 'QUEUED', owner: null, fence: null, acceptance: 'NOT_RUN', evidence: [] }); emit('TASK_QUEUED', { task: p.task.id });
  } else if (action === 'claim') {
    valid(); if (s.checkpoint_requested || expected() !== actor || s.presence.paused) refuse('Shift is draining');
    const t = s.tasks.find(t => t.id === p.task_id);
    if (!t || t.status !== 'QUEUED' || (t.lane && t.lane !== actor)) refuse('Task is not available');
    if (s.tasks.some(x => x.id !== t.id && ['WORKING','RECOVERY_REQUIRED','READY'].includes(x.status) && pathsOverlap(x.paths,t.paths))) refuse('Path ownership conflict');
    Object.assign(t, { owner: actor, fence: p.fence, status: 'WORKING', started_at: now }); emit('TASK_CLAIMED', { task: t.id });
  } else if (action === 'checkpoint') {
    if (actor !== 'local' && actor !== s.lease?.worker) refuse('Operator or active worker required');
    s.checkpoint_requested = true; emit('CHECKPOINT_REQUESTED');
  } else if (action === 'result') {
    valid(); const t=s.tasks.find(t => t.id === p.task_id);
    if (!t || t.owner !== actor || t.fence !== p.fence || t.status !== 'WORKING') refuse('Task not owned');
    if (!['CHECKPOINT','BLOCKED','COMPLETED'].includes(p.status) || !Array.isArray(p.evidence)) refuse('Result requires truthful status and evidence');
    if (['RCVP_VERIFIED','FULL_VERIFIED','INDEPENDENT_ACCEPTANCE_PASSED'].includes(p.acceptance)) refuse('Worker results cannot promote independent product acceptance');
    Object.assign(t, { status:p.status, evidence:p.evidence, result:p.result, acceptance:p.acceptance ?? 'NOT_RUN' }); emit('TASK_RESULT', { task:t.id,status:t.status });
  } else if (action === 'handover') {
    valid(); const h=p.handover;
    if (s.tasks.some(t => t.status === 'WORKING' && t.owner === actor)) refuse('Writers have not drained');
    if (!h?.id || s.handovers.some(x=>x.id===h.id) || h.from !== actor || h.to === actor || !['local','cloud'].includes(h.to)) refuse('Invalid or duplicate handover');
    if (!p.remote_verified || !h.source?.sha?.match(/^[0-9a-f]{40}$/) || !h.source.branch || !h.tasks?.length || !Array.isArray(h.tests) || !Array.isArray(h.blockers)) refuse('Handover is not remotely reconstructable or lacks evidence');
    for (const id of h.tasks) { const t=s.tasks.find(t=>t.id===id); if(!t || t.owner!==actor || t.fence!==p.fence || !['COMPLETED','CHECKPOINT','BLOCKED'].includes(t.status)) refuse('Handover contains an unreleased task'); }
    s.handovers.push({ ...h, status:'READY', fence:p.fence, created_at:now, revision:s.handovers.length+1 });
    s.checkpoint_requested=true; emit('HANDOVER_READY',{handover:h.id});
  } else if (action === 'ack') {
    const h=s.handovers.find(h=>h.id===p.handover_id);
    if(!h || h.to!==actor || h.status!=='READY' || h.revision!==p.revision || h.source.sha!==p.reconstructed_sha || !p.remote_verified) refuse('Missing, stale or unreconstructed handover');
    if(s.lease?.worker!==h.from || s.lease.fence!==h.fence) refuse('Sender ownership changed; reconcile before acknowledgement');
    h.status='ACKNOWLEDGED';h.ack={worker:actor,sha:p.reconstructed_sha,at:now,revision:p.revision};
    for(const id of h.tasks){const t=s.tasks.find(t=>t.id===id);t.owner=null;t.fence=null;if(t.status==='CHECKPOINT'){t.status='QUEUED';t.source=h.source;}}
    s.lease=null; emit('HANDOVER_ACKNOWLEDGED',{handover:h.id});
  } else if (action === 'release') {
    valid(); if(s.tasks.some(t=>t.owner===actor && ['WORKING','CHECKPOINT','RECOVERY_REQUIRED'].includes(t.status))) refuse('Unhanded source remains owned');
    s.lease=null; emit('LEASE_RELEASED');
  } else refuse('Unsupported action');
  return s;
}
