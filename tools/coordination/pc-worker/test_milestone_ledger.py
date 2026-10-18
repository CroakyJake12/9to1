#!/usr/bin/env python3
"""Independent ledger checks: real temporary git repositories, no live coordinator writes."""
import hashlib, importlib.util, json, pathlib, subprocess, tempfile, unittest
HELPER=pathlib.Path(__file__).with_name('milestone-ledger.py')
spec=importlib.util.spec_from_file_location('ledger',HELPER)
ledger=importlib.util.module_from_spec(spec);spec.loader.exec_module(ledger)
def write(p,v):
 p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(v));return p
class LedgerTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
  self.root=pathlib.Path(self.tmp.name);self.worker=self.root/'repo';self.worker.mkdir()
  def git(*a):return subprocess.check_output(['git','-C',str(self.worker),*a],stderr=subprocess.STDOUT,text=True).strip()
  self.git=git;git('init','-b','test-worker');git('config','user.email','fixture@example.invalid');git('config','user.name','Fixture')
  (self.worker/'source.txt').write_text('committed\n');git('add','source.txt');git('commit','-m','fixture')
  self.sp=self.root/'spec.md';self.sp.write_text('Canonical fixture requirements\n');self.sha=ledger.digest(self.sp)
  self.plan=write(self.root/'plan.json',{'worker_branch':'test-worker','milestones':[{'id':'A','dependencies':[]},{'id':'B','dependencies':['A']},{'id':'C','dependencies':[]}]})
  self.state=write(self.root/'state.json',{'current_milestone_id':'A','milestones':{x:{'implementation_status':'in_progress' if x=='A' else 'pending','acceptance_status':'NOT_RUN'} for x in 'ABC'},'history':[]})
  self.control=write(self.root/'control.json',{'mode':'RUN'})
  write(self.root/'worker-loop/config.json',{'directory':str(self.worker),'control_path':str(self.control),'milestones':{'plan':{'path':str(self.plan),'sha256':ledger.digest(self.plan)},'spec':{'path':str(self.sp),'sha256':self.sha},'state_path':str(self.state)}})
  self.e=self.worker/'work/pc-worker';self.e.mkdir(parents=True)
  self.log=self.e/'smoke.log';self.log.write_text('Actual fixture check: exit 0\n')
  self.manifest=write(self.e/'manifest.json',{'milestone_id':'A','spec_sha256':self.sha,'requirements':[{'id':'FIX-001','implementation_status':'implemented'}]})
  self.record={'milestone_id':'A','spec_sha256':self.sha,'summary':'Fixture completed','next_action':'Review independently','read_ranges':[{'start_line':1,'end_line':1}],'evidence':[str(self.log)],'checks':[{'command':'fixture check','exit_code':0,'result':'PASS'}],'requirement_manifest':str(self.manifest),'unresolved_requirements':[]}
 def invoke(self,action='finish',record=None):
  return ledger.update(self.root,action,write(self.e/'record.json',self.record if record is None else record))
 def rejected_unchanged(self,record=None,action='finish'):
  before=self.state.read_bytes()
  with self.assertRaises((ValueError,OSError,KeyError)):self.invoke(action,record)
  self.assertEqual(before,self.state.read_bytes())
 def test_finish_rejects_tracked_staged_and_untracked_source(self):
  for kind in ('dirty','staged','untracked'):
   with self.subTest(kind=kind):
    if kind=='untracked':(self.worker/'new-source.cui').write_text('source')
    else:
     (self.worker/'source.txt').write_text('modified')
     if kind=='staged':self.git('add','source.txt')
    self.rejected_unchanged();self.git('reset','--hard','HEAD')
    (self.worker/'new-source.cui').unlink(missing_ok=True)
 def test_finish_requires_spec_evidence_checks_manifest(self):
  for field in ('spec_sha256','evidence','checks','requirement_manifest'):
   with self.subTest(field=field):
    r=dict(self.record);r.pop(field);self.rejected_unchanged(r)
  for field,value in [('spec_sha256','wrong'),('evidence',[str(self.e/'missing')]),('checks',[{'result':'FAIL','exit_code':1}]),('unresolved_requirements',['FIX-002'])]:
   with self.subTest(field=field,value=value):
    r=dict(self.record);r[field]=value;self.rejected_unchanged(r)
 def test_manifest_identity_and_unfinished_requirements_rejected(self):
  original=ledger.read(self.manifest)
  for patch in ({'spec_sha256':'wrong'},{'milestone_id':'OTHER'},{'requirements':[]},{'requirements':[{'id':'FIX-001','implementation_status':'pending'}]}):
   with self.subTest(patch=patch):write(self.manifest,original|patch);self.rejected_unchanged()
 def test_finish_remains_pending_review_and_records_exact_custody(self):
  result=self.invoke();s=ledger.read(self.state);a=s['milestones']['A']
  self.assertEqual(a['implementation_status'],'implemented');self.assertEqual(a['acceptance_status'],'PENDING_REVIEW')
  self.assertEqual(a['commit'],self.git('rev-parse','HEAD'));self.assertEqual(a['evidence'][0]['sha256'],ledger.digest(self.log))
  self.assertEqual(a['requirement_manifest']['sha256'],ledger.digest(self.manifest));self.assertEqual(result['next_milestone_id'],'B')
 def test_block_advances_independent_work_not_blocked_dependent(self):
  r=self.record|{'blocker':'Owner legal release decision','unblock_condition':'Explicit owner decision'}
  result=self.invoke('block',r);self.assertEqual(result['next_milestone_id'],'C');s=ledger.read(self.state)
  self.assertEqual(s['milestones']['B']['implementation_status'],'pending');self.assertEqual(s['milestones']['A']['implementation_status'],'blocked')
 def test_stop_and_pause_prevent_every_mutation(self):
  for mode in ('STOP','PAUSE'):
   write(self.control,{'mode':mode})
   for action in ('next','checkpoint','finish','block','dependency'):
    with self.subTest(mode=mode,action=action):self.rejected_unchanged(action=action)
   self.assertEqual(ledger.update(self.root,'status')['current_milestone_id'],'A')
 def test_duplicate_finish_does_not_corrupt_or_duplicate_history(self):
  self.invoke();self.rejected_unchanged();self.assertEqual(len(ledger.read(self.state)['history']),1)
 def test_next_cannot_skip_current_in_progress(self):
  before=self.state.read_bytes();r=ledger.update(self.root,'next')
  self.assertEqual(r['current_milestone_id'],'A');self.assertEqual(before,self.state.read_bytes())
 def test_plan_spec_and_branch_identity_fail_closed(self):
  for target in (self.plan,self.sp):
   old=target.read_bytes();target.write_bytes(old+b' ');self.rejected_unchanged();target.write_bytes(old)
  self.git('checkout','-b','wrong-branch');self.rejected_unchanged()
 def test_scoped_dependency_unblocks_only_target_without_accepting_framework(self):
  r=self.record|{'milestone_id':'B','dependency_id':'A','summary':'Only typed interface needed by B is implemented'}
  for patch in ({'dependency_id':'C'},{'spec_sha256':'wrong'},{'evidence':[]},{'checks':[{'result':'FAIL','exit_code':1}]}):
   with self.subTest(patch=patch):self.rejected_unchanged(r|patch,'dependency')
  self.invoke('dependency',r);s=ledger.read(self.state)
  self.assertEqual(s['milestones']['A']['implementation_status'],'in_progress')
  self.assertEqual(s['milestones']['A']['acceptance_status'],'NOT_RUN')
  self.assertEqual(s['milestones']['B']['dependency_evidence']['A']['acceptance_status'],'NOT_RUN')
  self.assertNotIn('dependency_evidence',s['milestones']['C'])
  self.assertEqual(ledger.update(self.root,'next')['current_milestone_id'],'A')
  result=self.invoke('block',self.record|{'blocker':'Legal release decision','unblock_condition':'Owner resolves'})
  self.assertEqual(result['next_milestone_id'],'B')
 def test_contradictory_or_incomplete_checks_cannot_advance(self):
  invalid=[{'result':'PASS','exit_code':1,'command':'failed'}, {'result':'PASS','exit_code':0}, {'result':'PASS','exit_code':'0','command':'not numeric'}, {'result':'PASS','command':'missing exit'}]
  for action in ('finish','dependency'):
   for check in invalid:
    with self.subTest(action=action,check=check):
     r=self.record|{'checks':[check]}
     if action=='dependency':r.update(milestone_id='B',dependency_id='A')
     self.rejected_unchanged(r,action)
 def test_dependency_rejects_dirty_staged_and_untracked_source(self):
  r=self.record|{'milestone_id':'B','dependency_id':'A'}
  for kind in ('dirty','staged','untracked'):
   with self.subTest(kind=kind):
    if kind=='untracked':(self.worker/'new-source.cui').write_text('source')
    else:
     (self.worker/'source.txt').write_text('modified')
     if kind=='staged':self.git('add','source.txt')
    self.rejected_unchanged(r,'dependency');self.git('reset','--hard','HEAD')
    (self.worker/'new-source.cui').unlink(missing_ok=True)
if __name__=='__main__':unittest.main(verbosity=2)
