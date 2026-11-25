#!/usr/bin/env python3
"""Read-only local swarm telemetry. Token rates always identify their denominator."""
import csv, datetime, hashlib, json, pathlib, threading, time
from collections import Counter
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from zoneinfo import ZoneInfo
import harness
ROOT = pathlib.Path(__file__).resolve().parent
REG = pathlib.Path.home() / 'Documents/GitHub/9to1-night-20261124/atlas-v4-live-register-20261124'
TZ = ZoneInfo('Europe/London')
CACHE = {}
CACHE_LOCK = threading.RLock()

def read(path, default):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return default

def event_rows():
    rows = []
    for path in (ROOT / 'events.jsonl', ROOT / 'events-v2.jsonl'):
        try:
            for line in path.read_text().splitlines():
                try:
                    row = json.loads(line)
                    if isinstance(row, dict) and isinstance(row.get('at'), (int, float)):
                        rows.append(row)
                except ValueError:
                    pass
        except OSError:
            pass
    return sorted(rows, key=lambda row: row['at'])

def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)

def counted_tokens(row):
    value = row.get('tokens', row.get('output_tokens'))
    return value if type(value) is int and value >= 0 else None

def inference_metrics(rows, now):
    dispatches = {r['run']: r for r in rows if r.get('kind') == 'dispatch' and r.get('run')}
    unique = {}
    for index, row in enumerate(rows):
        if row.get('kind') == 'generation':
            unique[row.get('run') or ('unknown', index)] = {
                **row,
                'model': row.get('model') or dispatches.get(row.get('run'), {}).get('model') or 'unknown'
            }
    generations = sorted(unique.values(), key=lambda row: row['at'])
    recent = [g for g in generations if now - 30 <= g['at'] <= now]
    known = [g for g in generations if counted_tokens(g) is not None]
    recent_known = [g for g in recent if counted_tokens(g) is not None]
    epoch = next((r['at'] for r in reversed(rows) if r.get('kind') in ('start', 'resume', 'harness_resumed', 'harness_resume')), None)
    phase = [g for g in generations if epoch is None or g['at'] >= epoch]
    phase_known = [g for g in phase if counted_tokens(g) is not None]
    phase_tokens = sum(counted_tokens(g) for g in phase_known)
    models = {}
    for model in sorted({g['model'] for g in phase}):
        all_runs = [g for g in phase if g['model'] == model]
        fresh_runs = [g for g in all_runs if now - 300 <= g['at'] <= now]
        completed = [g for g in fresh_runs if number(g.get('decode_s')) and g['decode_s'] > 0 and counted_tokens(g) is not None]
        sustained = [g for g in completed if counted_tokens(g) >= 128]
        chosen = sustained[-5:] or completed[-5:]
        seconds = sum(g['decode_s'] for g in chosen)
        rate = sum((g['engine_tps'] if number(g.get('engine_tps')) else counted_tokens(g) / g['decode_s']) * g['decode_s'] for g in chosen) / seconds if seconds else None
        latest = all_runs[-1] if all_runs else {}
        draft_runs = [g for g in fresh_runs[-20:] if number(g.get('draft_n')) and g['draft_n'] > 0 and number(g.get('draft_n_accepted')) and 0 <= g['draft_n_accepted'] <= g['draft_n']]
        drafted = sum(g['draft_n'] for g in draft_runs)
        accepted = sum(g['draft_n_accepted'] for g in draft_runs)
        models[model] = {
            'draft_n': drafted,
            'draft_n_accepted': accepted,
            'draft_acceptance_pct': 100 * accepted / drafted if drafted else None,
            'decode_tps': rate,
            'basis': ('current run, last 5 min; >=128 output tokens' if sustained else 'current run, short responses; not a sustained benchmark') if chosen else 'no completed response in the current run within 5 min',
            'samples': len(chosen),
            'sample_tokens': sum(counted_tokens(g) for g in chosen),
            'sample_age_s': max(0, now - chosen[-1]['at']) if chosen else None,
            'last_output_tokens': counted_tokens(latest),
            'last_decode_tps': latest.get('engine_tps', latest.get('individual_tps')),
            'last_elapsed_s': latest.get('elapsed_s'),
            'last_ttft_s': latest.get('ttft_s'),
            'last_prompt_s': latest.get('prompt_s'),
            'last_token_source': latest.get('token_source', 'usage.completion_tokens'),
        }
    return {
        'completed_generations': len(generations),
        'reported_output_tokens': sum(counted_tokens(g) for g in known),
        'missing_usage_runs': len(generations) - len(known),
        'completed_output_tps_30s': sum(counted_tokens(g) for g in recent_known) / 30 if len(recent_known) == len(recent) else None,
        'recent_completed_runs': len(recent),
        'phase_output_tokens': phase_tokens,
        'phase_elapsed_s': max(1, now - epoch) if epoch else None,
        'end_to_end_tps': phase_tokens / max(1, now - epoch) if epoch and len(phase_known) == len(phase) else None,
        'models': models,
    }

def spec_snapshot():
    now = time.monotonic()
    with CACHE_LOCK:
        cached = CACHE.get('spec')
        if cached and now - cached[0] < 30:
            return cached[1]
        register_path = REG / 'register.json'
        register = read(register_path, {})
        overlay = read(REG / 'evidence-live.json', {})
        try:
            digest = hashlib.sha256(register_path.read_bytes()).hexdigest()
        except OSError:
            digest = None
        rows = register.get('rows', [])
        eligible = [r for r in rows if r.get('kind') != 'owner-proposal']
        # Count only acceptance records carrying all required evidence fields.
        accepted = [r for r in eligible if str(r.get('status', '')).upper() in ('VERIFIED', 'ACCEPTED', 'COMPLETED', 'COMPLETE')
                    and r.get('implementation_commit') and r.get('production_entrypoint')
                    and r.get('command_or_service') and r.get('evidence')]
        try:
            with (REG / 'live-status.csv').open() as handle:
                historical = Counter(r.get('status') for r in csv.DictReader(handle))
        except OSError:
            historical = Counter()
        data = {
            'denominator': len(eligible),
            'excluded_pending_owner': len(rows) - len(eligible),
            'recorded_verified': len(accepted),
            'recorded_verified_pct': 100 * len(accepted) / len(eligible) if eligible else None,
            'current_register_sha256': digest,
            'evidence_hash_matches': bool(digest and overlay.get('baseline_register_sha256') == digest),
            'historical_partial': historical.get('PARTIAL', 0),
            'historical_as_of': overlay.get('updated_utc'),
            'meaning': 'Recorded full-row acceptance only; implementation completeness has not been assessed.',
        }
        CACHE['spec'] = (now, data)
        return data

def broker_snapshot():
    now = time.monotonic()
    with CACHE_LOCK:
        cached = CACHE.get('broker')
        if cached and now - cached[0] < 1.5:
            return cached[1]
        try:
            value = {'data': harness.api('GET', '/v1/status', timeout=3), 'error': None}
        except Exception as exc:
            value = {'data': {}, 'error': str(exc)}
        CACHE['broker'] = (now, value)
        return value

def benchmark_summary(benchmark):
    models = {}
    trials = benchmark.get('trials', [])
    for model in sorted({trial.get('model') for trial in trials if trial.get('model')}):
        samples = [trial for trial in trials if trial.get('model') == model and trial.get('state') == 'completed' and number(trial.get('tokens')) and number(trial.get('decode_seconds')) and trial['decode_seconds'] > 0]
        decode_seconds = sum(trial['decode_seconds'] for trial in samples)
        wall_seconds = sum(trial.get('wall_seconds', 0) for trial in samples)
        drafted = sum((trial.get('timings') or {}).get('draft_n', 0) for trial in samples)
        accepted = sum((trial.get('timings') or {}).get('draft_n_accepted', 0) for trial in samples)
        models[model] = {
            'trials': len(samples),
            'tokens': sum(trial['tokens'] for trial in samples),
            'engine_tps': sum(trial['engine_tps'] * trial['decode_seconds'] for trial in samples if number(trial.get('engine_tps'))) / decode_seconds if decode_seconds and all(number(trial.get('engine_tps')) for trial in samples) else None,
            'wall_tps': sum(trial['tokens'] for trial in samples) / wall_seconds if wall_seconds and all(number(trial.get('wall_seconds')) for trial in samples) else None,
            'draft_n': drafted, 'draft_n_accepted': accepted,
            'draft_acceptance_pct': 100 * accepted / drafted if drafted else None,
        }
    return {'models': models, 'active': benchmark.get('active'), 'finished': benchmark.get('finished'), 'max_tokens': benchmark.get('max_tokens')}

def snapshot():
    now = time.time()
    state = read(ROOT / 'state.json', {})
    rows = event_rows()
    metrics = inference_metrics(rows, now)
    broker = broker_snapshot()
    data = broker['data']
    models = [{k: model.get(k) for k in ('name', 'id', 'state', 'gpu', 'slots', 'error')}
              for model in data.get('models', [])
              if model.get('state') in ('loaded', 'loading', 'failed') or 'Inkling' in model.get('name', '')]
    resources = data.get('resources') or read(ROOT / 'live.json', {}).get('resources', {})
    runs = {r['run']: r for r in rows if r.get('kind') in ('dispatch', 'run_state') and r.get('run')}
    actual = {r['id']: r['state'] for r in data.get('agents', [])}
    active = [{**row, 'state': actual.get(run, row.get('state'))}
              for run, row in runs.items()
              if actual.get(run, row.get('state')) in ('running', 'queued')]
    tasks = {}
    for task, value in state.get('tasks', {}).items():
        events = [r for r in rows if r.get('task') == task]
        counts = Counter(r.get('action') for r in events if r.get('kind') == 'worker_action')
        applied = sum(1 for r in events if r.get('kind') in ('edit_applied', 'patch_applied'))
        tasks[task] = {
            **value,
            'reads': counts['read'] + counts['search'] + counts['list'],
            'edit_requests': counts['replace'],
            'applied_edits': applied if applied else value.get('edits_applied'),
            'tests_passed': sum(1 for r in events if r.get('kind') == 'test' and r.get('passed') is True),
            'last_activity_age_s': max(0, now - events[-1]['at']) if events else None,
            'model': value.get('model') or next((r['model'] for r in reversed(events) if r.get('model')), None),
        }
    spec = spec_snapshot()
    candidates = [task for task, value in tasks.items() if value.get('state') == 'verified_candidate']
    tuning = read(ROOT / 'speed-tuning.json', {})
    return {
        'at': datetime.datetime.now(TZ).strftime('%H:%M:%S GMT'),
        'metrics': metrics, 'active': active, 'tasks': tasks, 'models': models,
        'resources': resources, 'error': broker['error'], 'spec': spec,
        'candidate_count': len(candidates), 'candidate_ids': candidates,
        'report': read(ROOT / 'report-2200.json', {}),
        'benchmark': benchmark_summary(read(ROOT / 'speed-benchmark.json', {})),
        'tuning': {'concurrent': tuning.get('concurrent'), 'completed_trials': len(tuning.get('trials', [])), 'finished': tuning.get('finished')},
        'latest': [{
            'time': datetime.datetime.fromtimestamp(row['at'], TZ).strftime('%H:%M:%S'),
            'kind': row['kind'], 'task': row.get('task', ''),
            'detail': row.get('action') or row.get('reason') or row.get('state') or ''
        } for row in rows[-16:][::-1]]
    }

HTML = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>9to1 · Swarm live</title>
<style>
:root{color-scheme:dark;font:15px/1.5 system-ui,sans-serif;background:#0d1118;color:#e8edf5}*{box-sizing:border-box}body{max-width:1260px;margin:auto;padding:24px}header{display:flex;align-items:center;justify-content:space-between;gap:20px}h1{font-size:27px;margin:0}h2{font-size:16px;margin:23px 0 10px}p{margin:8px 0}small,.muted,.label{color:#9aacc3}.label{font-size:12px}.tag{color:#75e0b1;font-size:12px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px}.box{border:1px solid #2d3848;border-radius:13px;background:#171e29;padding:17px}.num{font-size:36px;font-weight:700;line-height:1.3;font-variant-numeric:tabular-nums}.green{color:#75e0b1}.amber{color:#edc086}.foot{font-size:12px;color:#9aacc3}.bar{height:6px;border-radius:5px;background:#2d3848;margin:9px 0;overflow:hidden}.bar i{display:block;background:#75e0b1;height:100%}table{border-collapse:collapse;width:100%;font-size:13px}td,th{text-align:left;padding:10px;border-bottom:1px solid #2d3848;overflow-wrap:anywhere}th{color:#9aacc3;font-weight:500}pre{white-space:pre-wrap;font:12px/1.5 ui-monospace,monospace;color:#bdcad9;margin:0}.scroll{overflow-x:auto}.model{font-size:15px;font-weight:650}.rundetail{margin-top:8px;font-size:12px;color:#9aacc3}.notice{border-left:3px solid #edc086;padding-left:10px;margin-top:12px}.warn{color:#edc086}
</style></head><body>
<header><div><h1>9to1 / Swarm control</h1><small>Local Dulche workers · 24 November 2026</small></div><div id="clock" class="tag">Connecting…</div></header>
<h2>Sustained speed & speculative decoding</h2><div id="benchRates" class="grid"></div><p class="foot" id="benchDetail">Controlled benchmark pending.</p>
<h2>Worker generation speed</h2><div id="modelRates" class="grid"></div>
<p class="foot">Decode rates use server timings from completed requests. Long responses (128+ tokens) are shown when available. Refreshes every 2 seconds.</p>
<h2>Swarm throughput</h2><div class="grid">
<div class="box"><div class="label">Completed output · last 30 seconds</div><div id="rate" class="num green">—</div><small id="recent">Includes prompt, queue and tool gaps</small></div>
<div class="box"><div class="label">End-to-end · current run</div><div id="e2e" class="num">—</div><small>Output tokens / elapsed wall time</small></div>
<div class="box"><div class="label">Reported output tokens</div><div id="tokens" class="num">—</div><small id="gens"></small></div>
<div class="box"><div class="label">Active / queued inference</div><div id="active" class="num">—</div><small id="broker">Broker run state</small></div>
</div><p class="foot">Output totals update when a request completes; the 30-second figure is completion throughput, not a streamed instantaneous rate. Tokens measure inference, not completed development.</p>
<h2>Development assignments</h2><div class="box scroll"><table><thead><tr><th>Ticket / model</th><th>State</th><th>Reads / edit requests / passed tests</th><th>Evidence or blocker</th></tr></thead><tbody id="tasks"></tbody></table></div>
<h2>Specification evidence</h2><div class="box"><div class="grid">
<div><div class="label">Full acceptance recorded</div><div class="num" id="spec">—</div><div class="bar"><i id="fill"></i></div><small id="specDetail"></small></div>
<div><div class="label">Historical partial evidence</div><div class="num" id="partial">—</div><small id="partialDetail"></small></div>
<div><div class="label">Reviewed & test-passing changes</div><div class="num" id="candidates">—</div><small>Current run · ready for integration</small></div>
</div><p class="foot">This measures recorded acceptance evidence. It does not estimate how much of the application is implemented. Scoped tests do not certify an entire specification row.</p><p class="foot notice" id="warning"></p></div>
<h2>Models & hardware</h2><div class="box" id="hardware"></div>
<h2>Activity</h2><div class="box"><pre id="events"></pre></div>
<p class="foot">Checkpoint: 22:00 GMT · reviewed, test-passing owned commits · report-2200.json</p>
<script>
const el=id=>document.getElementById(id),put=(id,v)=>el(id).textContent=v,fmt=x=>x==null?'—':Number(x).toFixed(1),num=x=>Number(x).toLocaleString();
function node(tag,text,cls){const e=document.createElement(tag);e.textContent=text;if(cls)e.className=cls;return e}
function cell(tr,text){tr.append(node('td',text))}
function name(id){return id?.includes('flash')?'Flash · RTX 5080':id?.includes('v100')?'27B · V100':id||'Unassigned'}
function age(seconds){return seconds==null?'—':seconds<60?Math.round(seconds)+'s ago':Math.floor(seconds/60)+'m ago'}
async function tick(){try{
 const r=await fetch('/status',{cache:'no-store'});if(!r.ok)throw Error('HTTP '+r.status);const d=await r.json(),m=d.metrics;
 const working=Object.values(d.tasks).filter(t=>['working','reviewing','testing','preflight'].includes(t.state)).length;
 put('clock',(d.error?'TELEMETRY PARTIAL':working?'LIVE · '+working+' workers':'LIVE TELEMETRY · workers idle')+' · '+d.at);put('rate',fmt(m.completed_output_tps_30s)+' t/s');put('e2e',fmt(m.end_to_end_tps)+' t/s');put('recent',m.recent_completed_runs+' requests completed in the last 30 s');
 put('tokens',num(m.reported_output_tokens));put('gens',m.completed_generations+' completed generations'+(m.missing_usage_runs?' · '+m.missing_usage_runs+' without token counts':''));
 put('active',d.active.filter(x=>x.state==='running').length+' / '+d.active.filter(x=>x.state==='queued').length);put('broker',d.error?'Broker unavailable · events may be stale':'Broker run state');
 el('benchRates').replaceChildren();
 const concurrent=d.tuning?.concurrent;
 if(concurrent?.complete){const box=node('div','','box');box.append(node('div','Both GPUs · concurrent benchmark','model'),node('div',fmt(concurrent.aggregate_wall_tps)+' t/s','num green'),node('div','Measured total output / shared wall time','label'),node('div',num(concurrent.output_tokens)+' output tokens in '+fmt(concurrent.wall_seconds)+' seconds','rundetail'));el('benchRates').append(box)}
 for(const [id,b] of Object.entries(d.benchmark.models)){const box=node('div','','box');box.append(node('div',name(id)+' · controlled benchmark','model'),node('div',fmt(b.engine_tps)+' t/s','num green'),node('div','Sustained engine decode · '+b.trials+' trials · '+num(b.tokens)+' tokens','label'),node('div','End-to-end benchmark: '+fmt(b.wall_tps)+' t/s','rundetail'));const spec=node('div',b.draft_acceptance_pct==null?'Draft acceptance: unavailable':fmt(b.draft_acceptance_pct)+'% draft acceptance','model');spec.style.marginTop='10px';box.append(spec);if(b.draft_n)box.append(node('div',num(b.draft_n_accepted)+' accepted / '+num(b.draft_n)+' drafted tokens','rundetail'));el('benchRates').append(box)}
 if(!Object.keys(d.benchmark.models).length)el('benchRates').append(node('div','Benchmark collecting sustained output…','box'));
 const ba=d.benchmark.active;put('benchDetail',(d.benchmark.finished?'Benchmark complete. ':ba?'Running '+name(ba.model)+' trial '+ba.trial+'. ':'Benchmark preparing. ')+(d.benchmark.max_tokens||1024)+' output-token budget per trial; same prompt per model. Spec % is accepted speculative draft tokens / proposed draft tokens.');
 el('modelRates').replaceChildren();
 const modelIds=[...new Set([...d.models.filter(x=>x.state==='loaded'||x.state==='loading').map(x=>x.id),...Object.keys(m.models)])];
 for(const id of modelIds){const v=m.models[id],box=node('div','','box');box.append(node('div',name(id),'model'),node('div',v?fmt(v.decode_tps)+' t/s':'Awaiting output','num green'));
 if(v){box.append(node('div',v.basis,'label'));if(v.draft_acceptance_pct!=null)box.append(node('div',fmt(v.draft_acceptance_pct)+'% draft acceptance · '+num(v.draft_n_accepted)+' / '+num(v.draft_n)+' tokens','model'));box.append(node('div',v.samples+' samples · '+num(v.sample_tokens)+' output tokens · '+age(v.sample_age_s),'rundetail'));box.append(node('div','Latest request: '+(v.last_output_tokens??'—')+' tokens · '+fmt(v.last_elapsed_s)+'s total · '+fmt(v.last_ttft_s)+'s first token','rundetail'))}
 else box.append(node('div','No completed generation measured yet','label'));el('modelRates').append(box)}
 el('tasks').replaceChildren();for(const [k,v]of Object.entries(d.tasks)){const tr=document.createElement('tr');cell(tr,k+' · '+name(v.model));cell(tr,v.state);cell(tr,v.reads+' / '+v.edit_requests+' / '+v.tests_passed);cell(tr,(v.reason||v.summary||v.commit||'Last activity '+age(v.last_activity_age_s)));el('tasks').append(tr)}
 const s=d.spec,p=s.recorded_verified_pct;put('spec',p==null?'Unavailable':p.toFixed(2)+'%');el('fill').style.width=(p||0)+'%';put('specDetail',num(s.recorded_verified)+' / '+num(s.denominator)+' eligible register rows');put('partial',num(s.historical_partial)+' rows');put('partialDetail',s.evidence_hash_matches?'Overlay matches current register':'Historical overlay · not current acceptance');put('candidates',d.candidate_count);
 put('warning',s.evidence_hash_matches?'Evidence overlay matches the current register.':"Historical evidence overlay has a different baseline hash. Its partial rows are excluded from current verified acceptance; current register is unchanged.");
 el('hardware').replaceChildren();for(const model of d.models){el('hardware').append(node('p',model.name+' · '+model.state+' · '+(model.gpu||'no adapter')+' · slots '+(model.slots??'—')+(model.error?' · '+model.error:'')))}
 for(const g of d.resources.gpus||[])el('hardware').append(node('p',g.name+' · '+g.temperature_c+'°C · '+g.used_mib+'/'+g.total_mib+' MiB · '+g.utilization+'% util'));
 el('hardware').append(node('small','Host available RAM '+fmt((d.resources.host_available_mib||0)/1024)+' GiB'+(d.error?' · '+d.error:'')));
 put('events',d.latest.map(x=>x.time+'  '+x.kind+'  '+x.task+'  '+x.detail).join('\n'));
 }catch(e){put('clock','OFFLINE · '+e.message)}}
tick();setInterval(tick,2000);
</script></body></html>"""

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            if self.path == '/status':
                data = json.dumps(snapshot(), allow_nan=False).encode()
                content_type = 'application/json'
            elif self.path == '/':
                data = HTML.encode()
                content_type = 'text/html; charset=utf-8'
            else:
                self.send_error(404)
                return
            self.send_response(200)
            self.send_header('Content-Type', content_type)
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception:
            self.send_error(503, 'Telemetry temporarily unavailable')

    def log_message(self, *args):
        pass

if __name__ == '__main__':
    ThreadingHTTPServer(('127.0.0.1', 18759), Handler).serve_forever()
