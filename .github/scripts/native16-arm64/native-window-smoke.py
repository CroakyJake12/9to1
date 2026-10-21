import os,pathlib,subprocess,time,json,sys,datetime
out=pathlib.Path('evidence');profile=pathlib.Path(os.environ['RUNNER_TEMP'])/'9to1-preview-arm64-fresh'
if profile.exists():raise SystemExit('Fresh profile already exists')
log=(out/'app.stdout.log').open('w');env=dict(os.environ,HAVEN_DATA_DIR=str(profile))
wm=subprocess.Popen(['openbox'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
app=subprocess.Popen(['/usr/bin/9to1-preview'],env=env,stdout=log,stderr=subprocess.STDOUT)
result={'architecture':'aarch64','fresh_profile':True,'native_window':'NOT OBSERVED','normal_close':'NOT RUN','lesson_workflow':'NOT RUN','app_functionality':'NOT ACCEPTED; actual screenshot review required','baseline':'Internal Native16, no AI/browser promise'}
try:
    deadline=time.monotonic()+45;win=None
    while time.monotonic()<deadline:
        if app.poll() is not None:raise RuntimeError('App exited before observed window: '+str(app.returncode))
        rows=subprocess.check_output(['wmctrl','-lp'],text=True).splitlines()
        hits=[x.split()[0] for x in rows if len(x.split())>2 and x.split()[2]==str(app.pid)]
        if hits:win=hits[0];break
        time.sleep(.25)
    if not win:raise RuntimeError('No actual native app window in45seconds')
    result['native_window']='OBSERVED';result['window_id']=win
    result['window_title']=subprocess.check_output(['wmctrl','-l'],text=True)
    if 'startup failed' in result['window_title'].lower():raise RuntimeError('Bootstrap failure window, not native app readiness')
    time.sleep(2)
    subprocess.run(['import','-window','root',str(out/'fresh-arm64-window.png')],check=True)
    (out/'windows.txt').write_text(subprocess.check_output(['wmctrl','-lp'],text=True))
    subprocess.run(['wmctrl','-ic',win],check=True)
    code=app.wait(timeout=25);result['normal_exit_code']=code
    if code!=0:raise RuntimeError('Normal window close returned '+str(code))
    result['normal_close']='PASS';result['status']='PASS_NATIVE_WINDOW_AND_CLOSE_ONLY_NEEDS_SCREENSHOT_REVIEW'
    manifest=pathlib.Path('/usr/share/doc/9to1-preview/source-and-build.json')
    result['installed_build_provenance']=json.loads(manifest.read_text())
except BaseException as e:
    result['status']='FAIL';result['error']=repr(e)
    if app.poll() is None:app.terminate();app.wait(timeout=10)
finally:
    log.close();wm.terminate();result['at']=datetime.datetime.now(datetime.timezone.utc).isoformat();(out/'native-startup-receipt.json').write_text(json.dumps(result,indent=2)+'\n')
sys.exit(0 if result.get('status')=='PASS_NATIVE_WINDOW_AND_CLOSE_ONLY_NEEDS_SCREENSHOT_REVIEW' else 1)
