import copy
import unittest
from queue_guard import GATES, canonical_path, validate

HASH = 'a' * 64
PACKAGE = 'b' * 64

def task(tid='9T-TEST-001'):
    return dict(id=tid, owner='W2', status=None, source_refs=['V2:1-4'],
                acceptance=['Real save and fresh reopen preserve canonical identity'],
                next_checkpoint='First source-bound replay', effort=None, depends_on=[],
                claim='PROPOSED', write_paths=[], gates={g:'NOT_RUN' for g in GATES})

def queue(t=None):
    return {'workers':{'W1':{},'W2':{},'W7':{},'W8':{}},'tasks':[t or task()]}

def verified():
    t = task()
    t.update(status='VERIFIED', candidate_manifest_sha256=HASH)
    t['gates'] = {g:'PASS' for g in GATES}
    t['receipts'] = {}
    for g in GATES:
        r = {'candidate_manifest_sha256':HASH,'url':'https://example.test/evidence/'+g}
        if g in {'package','smoke','acceptance'}: r['package_sha256']=PACKAGE
        if g in {'smoke','acceptance'}: r.update(reviewer='W7' if g=='smoke' else 'W8',kind='PRODUCT_RUNTIME')
        if g=='acceptance': r['full_scope']=True
        t['receipts'][g]=r
    return t

class QueueGuardTests(unittest.TestCase):
    def test_empty_valid_queue(self): self.assertEqual(validate({'workers':{},'tasks':[]}),[])
    def test_unstarted_valid(self): self.assertEqual(validate(queue()),[])
    def test_complete_receipts_structurally_valid(self): self.assertEqual(validate(queue(verified())),[])
    def test_status_not_gate(self):
        t=task(); t['status']='IMPLEMENTED'; self.assertTrue(validate(queue(t)))
    def test_duplicate_ids(self):
        q=queue();q['tasks'].append(task());self.assertTrue(validate(q))
    def test_unknown_owner(self):
        t=task();t['owner']='W99';self.assertTrue(validate(queue(t)))
    def test_missing_acceptance(self):
        t=task();t.pop('acceptance');self.assertTrue(validate(queue(t)))
    def test_unknown_dependency(self):
        t=task();t['depends_on']=['missing'];self.assertTrue(validate(queue(t)))
    def test_cycle(self):
        a=task('a');b=task('b');a['depends_on']=['b'];b['depends_on']=['a'];q=queue();q['tasks']=[a,b];self.assertTrue(validate(q))
    def test_self_cycle(self):
        t=task();t['depends_on']=[t['id']];self.assertTrue(validate(queue(t)))
    def test_dag(self):
        a=task('a');b=task('b');b['depends_on']=['a'];q=queue();q['tasks']=[b,a];self.assertEqual(validate(q),[])
    def test_no_proposed_write_lease(self):
        t=task();t['write_paths']=['framework/CUI/Runtime'];self.assertTrue(validate(queue(t)))
    def test_ack_requires_receipt_and_branch(self):
        t=task();t.update(claim='ACKNOWLEDGED',write_paths=['a']);self.assertTrue(validate(queue(t)))
    def test_casefold_parent_conflict(self):
        a=task('a');b=task('b')
        for t,p in [(a,'Framework/CUI/Runtime'),(b,'framework/cui/runtime/view.cs')]:t.update(claim='ACKNOWLEDGED',ack_url='https://example.test/ack',branch=t['id'],write_paths=[p])
        q=queue();q['tasks']=[a,b];self.assertTrue(validate(q))
    def test_no_false_sibling_prefix_conflict(self):
        a=task('a');b=task('b')
        for t,p in [(a,'framework/Runtime'),(b,'framework/Runtime.Tests')]:t.update(claim='ACKNOWLEDGED',ack_url='https://example.test/ack',branch=t['id'],write_paths=[p])
        q=queue();q['tasks']=[a,b];self.assertEqual(validate(q),[])
    def test_bad_paths(self):
        for p in ['../a','/root','a//b','a/./b','C:/x','a\\b','a/*','a/.','a/..',' a','a.','a /b','']:
            with self.subTest(p=p),self.assertRaises(ValueError):canonical_path(p)
    def test_safe_path_with_spaces(self):self.assertEqual(canonical_path('9to1 Workspace/Files/'),'9to1 workspace/files')
    def test_verified_not_just_build(self):
        t=task();t['status']='VERIFIED';self.assertTrue(validate(queue(t)))
    def test_completed_needs_implementation_receipt(self):
        t=task();t['status']='COMPLETED';self.assertTrue(validate(queue(t)))
    def test_stale_candidate(self):
        t=verified();t['receipts']['smoke']['candidate_manifest_sha256']='c'*64;self.assertTrue(validate(queue(t)))
    def test_wrong_artifact(self):
        t=verified();t['receipts']['acceptance']['package_sha256']='c'*64;self.assertTrue(validate(queue(t)))
    def test_author_cannot_verify(self):
        t=verified();t['receipts']['acceptance']['reviewer']='W2';self.assertTrue(validate(queue(t)))
    def test_unknown_reviewer(self):
        t=verified();t['receipts']['smoke']['reviewer']='W99';self.assertTrue(validate(queue(t)))
    def test_fixture_is_not_smoke(self):
        t=verified();t['receipts']['smoke']['kind']='FIXTURE';self.assertTrue(validate(queue(t)))
    def test_acceptance_needs_full_scope(self):
        t=verified();t['receipts']['acceptance']['full_scope']=False;self.assertTrue(validate(queue(t)))
    def test_runtime_needs_package(self):
        t=verified();t['gates']['package']='NOT_RUN';self.assertTrue(validate(queue(t)))
    def test_invalid_receipt_shape(self):
        t=verified();t['receipts']['smoke']=None;self.assertTrue(validate(queue(t)))
    def test_missing_snapshot_hash(self):
        t=verified();t.pop('candidate_manifest_sha256');self.assertTrue(validate(queue(t)))

if __name__=='__main__':unittest.main()
