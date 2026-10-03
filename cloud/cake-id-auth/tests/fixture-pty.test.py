#!/usr/bin/env python3
"""Actual private PTY fault controls; outer test subreaper strictly drains its own family."""
import fcntl, hashlib, importlib.util, json, os, pty, subprocess, tempfile, termios, time, unittest
from pathlib import Path
ROOT = Path(__file__).parent.resolve()
spec = importlib.util.spec_from_file_location('custodian', ROOT/'linux-fixture-custodian.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
class PtyControls(unittest.TestCase):
    def exercise(self, mode, fault):
        c = m.Custody()
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder); ready=folder/'ready'; marker=folder/'worker'
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
            os.close(slave)
            try:
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
            finally:
                if master is not None: os.close(master)
                proof=c.drain();self.assertTrue(proof['strictReaped'] and proof['originalsDisappeared'])
                caller.returncode=0 # Already kernel-reaped by the outer test custodian, never numeric wait.
    def test_original_shared_session_hangup_negative(self): self.exercise('baseline-loss','hangup')
    def test_detached_custodian_drains_after_unhandled_caller_loss(self): self.exercise('caller-loss','hangup')
    def test_actual_ctrl_c_handler_and_strict_receipt(self): self.exercise('handled','ctrl-c')
    def test_actual_hangup_handler_and_strict_receipt(self): self.exercise('handled','hangup')
if __name__=='__main__': unittest.main(verbosity=2)
