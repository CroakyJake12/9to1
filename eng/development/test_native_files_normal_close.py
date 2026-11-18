"""Actual Linux child processes/disposable profiles; fake shells are NOT Haven."""
from __future__ import annotations
import os
from pathlib import Path
import signal
import shutil
import subprocess
import stat
import tempfile
import unittest

import native_files_normal_close as normal


class NormalCloseTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.scratch = self.root / 'private'
        self.scratch.mkdir(mode=0o700)
        self.app_dir = self.root / 'app'
        self.app_dir.mkdir()
        self.exe = self.app_dir / 'Haven'
        self.exe.write_text('#!/bin/sh\ntrap "exit 0" USR1\necho run >> "$HOME/app-history"\nwhile :; do sleep 0.1; done\n')
        self.exe.chmod(0o700)
        (self.app_dir / 'Haven.dll').write_bytes(b'fixture')
        (self.app_dir / 'Haven.runtimeconfig.json').write_text('{}')
        self.current_pid = None
        self.probed = 0

    def probe(self, pid):
        self.current_pid = pid
        self.probed += 1
        return ['0x1']

    def sender(self, _wid):
        os.kill(self.current_pid, signal.SIGUSR1)
        return True

    def go(self, **kw):
        return normal.run_normal_close(self.exe, self.scratch, real_elf=False,
                                       timeout=2, stable=0.03,
                                       window_probe=self.probe,
                                       close_sender=self.sender, **kw)

    def test_two_normal_exits_in_one_private_profile_and_app_fixture_state(self):
        result = self.go()
        self.assertEqual(result['status'],'PASS')
        self.assertEqual(result['attempt_reasons'],['normal-close-exit-zero']*2)
        self.assertTrue(result['independent_processes'])
        self.assertTrue(result['same_disposable_profile'])
        self.assertEqual((self.scratch/'native-files-normal-close/profile/app-history').read_text(),'run\nrun\n')
        for i in (1,2):
            log=self.scratch/f'native-files-normal-close/attempt-{i}.stderr.log'
            self.assertEqual(stat.S_IMODE(log.stat().st_mode),0o600)

    def test_refuses_unsupported_close_protocol(self):
        r = normal.run_normal_close(self.exe,self.scratch, real_elf=False,
                                    timeout=2, stable=0,
                                    window_probe=self.probe,close_sender=lambda _:False)
        self.assertEqual(r['status'],'FAIL')
        self.assertEqual(r['attempt_reasons'],['wm-delete-not-supported'])

    def test_nonzero_normal_exit_is_rejected(self):
        self.exe.write_text('#!/bin/sh\ntrap "exit 17" USR1\nwhile :; do sleep 0.1; done\n')
        r=self.go()
        self.assertEqual(r['status'],'FAIL')
        self.assertEqual(r['attempt_reasons'],['normal-close-nonzero-exit'])

    def test_unexpected_window_loss_fails_closed(self):
        calls=[0]
        def probe(pid):
            calls[0]+=1
            return ['0x1'] if calls[0]==1 else []
        r=normal.run_normal_close(self.exe,self.scratch,real_elf=False,timeout=1,
                                  stable=0,window_probe=probe,close_sender=self.sender)
        self.assertEqual(r['status'],'FAIL')
        self.assertEqual(r['attempt_reasons'],['owned-window-lost'])

    def test_window_never_appears(self):
        r=normal.run_normal_close(self.exe,self.scratch,real_elf=False,timeout=0.2,
                                  stable=0,window_probe=lambda _pid:[],close_sender=self.sender)
        self.assertEqual(r['status'],'FAIL')
        self.assertEqual(r['attempt_reasons'],['window-timeout'])

    def test_exclusive_scratch(self):
        self.go()
        with self.assertRaises(normal.CloseError): self.go()

    def test_refuses_public_scratch(self):
        self.scratch.chmod(0o755)
        with self.assertRaises(normal.CloseError): self.go()
        self.assertFalse((self.scratch/'native-files-normal-close').exists())

    def test_rejects_non_elf_without_optin_fixture(self):
        with self.assertRaises(normal.CloseError):
            normal.run_normal_close(self.exe,self.scratch,timeout=1,
                                    window_probe=self.probe,close_sender=self.sender)

    def test_invalid_X_window_ID_never_sends(self):
        for invalid in ('123', '0xZ', '', '0x'+'f'*17, '0x1;cmd'):
            self.assertFalse(normal.send_wm_delete(invalid))

    @unittest.skipUnless(Path('/usr/bin/xprop').is_file() and bool(shutil.which('gcc')) and bool(os.environ.get('DISPLAY'))
                         and subprocess.run(['/usr/bin/xprop','-root','_NET_CLIENT_LIST'],
                                            capture_output=True,timeout=3,check=False).returncode == 0,
                         'requires a running disposable X11 display and C compiler')
    def test_real_x11_wm_delete_protocol_and_stale_window(self):
        # Compiles a *synthetic* X11 apphost. This tests real WM_DELETE delivery,
        # not Haven behavior or Google Drive acceptance.
        c_source = r"""
#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <unistd.h>
int main(void) {
  Display *d = XOpenDisplay(NULL);
  if(!d) return 2;
  Window w = XCreateSimpleWindow(d, DefaultRootWindow(d), 30,30,340,200,0,0,0);
  Atom del = XInternAtom(d, "WM_DELETE_WINDOW", False);
  Atom pidprop = XInternAtom(d, "_NET_WM_PID", False);
  unsigned long pid = (unsigned long)getpid();
  XChangeProperty(d,w,pidprop,XA_CARDINAL,32,PropModeReplace,(unsigned char*)&pid,1);
  XStoreName(d,w,"Haven Synthetic Protocol Fixture");
  XSetWMProtocols(d,w,&del,1);
  XMapWindow(d,w); XFlush(d);
  XEvent e;
  for (;;) {
    XNextEvent(d,&e);
    if(e.type==ClientMessage && (Atom)e.xclient.data.l[0]==del) {
      XDestroyWindow(d,w); XCloseDisplay(d); return 0;
    }
  }
}
"""
        subprocess.run(['gcc','-x','c','-','-lX11','-o',str(self.exe)],
                       input=c_source,text=True,capture_output=True,check=True)
        self.exe.chmod(0o700)
        from native_files_linux_smoke import x11_windows
        result = normal.run_normal_close(self.exe, self.scratch, timeout=10, stable=0.1,
                                         window_probe=x11_windows)
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['attempt_reasons'], ['normal-close-exit-zero']*2)
        # Verify a now-invalid but syntactically valid window cannot crash
        # the harness's X11 process when the server responds BadWindow.
        self.assertFalse(normal.send_wm_delete('0xffffffff'))

    def test_invalid_timeout(self):
        with self.assertRaises(normal.CloseError):
            normal.run_normal_close(self.exe,self.scratch,real_elf=False,timeout=500,
                                    window_probe=self.probe,close_sender=self.sender)

if __name__=='__main__': unittest.main()
