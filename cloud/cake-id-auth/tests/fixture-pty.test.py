#!/usr/bin/env python3
"""Actual private PTY fault controls; outer test subreaper strictly drains its own family."""
import fcntl, hashlib, importlib.util, json, os, pty, shutil, subprocess, tempfile, termios, time, unittest
from pathlib import Path
ROOT = Path(__file__).parent.resolve()
spec = importlib.util.spec_from_file_location('custodian', ROOT/'linux-fixture-custodian.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
def preserve_control_failures(primary, cleanup):
    if cleanup:
        raise BaseExceptionGroup('Original PTY control and independent cleanup failures retained',
                                 ([primary] if primary is not None else []) + cleanup)
    if primary is not None:
        raise primary

class FailureAndReceiptControls(unittest.TestCase):
    def test_original_and_independent_cleanup_error_objects_preserved(self):
        primary=AssertionError('original'); first=OSError('close'); second=RuntimeError('drain')
        with self.assertRaises(ExceptionGroup) as result:
            preserve_control_failures(primary,[first,second])
        self.assertEqual((primary,first,second),result.exception.exceptions)
        with self.assertRaises(AssertionError) as result:
            preserve_control_failures(primary,[])
        self.assertIs(primary,result.exception)
    def test_actual_durable_receipt_short_writes_complete(self):
        original=m.os.write
        with tempfile.TemporaryFile() as stream:
            def short(fd,data): return original(fd,data[:3])
            m.os.write=short
            try: m.publish_receipt(stream.fileno(),b'complete-private-receipt')
            finally: m.os.write=original
            stream.seek(0);self.assertEqual(b'complete-private-receipt',stream.read())
    def test_zero_write_refuses_durable_proof(self):
        original=m.os.write
        with tempfile.TemporaryFile() as stream:
            m.os.write=lambda fd,data:0
            try:
                with self.assertRaisesRegex(RuntimeError,'write incomplete'):
                    m.publish_receipt(stream.fileno(),b'no-proof')
            finally: m.os.write=original
            stream.seek(0);self.assertEqual(b'',stream.read())

class PtyControls(unittest.TestCase):
    def exercise(self, mode, fault):
        c = m.Custody()
        temporary = None; master = None; slave = None; caller = None; primary = None
        try:
            temporary = tempfile.mkdtemp()
            folder = Path(temporary); ready=folder/'ready'; marker=folder/'worker'
            source=ROOT/'linux-fixture-custodian.py'
            if mode=='baseline-loss':
                source=folder/'aedc-custodian.py'
                body=subprocess.check_output(['git','show','aedc29ec3a0a960e1ded36c0443f1efda4fcf36d:cloud/cake-id-auth/tests/linux-fixture-custodian.py'],cwd=ROOT)
                source.write_bytes(body)
                print('Baseline AEDC custodian SHA256',hashlib.sha256(body).hexdigest(),flush=True)
            master,slave=pty.openpty()
            def own_terminal():
                os.setsid(); fcntl.ioctl(slave,termios.TIOCSCTTY,0)
            caller=subprocess.Popen(['node',str(ROOT/'fixture-caller-control.mjs'),mode,str(source),str(folder),str(ready),str(marker)],
                stdin=slave,stdout=slave,stderr=slave,preexec_fn=own_terminal,close_fds=True)
            os.close(slave); slave=None
            for _ in range(200):
                c.observe()
                if ready.exists() and marker.exists(): break
                time.sleep(.01)
            self.assertTrue(ready.exists() and marker.exists(),'actual workload ready')
            worker=int(marker.read_text());c.observe(); self.assertIn(worker,c.records)
            if fault=='ctrl-c': os.write(master,b'\x03')
            else: os.close(master);master=None # Genuine controlling-terminal hangup.
            for _ in range(250):
                c.observe()
                if (folder/'caller-finished.json').exists() or (folder/'receipt.json').exists() and (folder/'receipt.json').stat().st_size: break
                if mode=='baseline-loss' and m.stat(caller.pid)['state']=='Z': break
                time.sleep(.01)
            if mode=='baseline-loss':
                self.assertFalse((folder/'receipt.json').exists())
                self.assertIsNotNone(m.stat(worker),'original topology loses custodian without draining workload')
                self.assertNotEqual(m.stat(worker)['state'],'Z')
                print('EXPECTED BASELINE NEGATIVE: PTY loss leaves workload alive, no strict receipt',flush=True)
            else:
                receipt=json.loads((folder/'receipt.json').read_text())
                self.assertTrue(receipt['strictReaped'] and receipt['originalsDisappeared'])
                self.assertIsNone(m.stat(worker))
                if mode=='handled': self.assertTrue((folder/'caller-finished.json').exists())
                else: self.assertFalse((folder/'caller-finished.json').exists(),'dead caller never cleans private state')
                print('Owned-worker strict receipt after',mode,fault,flush=True)
        except BaseException as error:
            primary=error
        finally:
            cleanup=[]; drained=False
            for fd in (master,slave):
                if fd is not None:
                    try: os.close(fd)
                    except BaseException as error: cleanup.append(error)
            try:
                proof=c.drain();self.assertTrue(proof['strictReaped'] and proof['originalsDisappeared']);drained=True
                if caller is not None: caller.returncode=0 # Kernel-reaped by outer custodian; no numeric wait.
            except BaseException as error: cleanup.append(error)
            if drained and temporary is not None:
                try: shutil.rmtree(temporary)
                except BaseException as error: cleanup.append(error)
            preserve_control_failures(primary,cleanup)
    def test_acquisition_failure_retains_original_and_closes_actual_pty(self):
        original_spawn=subprocess.Popen; original_pty=pty.openpty; opened=[]
        failure=RuntimeError('isolated acquisition failure')
        def capture_pty():
            pair=original_pty();opened.extend(pair);return pair
        def unavailable(*args,**kwargs): raise failure
        subprocess.Popen=unavailable;pty.openpty=capture_pty
        try:
            with self.assertRaises(RuntimeError) as result: self.exercise('handled','ctrl-c')
            self.assertIs(failure,result.exception)
            self.assertEqual(2,len(opened))
            for fd in opened:
                with self.assertRaises(OSError): os.fstat(fd)
        finally:
            subprocess.Popen=original_spawn;pty.openpty=original_pty
    def test_original_shared_session_hangup_negative(self): self.exercise('baseline-loss','hangup')
    def test_detached_custodian_drains_after_unhandled_caller_loss(self): self.exercise('caller-loss','hangup')
    def test_actual_ctrl_c_handler_and_strict_receipt(self): self.exercise('handled','ctrl-c')
    def test_actual_hangup_handler_and_strict_receipt(self): self.exercise('handled','hangup')
if __name__=='__main__': unittest.main(verbosity=2)
