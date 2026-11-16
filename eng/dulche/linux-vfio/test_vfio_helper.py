"""CPU-only tests. No modprobe, ACL utility, sysfs write or helper main is run."""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch

import vfio_helper as helper


class ParsingTests(unittest.TestCase):
    def test_duplicate_nonfinite_or_nonobject_control_is_refused(self):
        for data in (b'{"action":"bind","action":"stop"}', b'{"sent_ns":NaN}',
                     b'{"sent_ns":Infinity}', b'[]', b'not json'):
            with self.subTest(data=data), self.assertRaises(helper.Refusal):
                helper.strict_json(data)

    def test_stop_sequence_zero_is_valid_before_binding(self):
        request={'action':'stop','session':'a'*32,'sent_ns':10,'sequence':0}
        self.assertEqual(helper.validate_control(request,'a'*32,-1,100,False,20),(10,0))

    def test_stale_future_replayed_and_bool_controls_refuse(self):
        base={'action':'heartbeat','session':'a'*32,'sent_ns':100,'sequence':1}
        for changes in ({'sent_ns':-1},{'sent_ns':True},{'sent_ns':10},{'sent_ns':300},
                        {'sequence':True},{'sequence':0},{'session':'b'*32}):
            with self.subTest(changes=changes),self.assertRaises(helper.Refusal):
                helper.validate_control(dict(base,**changes),'a'*32,0,10,True,200)
        with self.assertRaises(helper.Refusal):
            helper.validate_control(base,'a'*32,0,10,True,helper.LEASE_NS+101)

    def test_effective_config_refuses_device_ids_hooks_and_unsafe_options(self):
        helper.validate_module_config(b'alias devname:vfio/vfio vfio\noptions vfio enable_unsafe_noiommu_mode=0\nsoftdep vfio-pci pre: vfio_iommu_type1\n')
        for line in (b'options vfio-pci ids=10de:1db4', b'options vfio_pci ids=',
                     b'install vfio-pci /bin/true',b'remove vfio /bin/true',
                     b'options vfio enable_unsafe_noiommu_mode=1',
                     b'options vfio_iommu_type1 allow_unsafe_interrupts=1',
                     b'softdep vfio-pci pre: nvidia', b'alias pci:any vfio-pci'):
            with self.subTest(line=line),self.assertRaises(helper.Refusal):
                helper.validate_module_config(line)

    def test_dependency_plan_is_closed_to_approved_modules(self):
        with patch.object(helper.platform,'release',return_value='unit-kernel'):
            helper.validate_dependency_plan(b'insmod /lib/modules/unit-kernel/kernel/vfio.ko.zst\ninsmod /lib/modules/unit-kernel/kernel/vfio-pci.ko.zst\n')
            for plan in (b'install /bin/anything',b'insmod /tmp/vfio.ko',
                         b'insmod /lib/modules/unit-kernel/nvidia.ko',
                         b'insmod /lib/modules/unit-kernel/vfio.ko ids=10de:1db4',b''):
                with self.subTest(plan=plan),self.assertRaises(helper.Refusal):
                    helper.validate_dependency_plan(plan)


class AuthorityTests(unittest.TestCase):
    def records(self):
        manifest='b'*64
        owner={'controller_pid':1234,'controller_start_ticks':99,
               'boot_id':'11111111-1111-1111-1111-111111111111','session':'a'*32}
        common={'proof_id':helper.PROOF,'manifest_sha256':manifest}
        canonical={'repository':'CroakyJake12/9to1','remote_ref':'refs/heads/main'}
        consumed={**common,**canonical,'schema':'dulche.v100-consumed/v1','consumed':True}
        encode=lambda value:(json.dumps(value,sort_keys=True)+'\n').encode()
        csha=hashlib.sha256(encode(consumed)).hexdigest()
        publication={**common,**canonical,'schema':'dulche.v100-publication/v1',
                     'consumed_sha256':csha,'consumed_commit_sha':'c'*40,'observed_main_sha':'c'*40}
        dispatch={**common,'schema':'dulche.v100-dispatch-claimed/v1','consumed_sha256':csha,
                  'consumed_commit_sha':'c'*40,'no_retry':True,'owner':owner}
        dsha=hashlib.sha256(encode(dispatch)).hexdigest()
        dp={**common,**canonical,'schema':'dulche.v100-dispatch-publication/v1','consumed_sha256':csha,
            'dispatch_sha256':dsha,'consumed_commit_sha':'c'*40,'dispatch_commit_sha':'d'*40,
            'observed_main_sha':'d'*40,'owner':owner}
        execution={**common,'schema':'dulche.v100-execution-started/v1','consumed_sha256':csha,
                   'dispatch_sha256':dsha,'dispatch_commit_sha':'d'*40,'no_retry':True,'owner':owner}
        records=dict(zip(('CONSUMED.json','PUBLICATION.json','DISPATCH-CLAIMED.json',
                          'DISPATCH-PUBLICATION.json','EXECUTION-STARTED.json'),
                         (consumed,publication,dispatch,dp,execution)))
        return records,{k:encode(v) for k,v in records.items()},csha,manifest,owner

    def test_complete_canonical_owner_bound_chain_is_required(self):
        records,raw,csha,manifest,owner=self.records()
        helper.validate_authority_records(records,raw,csha,manifest,owner)
        for field,value in (('controller_pid',1235),('controller_start_ticks',100),
                            ('boot_id','22222222-2222-2222-2222-222222222222'),('session','b'*32)):
            with self.subTest(field=field),self.assertRaises(helper.Refusal):
                helper.validate_authority_records(records,raw,csha,manifest,dict(owner,**{field:value}))
        for missing in ('DISPATCH-PUBLICATION.json','EXECUTION-STARTED.json'):
            copy_records=dict(records);del copy_records[missing]
            with self.assertRaises(helper.Refusal):
                helper.validate_authority_records(copy_records,raw,csha,manifest,owner)

    def test_changed_dispatch_bytes_or_publication_chain_refuse(self):
        records,raw,csha,manifest,owner=self.records()
        for name,key,value in (
            ('CONSUMED.json','consumed',False),
            ('DISPATCH-CLAIMED.json','no_retry',False),
            ('DISPATCH-PUBLICATION.json','dispatch_sha256','e'*64),
            ('DISPATCH-PUBLICATION.json','observed_main_sha','e'*40),
            ('EXECUTION-STARTED.json','dispatch_commit_sha','e'*40),
            ('EXECUTION-STARTED.json','no_retry',False)):
            changed=copy.deepcopy(records);changed[name][key]=value
            with self.subTest(name=name,key=key),self.assertRaises(helper.Refusal):
                helper.validate_authority_records(changed,raw,csha,manifest,owner)
        with self.assertRaises(helper.Refusal):
            helper.validate_authority_records(records,dict(raw,**{'DISPATCH-CLAIMED.json':b'changed'}),csha,manifest,owner)

    def test_authority_reader_uses_all_files_and_rejects_duplicate_json(self):
        records,raw,csha,manifest,owner=self.records()
        with tempfile.TemporaryDirectory(prefix='dulche-helper-auth-fixture-') as directory,\
                patch.object(helper,'AUTHORITY',Path(directory)):
            for name,data in raw.items():(Path(directory)/name).write_bytes(data)
            helper.validate_consumed(csha,manifest,owner)
            (Path(directory)/'EXECUTION-STARTED.json').write_bytes(b'{"owner":1,"owner":2}')
            with self.assertRaises(helper.Refusal):helper.validate_consumed(csha,manifest,owner)

    def test_controller_parent_pid_start_and_boot_are_checked(self):
        with patch.object(helper.os,'getppid',return_value=1234),\
                patch.object(Path,'stat',return_value=SimpleNamespace(st_uid=helper.OWNER_UID)),\
                patch.object(helper,'process_start',return_value='99'),\
                patch.object(helper,'read',return_value='11111111-1111-1111-1111-111111111111'):
            self.assertEqual(helper.controller_owner(1234,99,'a'*32)['controller_start_ticks'],99)
            with self.assertRaises(helper.Refusal):helper.controller_owner(1234,100,'a'*32)
            with self.assertRaises(helper.Refusal):helper.controller_owner(1235,99,'a'*32)


class BindingTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='dulche-vfio-mock-')
        self.addCleanup(self.temp.cleanup)
        root=Path(self.temp.name)
        self.pci=root/'pci';self.pci.mkdir();(self.pci/helper.GPU).mkdir()
        self.nodes=root/'vfio';self.nodes.mkdir()
        self.override=self.pci/helper.GPU/'driver_override';self.override.write_text('vfio-pci\n')
        self.driver=self.pci/helper.GPU/'driver'
        self.addCleanup(patch.stopall)
        patch.object(helper,'PCI',self.pci).start();patch.object(helper,'VFIO_DEVICES',self.nodes).start()
        self.binding=helper.Binding('18');self.binding.start_ns=time.monotonic_ns()
        self.binding.override_changed=True

    def test_foreign_override_is_never_cleared(self):
        self.override.write_text('another-driver\n')
        with patch.object(self.binding,'write') as write:
            result=self.binding.restore(True)
        self.assertFalse(result['restored']);write.assert_not_called()

    def test_owned_override_clear_and_already_clear_are_safe(self):
        def scoped_write(path,value,**kwargs):
            self.assertEqual(path,self.override);path.write_text(value)
        with patch.object(self.binding,'write',side_effect=scoped_write) as write:
            self.assertTrue(self.binding.restore(True)['restored'])
            self.assertEqual(write.call_count,1)
            self.assertTrue(self.binding.restore(True)['restored'])
            self.assertEqual(write.call_count,1)

    def test_uncertain_binding_or_running_acl_child_inhibits_all_writes(self):
        self.binding.binding_uncertain=True
        with patch.object(self.binding,'write') as write,patch.object(self.binding,'command') as command:
            self.assertFalse(self.binding.restore(True)['restored'])
            write.assert_not_called();command.assert_not_called()
        self.binding.binding_uncertain=False;self.binding.acl_uncertain=True
        self.binding.acl_pending_process=MagicMock();self.binding.acl_pending_process.poll.return_value=None
        with patch.object(self.binding,'write') as write,patch.object(self.binding,'command') as command:
            self.assertFalse(self.binding.restore(True)['restored'])
            write.assert_not_called();command.assert_not_called()

    def test_acl_child_ceased_allows_restore_only_original_node_and_acl(self):
        node=self.nodes/'18';node.write_bytes(b'CPU fixture only')
        st=node.stat();self.binding.node_identity=(st.st_dev,st.st_ino,st.st_rdev)
        self.binding.prior_acl='user::rw-\ngroup::---\nother::---'
        self.binding.acl_changed=self.binding.acl_uncertain=True
        self.binding.acl_pending_process=MagicMock();self.binding.acl_pending_process.poll.return_value=-9
        with patch.object(self.binding,'command',return_value=b'') as command,patch.object(self.binding,'write'):
            self.binding.restore(True)
        self.assertEqual(command.call_args.args[0],['/usr/bin/setfacl','--set','user::rw-,group::---,other::---',str(node)])
        self.binding.node_identity=(999,999,999)
        with patch.object(self.binding,'command') as command,patch.object(self.binding,'write') as write:
            self.assertFalse(self.binding.restore(True)['restored'])
            command.assert_not_called()
            write.assert_not_called()

    def test_uncertain_acl_restore_stops_before_any_device_write(self):
        node=self.nodes/'18';node.write_bytes(b'CPU fixture only')
        st=node.stat();self.binding.node_identity=(st.st_dev,st.st_ino,st.st_rdev)
        self.binding.prior_acl='user::rw-\ngroup::---\nother::---';self.binding.acl_changed=True
        fake=MagicMock();fake.poll.return_value=None
        with patch.object(self.binding,'command',side_effect=helper.WriteUncertain('mock timeout',fake)),patch.object(self.binding,'write') as write:
            self.assertFalse(self.binding.restore(True)['restored'])
            write.assert_not_called()

    def test_deadline_prevents_new_write_and_caps_remaining_timeout(self):
        self.binding.start_ns=0
        with patch.object(helper.time,'monotonic_ns',return_value=8_000_000_000),patch.object(helper,'bounded_sysfs_write') as write:
            with self.assertRaises(helper.Refusal):self.binding.write(self.override,'\n')
            write.assert_not_called()
        with patch.object(helper.time,'monotonic_ns',return_value=29_900_000_000),patch.object(helper,'bounded_sysfs_write') as write:
            self.binding.write(self.override,'\n',cleanup=True)
            self.assertLessEqual(write.call_args.args[2],.1)

    def test_changed_dependency_plan_refuses_before_module_load_or_sysfs(self):
        self.binding.module_plan={'plans':{'vfio-pci':b'old plan'}}
        plan=f'insmod /lib/modules/{helper.platform.release()}/kernel/vfio.ko\n'.encode()
        with patch.object(helper,'snapshot',return_value={'group':'18'}),\
                patch.object(helper,'validate_snapshot'),patch.object(Path,'exists',return_value=False),\
                patch.object(self.binding,'command',return_value=plan) as command,\
                patch.object(self.binding,'write') as write:
            with self.assertRaisesRegex(helper.Refusal,'plan changed'):
                self.binding.bind(time.monotonic_ns())
        self.assertEqual(command.call_count,1)
        self.assertEqual(command.call_args.args[0],['/usr/sbin/modprobe','--show-depends','vfio-pci'])
        write.assert_not_called()


class ProcessTests(unittest.TestCase):
    def test_command_exit_race_drains_trailing_output(self):
        process=MagicMock();process.stdout.fileno.return_value=9999;process.poll.return_value=0
        with patch.object(helper.subprocess,'Popen',return_value=process),patch.object(helper.os,'set_blocking'),\
                patch.object(helper.os,'read',side_effect=[BlockingIOError(),b'user::rw-\n',b'']),patch.object(helper.time,'sleep'):
            self.assertEqual(helper.bounded_command(['/never/executed']),b'user::rw-\n')
        process.kill.assert_not_called()

    def test_actual_cpu_command_and_timeout_are_bounded(self):
        command=[sys.executable,'-I','-S','-B','-c','import os; os.write(1,b"whole output\\n")']
        self.assertEqual(helper.bounded_command(command,.5),b'whole output\n')
        started=time.monotonic()
        with self.assertRaises(helper.WriteUncertain) as caught:
            helper.bounded_command([sys.executable,'-I','-S','-B','-c','import time; time.sleep(20)'],.05)
        process=caught.exception.process
        self.assertIsNotNone(process)
        process.wait(timeout=1)  # This owned CPU fixture is not a kernel/device operation.
        self.assertLess(time.monotonic()-started,1)

    def test_pidfd_stops_only_owned_cpu_child(self):
        command=[sys.executable,'-I','-S','-B','-c','import time; time.sleep(20)']
        owned=subprocess.Popen(command);sentinel=subprocess.Popen(command)
        try:
            fd=os.pidfd_open(owned.pid)
            try:self.assertTrue(helper.stop_qemu(fd,time.monotonic_ns()+1_000_000_000))
            finally:os.close(fd)
            owned.wait(timeout=1);self.assertIsNone(sentinel.poll())
        finally:
            for process in (owned,sentinel):
                if process.poll() is None:process.kill()
                process.wait(timeout=1)

    def test_stop_failure_still_reports_rollback_refusal(self):
        binding=MagicMock();binding.restore.return_value={'restored':False,'reason':'QEMU unconfirmed'}
        with patch.object(helper,'stop_qemu',side_effect=OSError('mock failure')),patch.object(helper,'emit') as emit:
            self.assertFalse(helper.finalize(binding,999,time.monotonic_ns(),'mock stop','a'*32))
        binding.restore.assert_called_once_with(False)
        self.assertEqual(emit.call_args.args,('stopped',))
        self.assertFalse(emit.call_args.kwargs['qemu_stopped'])
        self.assertEqual(emit.call_args.kwargs['cleanup']['reason'],'QEMU unconfirmed')

    def test_unbound_stop_needs_no_claim_or_device_writes(self):
        binding=MagicMock()
        with patch.object(helper,'stop_qemu',return_value=True),patch.object(helper,'emit') as emit:
            self.assertTrue(helper.finalize(binding,999,None,'controller_stop','a'*32,True))
        binding.restore.assert_not_called()
        self.assertTrue(emit.call_args.kwargs['binding_restored'])

    def test_main_cleanup_exception_still_closes_all_mocked_owned_fds(self):
        # Every privileged/system operation is mocked. This exercises only the
        # ExitStack and control flow; no actual helper process or root action.
        argv=['vfio_helper.py','--qemu-pid','5678','--qemu-start','88',
              '--qemu-name','dulche-v100-proof-fixture','--session','a'*32,
              '--controller-pid','1234','--controller-start','99']
        message=json.dumps({'action':'stop','session':'a'*32,'sequence':0,'sent_ns':1_000_000_000}).encode()+b'\n'
        selected=MagicMock();selected.__enter__.return_value=selected;selected.select.return_value=[(None,None)]
        with patch.object(sys,'argv',argv),patch.object(helper.os,'geteuid',return_value=0),\
                patch.object(helper.os,'open',return_value=100),patch.object(helper.os,'close') as close,\
                patch.object(helper.os,'fstat',return_value=SimpleNamespace(st_mode=0o100600,st_uid=0)),\
                patch.object(helper.fcntl,'flock'),patch.object(helper,'controller_owner'),\
                patch.object(helper.os,'pidfd_open',return_value=101),patch.object(helper.os,'set_blocking'),\
                patch.object(helper,'snapshot',return_value={'group':'18'}),patch.object(helper,'validate_snapshot'),\
                patch.object(helper,'pin_qemu',return_value=102),\
                patch.object(helper,'prepare_module_plan',return_value={'config_sha256':'f'*64}),\
                patch.object(helper,'emit'),patch.object(helper.time,'monotonic_ns',return_value=1_000_000_000),\
                patch.object(helper.selectors,'DefaultSelector',return_value=selected),\
                patch.object(helper.select,'select',return_value=([],[],[])),\
                patch.object(helper.os,'read',return_value=message),\
                patch.object(helper,'finalize',side_effect=RuntimeError('mock cleanup failure')):
            with self.assertRaisesRegex(RuntimeError,'mock cleanup failure'):helper.main()
        self.assertEqual([call.args[0] for call in close.call_args_list],[102,101,100])


if __name__=='__main__':
    unittest.main()
