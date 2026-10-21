"""Installed native package only: separate immediate-close and real Home-ready/close observations."""
from pathlib import Path
import subprocess,os,time,json,datetime,sys,re,traceback
OUT=Path('evidence');OUT.mkdir(exist_ok=True);assert os.geteuid()!=0;assert os.uname().machine=='aarch64';HERE=Path(os.environ['EXACT_OBSERVER_DIRECTORY'])
result={'started_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'build_kind':'LESSON_PREVIEW_ARM64','early_close':'NOT RUN','early_loading_visible':'NOT OBSERVED','actual_Home_readiness':'NOT OBSERVED','normal_Home_close':'NOT RUN','window_alone_is_ready':False,'independent_screenshot_review':'REQUIRED','owner_laptop':'NOT RUN','events':[]};app=None;wm=None;logs=[]
def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def save():(OUT/'fresh-home-diagnostic-receipt.json').write_text(json.dumps(result,indent=2)+'\n')
def event(kind,**data):result['events'].append({'at':now(),'kind':kind,**data});save()
def cmd(args,timeout=10):return subprocess.run(args,capture_output=True,text=True,timeout=timeout)
def screenshot(name):
 p=OUT/(name+'.png');r=cmd(['import','-window','root',str(p)]);event('SCREENSHOT',path=str(p),exit_code=r.returncode);return p

def observe(name):
 try:
  r=cmd(['/usr/bin/python3',str(HERE/'desktop-observe.py')],6);data=json.loads(r.stdout) if r.returncode==0 else {'rows':[],'error':r.stderr[:1000]}
 except Exception as e:data={'rows':[],'error':repr(e)}
 (OUT/(name+'.accessibility.json')).write_text(json.dumps(data,indent=2)+'\n');return data

def ocr(p):
 try:
  r=cmd(['tesseract',str(p),'stdout','--psm','11'],8);(OUT/(p.stem+'.ocr.txt')).write_text(r.stdout);return r.stdout.lower()
 except Exception as e:event('OCR_UNAVAILABLE',error=repr(e));return ''

def wait_wm():
 end=time.monotonic()+25
 while time.monotonic()<end:
  if wm.poll() is not None:raise RuntimeError('Window manager exited '+str(wm.returncode))
  p=cmd(['xprop','-root','_NET_SUPPORTING_WM_CHECK']);c=cmd(['wmctrl','-lp'])
  if p.returncode==0 and re.search(r'0x[1-9a-fA-F][0-9a-fA-F]*',p.stdout) and c.returncode==0:event('EWMH_READY',actual=p.stdout.strip());return
  event('WM_WAIT',wmctrl_exit=c.returncode,stderr=c.stderr[:500]);time.sleep(.25)
 raise RuntimeError('No EWMH/client-list readiness in25seconds')

def launch(label):
 global app
 profile=Path(os.environ['RUNNER_TEMP'])/('9to1-'+label+'-fresh');assert not profile.exists();log=(OUT/(label+'.stdout.log')).open('w');logs.append(log)
 env=dict(os.environ,HAVEN_DATA_DIR=str(profile),NO_AT_BRIDGE='0');env.pop('GH_TOKEN',None);env.pop('GITHUB_TOKEN',None)
 app=subprocess.Popen(['/usr/bin/9to1-preview'],env=env,stdout=log,stderr=subprocess.STDOUT);end=time.monotonic()+60
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError(label+' app exited before actual window: '+str(app.returncode))
  r=cmd(['wmctrl','-lp'])
  if r.returncode==0:
   for line in r.stdout.splitlines():
    x=line.split()
    if len(x)>2 and x[2]==str(app.pid):
     win=x[0];cmd(['wmctrl','-ir',win,'-b','add,maximized_vert,maximized_horz']);event('ACTUAL_WINDOW',label=label,pid=app.pid,window=win);return win
  time.sleep(.2)
 raise RuntimeError(label+' no real window within60seconds')

def close(win,label):
 r=cmd(['wmctrl','-ic',win]);event('NORMAL_CLOSE_REQUEST',label=label,wmctrl_exit=r.returncode);code=app.wait(timeout=35);event('NORMAL_CLOSE_RESULT',label=label,exit_code=code)
 if code!=0:raise RuntimeError(label+' normal close exited '+str(code))

def home_ready():
 end=time.monotonic()+180;attempt=0
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError('App exited while waiting for actual Home: '+str(app.returncode))
  attempt+=1;data=observe('home-readiness-'+str(attempt));names={r['name'].casefold() for r in data.get('rows',[]) if r.get('showing')};has_address='search or enter web address' in names;has_dashboard='home dashboard' in names or {'edit dashboard page','open home discover model catalogue'}.issubset(names);has_apps={'search apps','open chat','open spaces','open files','open write'}.issubset(names);has_home=has_dashboard or has_apps
  if has_address and has_home:event('ACTUAL_HOME_UI_OBSERVED',method='AT-SPI',surface='Home Apps' if has_apps else 'Home Dashboard',Dashboard_acceptance='NOT CLAIMED',attempt=attempt);screenshot('actual-home-ready');return
  p=screenshot('home-readiness-'+str(attempt));text=ocr(p)
  # Actual visible dashboard content, not title/loading/timeout substitution. Root still reviews image.
  if (all(x in text for x in ['welcome','edit page','discover models']) or all(x in text for x in ['apps','24 apps','select an app','automations','spaces','write'])) and 'could not start' not in text and 'checking required home services' not in text:
   event('ACTUAL_HOME_UI_OBSERVED',method='Actual screenshot OCR: Home Dashboard or exact observed Home Apps+24apps+typed tiles',Dashboard_acceptance='NOT CLAIMED',attempt=attempt);return
  event('HOME_NOT_READY',attempt=attempt,address_observed=has_address,home_observed=has_home,loading_text='checking required home services' in text);time.sleep(2)
 raise RuntimeError('Actual Home controls/content NOT OBSERVED within180seconds; Loading window is not readiness')
try:
 provenance=json.loads(Path('/usr/share/doc/9to1-preview/source-and-build.json').read_text());assert provenance['source_head']==os.environ['EXPECTED_SOURCE_HEAD'];result['installed_build_provenance']=provenance
 f=(OUT/'openbox.stdout.log').open('w');logs.append(f);wm=subprocess.Popen(['openbox'],stdout=f,stderr=subprocess.STDOUT);wait_wm()
 result['early_close']='PRIOR_RUN_FAIL_RETAINED_SEPARATELY; NOT REPEATED'
 w=launch('home-ready');home_ready();result['actual_Home_readiness']='ACTUAL_HOME_CONTENT_OBSERVED_REQUIRES_SCREENSHOT_REVIEW';close(w,'actual-home');result['normal_Home_close']='PASS';result['status']='PASS_BOUNDED_STARTUP_AND_HOME_CLOSE_CHECKPOINT_REQUIRES_SCREENSHOT_REVIEW'
except BaseException as e:
 result['status']='FAIL';result['error']=repr(e);result['traceback']=traceback.format_exc()
 try:screenshot('fresh-home-diagnostic-failure');observe('fresh-home-diagnostic-failure')
 except Exception:pass
finally:
 if app is not None and app.poll() is None:
  app.terminate()
  try:app.wait(timeout=10)
  except subprocess.TimeoutExpired:app.kill();app.wait()
  result['failure_cleanup']='OWNED_APP_TERMINATED; not normal-close acceptance'
 if wm is not None and wm.poll() is None:wm.terminate();wm.wait(timeout=10)
 for f in logs:f.close()
 result['ended_at']=now();save()
sys.exit(0 if result.get('status','').startswith('PASS_BOUNDED_') else 1)
