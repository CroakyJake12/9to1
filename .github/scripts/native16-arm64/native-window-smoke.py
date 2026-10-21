import os,pathlib,subprocess,time,json,sys,datetime,re
out=pathlib.Path('evidence');profile=pathlib.Path(os.environ['RUNNER_TEMP'])/'9to1-preview-arm64-fresh'
if profile.exists():raise SystemExit('Fresh profile already exists')
if os.geteuid()==0:raise SystemExit('Native acceptance must run as normal user')
log=(out/'app.stdout.log').open('w');env=dict(os.environ,HAVEN_DATA_DIR=str(profile));app=None
wm_log=(out/'openbox.stdout.log').open('w');wm=subprocess.Popen(['openbox'],stdout=wm_log,stderr=subprocess.STDOUT)
result={'architecture':'aarch64','fresh_profile':True,'native_window':'NOT OBSERVED','normal_close':'NOT RUN','lesson_workflow':'NOT RUN','app_functionality':'NOT ACCEPTED; actual screenshot review required','baseline':'LESSON_PREVIEW_ARM64','wm_readiness':[]}
try:
    deadline=time.monotonic()+25
    while time.monotonic()<deadline:
        if wm.poll() is not None:raise RuntimeError('Owned window manager exited '+str(wm.returncode))
        supporting=subprocess.run(['xprop','-root','_NET_SUPPORTING_WM_CHECK'],text=True,capture_output=True)
        clients=subprocess.run(['wmctrl','-lp'],text=True,capture_output=True)
        if supporting.returncode==0 and re.search(r'0x[1-9a-fA-F][0-9a-fA-F]*',supporting.stdout) and clients.returncode==0:
            result['wm_readiness'].append({'state':'READY','supporting_wm':supporting.stdout.strip()});break
        result['wm_readiness'].append({'wmctrl_exit':clients.returncode,'stderr':clients.stderr[:500]});time.sleep(.25)
    else:raise RuntimeError('Window manager EWMH/client-list readiness timed out25seconds')
    app=subprocess.Popen(['/usr/bin/9to1-preview'],env=env,stdout=log,stderr=subprocess.STDOUT)
    deadline=time.monotonic()+60;win=None
    while time.monotonic()<deadline:
        if app.poll() is not None:raise RuntimeError('Actual app exited before observed window: '+str(app.returncode))
        clients=subprocess.run(['wmctrl','-lp'],text=True,capture_output=True)
        if clients.returncode:time.sleep(.25);continue
        hits=[x.split()[0] for x in clients.stdout.splitlines() if len(x.split())>2 and x.split()[2]==str(app.pid)]
        if hits:win=hits[0];break
        time.sleep(.25)
    if not win:raise RuntimeError('No actual native app window in60seconds')
    result['native_window']='OBSERVED';result['window_id']=win
    result['window_title']=subprocess.check_output(['wmctrl','-l'],text=True)
    if 'startup failed' in result['window_title'].lower():raise RuntimeError('Bootstrap failure window, not native app readiness')
    time.sleep(3)
    if app.poll() is not None:raise RuntimeError('Actual app exited after window observation: '+str(app.returncode))
    subprocess.run(['import','-window','root',str(out/'fresh-arm64-window.png')],check=True)
    (out/'windows.txt').write_text(subprocess.check_output(['wmctrl','-lp'],text=True))
    subprocess.run(['wmctrl','-ic',win],check=True);code=app.wait(timeout=35);result['normal_exit_code']=code
    if code!=0:raise RuntimeError('Normal window close returned '+str(code))
    result['normal_close']='PASS';result['status']='PASS_NATIVE_WINDOW_AND_CLOSE_ONLY_NEEDS_SCREENSHOT_REVIEW'
    result['installed_build_provenance']=json.loads(pathlib.Path('/usr/share/doc/9to1-preview/source-and-build.json').read_text())
except BaseException as e:
    result['status']='FAIL';result['error']=repr(e)
    try:subprocess.run(['import','-window','root',str(out/'fresh-arm64-failure.png')],check=True)
    except Exception:pass
    if app is not None and app.poll() is None:
        app.terminate()
        try:app.wait(timeout=10)
        except subprocess.TimeoutExpired:app.kill();app.wait()
        result['failure_cleanup']='OWNED_PROCESS_TERMINATED; not normal-close proof'
finally:
    log.close();wm_log.close()
    if wm.poll() is None:wm.terminate();wm.wait(timeout=10)
    result['at']=datetime.datetime.now(datetime.timezone.utc).isoformat();(out/'native-startup-receipt.json').write_text(json.dumps(result,indent=2)+'\n')
sys.exit(0 if result.get('status')=='PASS_NATIVE_WINDOW_AND_CLOSE_ONLY_NEEDS_SCREENSHOT_REVIEW' else 1)
