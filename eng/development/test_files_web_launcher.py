import os, subprocess, tempfile, pathlib, unittest, stat, shutil
HERE=pathlib.Path(__file__).resolve().parent
SCRIPT=HERE/'files-web-launcher.sh'
class LauncherTests(unittest.TestCase):
  def run_it(self,home,bindir,*args):
    env={**os.environ,'HOME':str(home),'PATH':str(bindir)}
    return subprocess.run(['/usr/bin/bash',str(SCRIPT),*args],env=env,text=True,capture_output=True)
  def fixture(self):
    tmp=tempfile.TemporaryDirectory();self.addCleanup(tmp.cleanup)
    root=pathlib.Path(tmp.name);home=root/'home';home.mkdir();bin=root/'bin';bin.mkdir()
    # Use a hermetic executable search path: CI's installed Chromium must
    # not override the disposable browser fixture on display-less runners.
    for tool in ('dirname', 'pwd', 'basename', 'mkdir', 'cat', 'chmod', 'rm'):
      installed=shutil.which(tool)
      self.assertIsNotNone(installed,f'Missing required fixture utility: {tool}')
      (bin/tool).symlink_to(installed)
    return home,bin
  def browser(self,bin,name):
    p=bin/name;p.write_text('#!/bin/sh\nprintf "%s\\n" "$@" > "${HOME}/called"\n');p.chmod(0o700)
  def test_check_has_no_browser_side_effect(self):
    home,bin=self.fixture();self.browser(bin,'google-chrome-stable')
    r=self.run_it(home,bin,'--check')
    self.assertEqual(r.returncode,0,r.stderr);self.assertIn('google-chrome-stable',r.stdout)
    self.assertIn('https://9to1.uk/files/#/app.files',r.stdout)
    self.assertFalse((home/'called').exists())
  def test_launch_uses_app_window_and_fixed_https_url(self):
    home,bin=self.fixture();self.browser(bin,'google-chrome-stable')
    r=self.run_it(home,bin,'launch')
    self.assertEqual(r.returncode,0,r.stderr)
    self.assertEqual((home/'called').read_text(),'--app=https://9to1.uk/files/#/app.files\n')
  def test_chromium_fallback(self):
    home,bin=self.fixture();self.browser(bin,'chromium')
    r=self.run_it(home,bin)
    self.assertEqual(r.returncode,0,r.stderr)
    self.assertEqual((home/'called').read_text(),'--app=https://9to1.uk/files/#/app.files\n')
  def test_browser_priority_ignores_host_installations(self):
    home,bin=self.fixture();self.browser(bin,'chromium');self.browser(bin,'google-chrome-stable')
    r=self.run_it(home,bin,'--check')
    self.assertEqual(r.returncode,0,r.stderr)
    self.assertIn('Browser: google-chrome-stable',r.stdout)
    self.assertFalse((home/'called').exists())
  def test_xdg_open_only_fallback_uses_plain_https_url(self):
    home,bin=self.fixture();self.browser(bin,'xdg-open')
    r=self.run_it(home,bin)
    self.assertEqual(r.returncode,0,r.stderr)
    self.assertEqual((home/'called').read_text(),'https://9to1.uk/files/#/app.files\n')
  def test_no_browser_or_opener_refuses_without_side_effects(self):
    home,bin=self.fixture()
    r=self.run_it(home,bin)
    self.assertEqual(r.returncode,127,r.stderr)
    self.assertIn('No supported browser or xdg-open',r.stderr)
    self.assertFalse((home/'called').exists())
    self.assertFalse((home/'.local').exists())
  def test_install_creates_noncredential_desktop_entry_and_refuses_overwrite(self):
    home,bin=self.fixture();self.browser(bin,'chromium')
    r=self.run_it(home,bin,'--install')
    self.assertEqual(r.returncode,0,r.stderr)
    entry=home/'.local/share/applications/9to1-files-web.desktop'
    launcher=home/'.local/bin/9to1-files-web'
    self.assertTrue(entry.is_file());self.assertTrue(launcher.is_file())
    self.assertIn('Name=9to1 Files',entry.read_text());self.assertIn(f'Exec="{launcher}"',entry.read_text())
    self.assertEqual(stat.S_IMODE(launcher.stat().st_mode),0o700)
    initial=entry.read_bytes();again=self.run_it(home,bin,'--install')
    self.assertEqual(again.returncode,3);self.assertEqual(entry.read_bytes(),initial)
    r=self.run_it(home,bin,'--check');self.assertEqual(r.returncode,0)
  def test_refuses_unsafe_home_paths_without_creating_anything(self):
    home,bin=self.fixture(); bad=home/'unsafe%name';bad.mkdir()
    r=self.run_it(bad,bin,'--install')
    self.assertEqual(r.returncode,2);self.assertFalse((bad/'.local').exists())
  def test_does_not_overwrite_symlink_target(self):
    home,bin=self.fixture(); apps=home/'.local/share/applications'; apps.mkdir(parents=True)
    expected=home/'important-file';expected.write_text('protected')
    (apps/'9to1-files-web.desktop').symlink_to(expected)
    r=self.run_it(home,bin,'--install')
    self.assertEqual(r.returncode,3);self.assertEqual(expected.read_text(),'protected')
  def test_bad_option_has_no_side_effect(self):
    home,bin=self.fixture(); r=self.run_it(home,bin,'--delete')
    self.assertEqual(r.returncode,2);self.assertFalse((home/'.local').exists())
if __name__=='__main__':unittest.main()
