import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock
sys.dont_write_bytecode = True
HERE = Path(__file__).parent
RUNTIME = HERE if (HERE/'continuation.py').is_file() and (HERE/'test_continuation.py').is_file() else HERE.parents[1] / 'pc-gateway'
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module
helper = load('worker_access', HERE / 'worker-access.py')
fixtures = load('continuation_fixtures', RUNTIME / 'test_continuation.py')
module = fixtures.module

class Desktop:
    def __init__(self): self.starts = []; self.opens = []
    def unit(self, name, start=False):
        if start: self.starts.append(name)
        return True
    def open(self, target): self.opens.append(target)

class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.control = self.root / 'control.json'
        self.control.write_text(json.dumps({'mode':'PAUSE', 'hold_only':True, 'owner_note':'Preserve this correction'}))
        self.prompt = self.root / 'prompt.txt'; self.prompt.write_text('The canonical specification, owner corrections and scoped milestone commit policy remain binding.')
        self.config = {'session_id': fixtures.SID, 'model': fixtures.MODEL, 'directory':'/project',
            'server':'http://127.0.0.1:1234', 'opencode':'/verified/opencode', 'service_config':'/private/auth',
            'control_path':str(self.control), 'dispatch_lock_path':str(self.root/'dispatch.lock'),
            'enabled':True, 'free_usage_verified':True, 'approved_user_message_ids':['msg_owner'],
            'other_programme_session_ids':['ses_peer'], 'coordinator_marker_path':str(self.root/'hold.json'),
            'service_recovery':{'enabled':True, 'exclusive_ownership_verified':True},
            'continuation_prompt':{'path':str(self.prompt),'sha256':helper.digest(self.prompt)},
            'retry_prompt':{'path':str(self.prompt),'sha256':helper.digest(self.prompt)}}
        self.write_config()
        self.state={'session_id':fixtures.SID,'model':fixtures.MODEL,'directory':'/project','dispatched':[], 'mode':'paused', 'last_healthy_service':{'active':[fixtures.SID], 'at':900}}
        self.state_path=self.root/'continuation-state.json'; self.state_path.write_text(json.dumps(self.state))
        self.manifest=self.root/'worker-access.json'; self.manifest.write_text(json.dumps({
            'loop_root':str(self.root),'worker_url':'http://127.0.0.1:1234/server/fixture/session/'+fixtures.SID,
            'identity':{k:self.config[k] for k in ('session_id','model','directory','server')},
            'guide_path':str(self.root/'guide.html'),'unit':'9to1-pc-worker-continuation.service'}))
        self.api=fixtures.API(); self.api.session['outcome']='interrupted'; self.desktop=Desktop()
        self.access=self.new_access()
    def tearDown(self): self.temp.cleanup()
    def write_config(self): (self.root/'config.json').write_text(json.dumps(self.config))
    def new_access(self): return helper.Access(self.manifest,module=module,api=self.api,desktop=self.desktop,now=lambda:1000)
    def assertHeld(self):
        with self.assertRaises(helper.Held): self.access.resume()
        self.assertFalse(self.api.prompts)
    def test_status_reads_only_and_does_not_start_or_write(self):
        before={str(p):p.read_bytes() for p in self.root.iterdir() if p.is_file()}
        result=self.access.status(); self.assertEqual('available',result['service'])
        self.assertEqual(before,{str(p):p.read_bytes() for p in self.root.iterdir() if p.is_file()})
        self.assertFalse(self.desktop.starts); self.assertFalse(self.api.prompts)
    def test_open_existing_paused_service_preserves_pause_without_monitor_start(self):
        original=self.control.read_bytes(); self.access.open_worker()
        self.assertEqual(original,self.control.read_bytes()); self.assertFalse(self.desktop.starts)
        self.assertEqual([json.loads(self.manifest.read_text())['worker_url']],self.desktop.opens)
    def test_open_starts_only_absent_exclusive_service_in_run_and_monitor(self):
        self.control.write_text('{"mode":"RUN"}'); self.api.fail='session.get'
        original=self.api.start_service
        def start(): original(); self.api.fail=None
        self.api.start_service=start
        self.access.open_worker(); self.assertEqual(1,self.api.service_starts); self.assertEqual(1,len(self.desktop.starts))
        self.assertFalse(self.api.prompts)
    def test_open_does_not_start_under_stop_or_when_any_opencode_process_exists(self):
        for mode,process in [('STOP',True),('RUN',False)]:
            with self.subTest(mode=mode):
                self.control.write_text(json.dumps({'mode':mode})); self.api.fail='session.get'; self.api.process_absent=process
                with self.assertRaises(helper.Held): self.access.open_worker()
        self.assertEqual(0,self.api.service_starts)
    def test_open_preserves_unresolved_resume_intent(self):
        self.access.record(kind='resume',phase='intent',message_id='msg_saved')
        before=self.access.access_state.read_bytes(); self.access.open_worker()
        self.assertEqual(before,self.access.access_state.read_bytes())
    def test_explicit_resume_preserves_owner_fields_exact_session_instructions_and_monitor_state(self):
        state=self.state_path.read_bytes(); result=self.access.resume()
        self.assertEqual('resumed_existing_work',result['status']); self.assertEqual(state,self.state_path.read_bytes())
        c=json.loads(self.control.read_text()); self.assertEqual('RUN',c['mode']); self.assertFalse(c['hold_only']); self.assertEqual('Preserve this correction',c['owner_note'])
        call=self.api.prompts[0]; self.assertEqual(fixtures.SID,call[1]['sessionID']); self.assertTrue(call[2]['resume'])
        self.assertIn(self.prompt.read_text(),call[2]['text']); self.assertIn(result['message_id'],json.loads((self.root/'config.json').read_text())['approved_user_message_ids'])
        self.assertFalse(Path(self.config['coordinator_marker_path']).exists()); self.assertEqual(1,len(self.desktop.starts))
    def test_resume_duplicate_does_not_send_again(self):
        self.access.resume()
        self.assertEqual('already_resumed',self.new_access().resume()['status'])
        self.assertEqual(1,len(self.api.prompts))
    def test_active_session_or_unrelated_session_blocks_without_gateway_probe(self):
        for active in ('ses_unrelated',):
            with self.subTest(active=active):
                self.api.active={active:{'type':'running'}}; self.assertHeld()
        self.assertEqual(0,self.api.gateway_calls)
    def test_active_saved_worker_returns_noop_without_prompt_or_control_change(self):
        self.api.active={fixtures.SID:{'type':'running'}}; before=self.control.read_bytes()
        self.assertEqual('already_active',self.access.resume()['status'])
        self.assertEqual(before,self.control.read_bytes()); self.assertFalse(self.api.prompts); self.assertEqual(0,self.api.gateway_calls)
    def test_known_queued_input_is_preserved_and_resume_does_not_duplicate_its_text(self):
        self.config['approved_user_message_ids'].append('msg_queued'); self.write_config()
        queued={'id':'msg_queued','type':'user','text':'Existing corrective assignment'}; self.api.inbox=[queued.copy()]
        self.access.resume(); self.assertEqual(queued,self.api.inbox[0]); self.assertEqual(2,len(self.api.inbox))
        self.assertNotIn('Existing corrective assignment',self.api.prompts[0][2]['text']); self.assertEqual(1,len(self.api.prompts))
    def test_known_queue_change_during_probe_holds(self):
        self.config['approved_user_message_ids'].append('msg_queued'); self.write_config()
        def probe(*args): self.api.inbox.append({'id':'msg_queued','type':'user'}); return {'kind':'loaded'}
        self.api.probe_gateway=probe; self.assertHeld()
    def test_explicit_resume_starts_absent_service_without_clearing_pause_until_loaded(self):
        self.api.fail='session.get'; original=self.api.start_service; seen=[]
        def start(): seen.append(json.loads(self.control.read_text())['mode']); original(); self.api.fail=None
        self.api.start_service=start
        self.access.resume(); self.assertEqual(['PAUSE'],seen); self.assertEqual(1,self.api.service_starts)
        self.assertEqual('RUN',json.loads(self.control.read_text())['mode']); self.assertEqual(1,len(self.api.prompts))
    def test_absent_service_explicit_resume_preserves_stop_when_pc_not_ready(self):
        self.control.write_text('{"mode":"STOP"}'); self.api.fail='session.get'; original=self.api.start_service
        def start(): original(); self.api.fail=None
        self.api.start_service=start; self.api.gateway_result={'kind':'not_loaded'}
        self.assertHeld(); self.assertEqual(1,self.api.service_starts); self.assertEqual('STOP',json.loads(self.control.read_text())['mode'])
    def test_unknown_input_inbox_permissions_forms_and_running_tool_all_hold(self):
        for item in ('input','inbox','permission','form','tool'):
            with self.subTest(item=item):
                self.api=fixtures.API(); self.access=self.new_access()
                if item=='input': self.api.user['id']='msg_unknown'
                if item=='inbox': self.api.inbox=[{'id':'msg_unknown'}]
                if item=='permission': self.api.permissions=[{'id':'per_pending'}]
                if item=='form': self.api.forms=[{'id':'form_pending'}]
                if item=='tool': self.api.assistant['content']=[{'type':'tool','state':{'status':'running'}}]
                self.assertHeld()
    def test_pending_dispatch_or_ambiguous_outage_interrupt_holds(self):
        for extra in ({'pending':{'message_id':'msg_uncertain'}},{'outage_interrupt':{'status':'intent'}}):
            with self.subTest(extra=extra):
                self.state_path.write_text(json.dumps({**self.state,**extra})); self.assertHeld()
    def test_gateway_not_loaded_auth_and_rate_limit_hold_and_preserve_stop(self):
        self.control.write_text('{"mode":"STOP","reason":"Owner"}')
        original=self.control.read_bytes()
        for kind in ('not_loaded','unavailable','authentication','rate_limit'):
            self.api.gateway_result={'kind':kind}; self.assertHeld(); self.assertEqual(original,self.control.read_bytes())
    def test_new_stop_during_gateway_readiness_is_preserved(self):
        def probe(*args): self.control.write_text('{"mode":"STOP","reason":"New stop"}'); return {'kind':'loaded'}
        self.api.probe_gateway=probe; self.assertHeld(); self.assertEqual('New stop',json.loads(self.control.read_text())['reason'])
    def test_new_stop_after_durable_intent_is_preserved_without_dispatch(self):
        original=self.access.record
        def record(**values):
            result=original(**values)
            if values.get('kind')=='resume': self.control.write_text('{"mode":"STOP","reason":"After intent"}')
            return result
        self.access.record=record; self.assertHeld(); self.assertEqual('After intent',json.loads(self.control.read_text())['reason'])
    def test_model_config_changed_during_probe_prevents_resume(self):
        def probe(*args): self.config['enabled']=False; self.write_config(); return {'kind':'loaded'}
        self.api.probe_gateway=probe; self.assertHeld()
    def test_changed_approved_prompt_or_paid_model_holds(self):
        self.prompt.write_text('Unknown replacement'); self.assertHeld()
        self.prompt.write_text('The canonical specification, owner corrections and scoped milestone commit policy remain binding.')
        self.api.model['cost'][0]['input']=1; self.assertHeld()
    def test_existing_coordinator_marker_is_not_removed(self):
        marker=Path(self.config['coordinator_marker_path']); marker.write_text('Actual independent blocker')
        self.assertHeld(); self.assertEqual('Actual independent blocker',marker.read_text())
    def ambiguous(self):
        original=self.api.request
        def request(operation,params=None,data=None):
            result=original(operation,params,data)
            return {} if operation=='session.prompt' else result
        self.api.request=request
        with self.assertRaises(helper.Held): self.access.resume()
        return json.loads(self.access.access_state.read_text())
    def test_ambiguous_prompt_keeps_marker_and_exact_reconciliation_never_resends(self):
        intent=self.ambiguous(); marker=Path(intent['marker_path']); self.assertTrue(marker.exists())
        self.api.messages[intent['message_id']]={'id':intent['message_id'],'type':'user','metadata':{'pc_owner_resume':intent['boundary']}}
        result=self.new_access().resume(); self.assertEqual('previous_resume_reconciled',result['status'])
        self.assertEqual(1,len(self.api.prompts)); self.assertFalse(marker.exists())
    def test_unknown_ambiguous_ack_is_not_resent_or_cleared(self):
        intent=self.ambiguous()
        with self.assertRaises(helper.Held): self.new_access().resume()
        self.assertEqual(1,len(self.api.prompts)); self.assertTrue(Path(intent['marker_path']).exists())
    def test_reconciled_resume_does_not_clear_replaced_owner_marker_or_override_new_stop(self):
        intent=self.ambiguous(); marker=Path(intent['marker_path']); marker.write_text('New owner hold')
        self.control.write_text('{"mode":"STOP"}')
        self.api.messages[intent['message_id']]={'id':intent['message_id'],'type':'user','metadata':{'pc_owner_resume':intent['boundary']}}
        self.new_access().resume(); self.assertEqual('New owner hold',marker.read_text()); self.assertEqual('STOP',json.loads(self.control.read_text())['mode'])
    def test_new_stop_during_explicit_absent_service_check_prevents_start(self):
        self.api.fail='session.get'; original=self.api.local_service_unavailable
        def absent(): self.control.write_text('{"mode":"STOP","reason":"new stop"}'); return original()
        self.api.local_service_unavailable=absent
        self.assertHeld(); self.assertEqual(0,self.api.service_starts)
    def test_foreign_previous_service_ownership_prevents_start(self):
        self.state['last_healthy_service']['active'].append('ses_foreign'); self.state_path.write_text(json.dumps(self.state))
        self.api.fail='session.get'; self.assertHeld(); self.assertEqual(0,self.api.service_starts)
    def test_validated_explicit_resume_unparks_known_repaired_api_hold_without_state_write(self):
        self.state['parked']={'mode':'held_configuration_unavailable','epoch':0}; self.state_path.write_text(json.dumps(self.state))
        before=self.state_path.read_bytes(); self.access.resume()
        self.assertEqual(1,json.loads((self.root/'config.json').read_text())['recovery_epoch'])
        intent=json.loads(self.access.access_state.read_text()); self.assertEqual(0,intent['recovery_epoch_before']); self.assertEqual(1,intent['recovery_epoch_after'])
        self.assertEqual(before,self.state_path.read_bytes()); self.assertEqual(1,len(self.api.prompts))
    def test_unknown_park_or_provider_retry_after_remains_held(self):
        for fields in ({'parked':{'mode':'held_unclassified_provider_failure_while_healthy','epoch':0}}, {'retry_at':1001}, {'gateway_probe_at':1001}):
            with self.subTest(fields=fields):
                self.state_path.write_text(json.dumps({**self.state,**fields})); self.assertHeld()
        self.assertEqual(0,self.api.gateway_calls)
    def test_failed_readiness_does_not_clear_existing_park(self):
        self.state['parked']={'mode':'held_gateway_authentication','epoch':0}; self.state_path.write_text(json.dumps(self.state))
        self.api.gateway_result={'kind':'authentication'}; self.assertHeld()
        self.assertEqual(0,json.loads((self.root/'config.json').read_text()).get('recovery_epoch',0))
    def test_gui_status_accepts_null_outcome_and_reports_model_readiness(self):
        value=helper.gui_summary({'worker_active':False,'outcome':None,'pc_model':'not_loaded'},'PAUSE','http://saved')
        self.assertIn('idle/unavailable',value); self.assertIn('not_loaded',value); self.assertIn('PAUSE',value)
    def test_worker_identity_changes_and_foreign_url_fail_closed(self):
        data=json.loads(self.manifest.read_text()); data['worker_url']='https://example.com/session/'+fixtures.SID
        self.manifest.write_text(json.dumps(data))
        with self.assertRaises(helper.Held): self.new_access()
    def test_dispatch_lock_contention_never_prompts(self):
        with module.lock(self.config['dispatch_lock_path']):
            with self.assertRaises(BlockingIOError): self.access.resume()
        self.assertFalse(self.api.prompts)

if __name__=='__main__': unittest.main(verbosity=2)
