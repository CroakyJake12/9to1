#!/usr/bin/env python3
"""Offline regression checks for the local swarm's work and evidence boundaries."""
import importlib.util,pathlib,subprocess,tempfile,unittest
from unittest.mock import patch
sp=importlib.util.spec_from_file_location('swarm_under_test',pathlib.Path(__file__).with_name('harness.py'))
h=importlib.util.module_from_spec(sp);sp.loader.exec_module(h)
class Boundaries(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory()
  self.root=pathlib.Path(self.temp.name)
  self.repo=self.root/'repo';self.repo.mkdir()
  subprocess.run(['git','init','-q',str(self.repo)],check=True)
  self.allowed=['source.cs','tests.cs']
  (self.repo/'source.cs').write_text('class Source { const int Limit = 3; }\n')
  (self.repo/'tests.cs').write_text('class Tests { }\n')
  (self.repo/'other.cs').write_text('class Other { }\n')
  subprocess.run(['git','add','.'],cwd=self.repo,check=True)
 def tearDown(self):self.temp.cleanup()
 def test_new_candidate_directory_is_clean_but_real_scoped_edits_are_dirty(self):
  paths=['new-owned-dir/harness.py']
  self.assertFalse(h.canonical_paths_dirty(paths,cwd=self.repo))
  (self.repo/'new-owned-dir').mkdir()
  (self.repo/paths[0]).write_text('new owned source')
  self.assertTrue(h.canonical_paths_dirty(paths,cwd=self.repo))
  self.assertTrue(h.canonical_paths_dirty(['source.cs'],cwd=self.repo))
 def test_git_status_warnings_are_not_porcelain_and_failure_is_not_clean(self):
  with patch.object(h.subprocess,'run',return_value=subprocess.CompletedProcess([],0,'','warning: missing candidate directory')):
   self.assertFalse(h.canonical_paths_dirty(['future/source.cs'],cwd=self.repo))
  with patch.object(h.subprocess,'run',return_value=subprocess.CompletedProcess([],0,'?? future/source.cs\0','warning: harmless')):
   self.assertTrue(h.canonical_paths_dirty(['future/source.cs'],cwd=self.repo))
  with patch.object(h.subprocess,'run',return_value=subprocess.CompletedProcess([],128,'','fatal: not a repository')):
   with self.assertRaisesRegex(RuntimeError,'Git status failed'):h.canonical_paths_dirty(['future/source.cs'],cwd=self.repo)
 def test_native_usage_and_engine_fallback_only(self):
  self.assertEqual(h.output_tokens({'usage':{'completion_tokens':12},'timings':{'predicted_n':11}}),(12,'usage.completion_tokens'))
  self.assertEqual(h.output_tokens({'usage':{},'timings':{'predicted_n':7}}),(7,'timings.predicted_n'))
  self.assertEqual(h.output_tokens({'usage':{'completion_tokens':0},'timings':{'predicted_n':99}}),(0,'usage.completion_tokens'))
  self.assertEqual(h.output_tokens({'usage':{'completion_tokens':True},'choices':[{'message':{'content':'many words'}}]}),(None,None))
  self.assertEqual(h.output_tokens({'usage':{'completion_tokens':-1}}),(None,None))
 def test_owned_exact_edits_and_batch(self):
  reply=h.action(self.repo,{'action':'batch','actions':[{'action':'replace','path':'source.cs','old':'Limit = 3','new':'Limit = 4'},{'action':'replace','path':'tests.cs','old':'class Tests { }','new':'class Tests { /* regression */ }'}]},self.allowed,'')
  self.assertIn('Applied exact replacement',reply)
  self.assertIn('Limit = 4',(self.repo/'source.cs').read_text())
  self.assertIn('regression',(self.repo/'tests.cs').read_text())
 def test_unowned_and_ambiguous_edits_are_rejected(self):
  original=(self.repo/'other.cs').read_text()
  with self.assertRaises(ValueError):h.action(self.repo,{'action':'replace','path':'other.cs','old':'Other','new':'Changed'},self.allowed,'')
  self.assertEqual((self.repo/'other.cs').read_text(),original)
  (self.repo/'source.cs').write_text('same same')
  with self.assertRaises(ValueError):h.action(self.repo,{'action':'replace','path':'source.cs','old':'same','new':'different'},self.allowed,'')
  self.assertEqual((self.repo/'source.cs').read_text(),'same same')
 def test_symlinks_and_parent_paths_cannot_escape(self):
  external=self.root/'outside.cs';external.write_text('preserve')
  (self.repo/'link.cs').symlink_to(external)
  subprocess.run(['git','add','link.cs'],cwd=self.repo,check=True)
  for name in ('../outside.cs',str(external),'link.cs'):
   with self.subTest(name=name),self.assertRaises(ValueError):h.clean_path(self.repo,name)
  self.assertEqual(external.read_text(),'preserve')
 def test_notes_cannot_defeat_repeat_detection(self):
  a={'action':'read','path':'source.cs','start':1,'count':20,'notes':'first thought'}
  b={**a,'notes':'different thought'}
  self.assertEqual(h.operation_signature(self.repo,a),h.operation_signature(self.repo,b))
  batch1={'action':'batch','notes':'one','actions':[a]}
  batch2={'action':'batch','notes':'two','actions':[b]}
  self.assertEqual(h.operation_signature(self.repo,batch1),h.operation_signature(self.repo,batch2))
  before=h.operation_signature(self.repo,a)
  (self.repo/'source.cs').write_text('new content')
  self.assertNotEqual(before,h.operation_signature(self.repo,a))
 def test_initial_source_bundle_keeps_all_lines(self):
  lines=['// line '+str(n) for n in range(600)]
  (self.repo/'source.cs').write_text('\n'.join(lines))
  bundle=h.task_source_bundle(self.repo,self.allowed)
  self.assertIn('// line 0\n',bundle);self.assertIn('// line 599',bundle)
  self.assertIn('class Tests',bundle)
 def test_only_real_passed_test_runs_count(self):
  cases=[('Passed! - Failed: 0, Passed: 3, Skipped: 1, Total: 4',0,True),
   ('Test Run Successful.\nTotal tests: 3\n     Passed: 3',0,True),
   ('Total tests: 4\n    Skipped: 4',0,False),
   ('Passed! - Failed: 0, Passed: 0, Skipped: 4, Total: 4',0,False),
   ('Total tests: 1\nPassed: 1',1,False),
   ('No test matches the given testcase filter.\nTotal tests: 0',0,False)]
  for raw,code,expected in cases:
   with self.subTest(raw=raw):self.assertEqual(h.tests_passed(raw,code),expected)
 def test_observed_batch_envelopes_apply_owned_patch(self):
  raw={'notes':'observed wrapper','actions':[{'action':'batch','actions':[{'action':'replace','path':'tests.cs','old':'class Tests { }','new':'class Tests { /* accepted */ }'}]}]}
  action=h.normalize_action(raw)
  self.assertEqual(action['action'],'batch')
  self.assertEqual(len(action['actions']),1)
  h.action(self.repo,action,self.allowed,'')
  self.assertIn('accepted',(self.repo/'tests.cs').read_text())
  done=h.normalize_action({'notes':'complete','actions':[{'action':'done','summary':'ready'}]})
  self.assertEqual(done['action'],'done')
 def test_batch_limits_survive_normalization(self):
  with self.assertRaises(ValueError):h.normalize_action({'actions':[{'action':'diff'}]*7})
  with self.assertRaises(ValueError):h.normalize_action({'actions':[]})
  with self.assertRaises(ValueError):h.normalize_action({'actions':['invalid']})
  obj={'action':'diff'}
  for _ in range(7):obj={'action':'batch','actions':[obj]}
  with self.assertRaises(ValueError):h.normalize_action(obj)
 def test_candidate_diff_cannot_escape_owned_scope(self):
  self.assertEqual(h.validate_changed_paths(['tests.cs','source.cs'],self.allowed),['source.cs','tests.cs'])
  with self.assertRaises(ValueError):h.validate_changed_paths(['source.cs','other.cs'],self.allowed)
  with self.assertRaises(ValueError):h.validate_changed_paths([],self.allowed)
  with self.assertRaises(ValueError):h.validate_changed_paths(['../private'],['../private'])
 def test_formatting_failure_is_recoverable_feedback(self):
  (self.repo/'source.cs').write_text('class Source { }   \n')
  with self.assertRaisesRegex(ValueError,'formatting must be corrected'):h.validate_patch_format(self.repo)
  (self.repo/'source.cs').write_text('class Source { }\n')
  h.validate_patch_format(self.repo)
 def test_directory_discovery_stays_in_tracked_source(self):
  folder=self.repo/'9to1 Workspace/Types';folder.mkdir(parents=True)
  (folder/'Types.cs').write_text('public record SpaceLayoutNode(int Id, string Category, string Title);\n')
  (folder/'untracked.cs').write_text('private untracked text')
  (folder/'unsafe.pem').write_text('not a source')
  (folder/'escape.cs').symlink_to(self.repo/'source.cs')
  subprocess.run(['git','add','--','9to1 Workspace/Types/Types.cs','9to1 Workspace/Types/unsafe.pem','9to1 Workspace/Types/escape.cs'],cwd=self.repo,check=True)
  self.assertEqual(h.source_files(self.repo,'9to1 Workspace/Types'),['9to1 Workspace/Types/Types.cs'])
  result=h.action(self.repo,{'action':'search','path':'9to1 Workspace/Types','text':'record SpaceLayoutNode'},self.allowed,'')
  self.assertIn('Types.cs:1:',result);self.assertIn('Category',result)
  with self.assertRaises(ValueError):h.source_files(self.repo,'../outside')
 def test_early_compiler_error_survives_long_build_tail(self):
  raw='early.cs(8,2): error CS0246: missing XamlX\n'+'normal build output\n'*500
  self.assertIn('error CS0246',h.test_feedback(raw))
  self.assertIn('FINAL OUTPUT',h.test_feedback(raw))
 def test_v100_serializes_while_flash_is_independent(self):
  import threading,time
  entered=threading.Event();release=threading.Event();second_entered=threading.Event();flash_entered=threading.Event()
  guard=threading.Lock();active=0;peak=0;errors=[]
  def fake(role,task,messages,limit=1800):
   nonlocal active,peak
   if h.LANE.model==h.MODEL:flash_entered.set();return 'flash'
   with guard:active+=1;peak=max(peak,active)
   try:
    if task=='first':entered.set();release.wait(3)
    else:second_entered.set()
    return 'v100'
   finally:
    with guard:active-=1
  def call(model,task):
   try:h.LANE.model=model;h.gen('test',task,[],8)
   except Exception as error:errors.append(error)
  threads=[threading.Thread(target=call,args=(h.V100_MODEL,'first'),daemon=True),threading.Thread(target=call,args=(h.V100_MODEL,'second'),daemon=True),threading.Thread(target=call,args=(h.MODEL,'flash'),daemon=True)]
  with patch.object(h,'_gen',side_effect=fake),patch.object(h,'CUTOFF',time.time()+30):
   try:
    threads[0].start();self.assertTrue(entered.wait(2))
    threads[1].start();threads[2].start()
    self.assertTrue(flash_entered.wait(2));self.assertFalse(second_entered.wait(.08))
   finally:
    release.set()
    for thread in threads:
     if thread.ident is not None:thread.join(3)
  self.assertEqual(peak,1);self.assertTrue(second_entered.is_set());self.assertEqual(errors,[])
 def test_compacted_dependency_read_can_recover_without_erasing_history(self):
  import json,copy
  c=h.context_receipts
  operation={'action':'read','path':'source.cs','start':1,'count':250}
  exact='first dependency line\n'+'observed declaration body;\n'*300+'last dependency line'
  signature=h.operation_signature(self.repo,operation)
  cache=c.remember_receipt([],signature,operation,exact)
  history=[{'role':'assistant','content':json.dumps(operation)},{'role':'user','content':exact}]
  history.extend([{'role':'assistant','content':'{"action":"diff"}'},{'role':'user','content':'observed later response'}]*4)
  original=copy.deepcopy(history)
  compacted=c.compact_history(history,cache,'','',budget=4000)
  self.assertEqual(history,original)
  self.assertFalse(c.receipt_is_visible(cache[0],compacted))
  self.assertIn('source.cs',compacted[0]['content'])
  repeats={signature:99} # Historical repeat counts must not block an evicted receipt.
  self.assertEqual(c.read_recovery(cache,signature,compacted,repeats),exact)
  visible=[*compacted,{'role':'user','content':exact}]
  self.assertEqual(c.read_recovery(cache,signature,visible,repeats),exact)
  with self.assertRaisesRegex(RuntimeError,'already visible'):
   c.read_recovery(cache,signature,visible,repeats)
  self.assertEqual(history,original)
 def test_receipt_cache_bounds_and_file_revision_prevent_stale_replay(self):
  c=h.context_receipts;cache=[]
  operation={'action':'read','path':'source.cs','start':1,'count':10}
  signature=h.operation_signature(self.repo,operation)
  cache=c.remember_receipt(cache,signature,operation,'actual source')
  (self.repo/'source.cs').write_text('changed source')
  self.assertIsNone(c.read_recovery(cache,h.operation_signature(self.repo,operation),[],{}))
  for index in range(20):cache=c.remember_receipt(cache,str(index),operation,str(index)+'x'*23000)
  self.assertLessEqual(len(cache),c.MAX_RECEIPTS)
  self.assertLessEqual(sum(len(row['result']) for row in cache),c.MAX_CACHE_CHARS)
  self.assertEqual(cache[-1]['signature'],'19')
  snapshot=list(cache)
  self.assertEqual(c.remember_receipt(cache,'edit',{'action':'replace'},'modified'),snapshot)
if __name__=='__main__':unittest.main(verbosity=2)
