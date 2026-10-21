"""Actual installed-package UI journey. Standard accessibility + xdotool; no application hooks/database writes."""
from pathlib import Path
import os,subprocess,time,json,datetime,hashlib,sqlite3,sys,traceback
import re
HEAD=os.environ['EXPECTED_SOURCE_HEAD'];assert re.fullmatch(r'[0-9a-f]{40}',HEAD);PROMPT='What is 2 + 2? Answer briefly and explain your calculation.'
OUT=Path(os.environ['GITHUB_WORKSPACE'])/'evidence';PROFILE=Path(os.environ['RUNNER_TEMP'])/'9to1-chat-package-fresh';assert os.geteuid()!=0;assert not PROFILE.exists();assert os.uname().machine=='aarch64';OUT.mkdir(exist_ok=True)
receipt={'started_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_head':HEAD,'prompt':PROMPT,'normal_user_uid':os.geteuid(),'fresh_profile_initially_absent':True,'desktop':'Xvfb1280x900+openbox','installed_package':'NOT VERIFIED','startup_window_and_close':'NOT RUN','actual_UI_chat':'NOT RUN','streaming':'NOT OBSERVED','persistence_database':'NOT RUN','visual_reopen':'NOT RUN','owner_laptop':'NOT RUN','independent_screenshot_review':'REQUIRED','stages':[]};app=None;wm=None;logs=[]
def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def save(): (OUT/'package-ui-journey-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
def event(kind,**kw):receipt['stages'].append({'at':now(),'kind':kind,**kw});save()
def command(args,check=True):return subprocess.run(args,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE,check=check,timeout=8)
def shot(name):
 p=OUT/(name+'.png');command(['import','-window','root',str(p)]);return str(p)
def xkey(k):command(['xdotool','key','--clearmodifiers',k])
def type_text(t):command(['xdotool','type','--clearmodifiers','--delay','35',t])
def wait_wm():
 end=time.monotonic()+25
 while time.monotonic()<end:
  if wm.poll() is not None:raise RuntimeError('Owned window manager exited '+str(wm.returncode))
  prop=command(['xprop','-root','_NET_SUPPORTING_WM_CHECK'],False);clients=command(['wmctrl','-lp'],False)
  if prop.returncode==0 and re.search(r'0x[1-9a-fA-F][0-9a-fA-F]*',prop.stdout) and clients.returncode==0:event('EWMH_READY',supporting_wm_check=prop.stdout.strip());return
  event('WM_READINESS_WAIT',xprop_exit=prop.returncode,wmctrl_exit=clients.returncode,wmctrl_stderr=clients.stderr[:500]);time.sleep(.3)
 raise RuntimeError('Window manager did not expose EWMH/client-list within25seconds')
def wait_window():
 end=time.monotonic()+60
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError('Actual app exited before window: '+str(app.returncode))
  rows=command(['wmctrl','-lp'],False)
  if rows.returncode:
   event('CLIENT_LIST_TRANSIENT',stderr=rows.stderr[:500]);time.sleep(.3);continue
  for line in rows.stdout.splitlines():
   fields=line.split()
   if len(fields)>2 and fields[2]==str(app.pid):return fields[0]
  time.sleep(.3)
 raise RuntimeError('No actual app window in60seconds')
def launch(label):
 global app
 f=(OUT/(label+'.stdout.log')).open('w');logs.append(f);env=dict(os.environ,HAVEN_DATA_DIR=str(PROFILE),NO_AT_BRIDGE='0');env.pop('GH_TOKEN',None);env.pop('GITHUB_TOKEN',None)
 app=subprocess.Popen(['/usr/bin/9to1-preview'],env=env,stdout=f,stderr=subprocess.STDOUT);win=wait_window();command(['wmctrl','-ir',win,'-b','add,maximized_vert,maximized_horz']);command(['wmctrl','-ia',win]);event('APP_WINDOW',label=label,pid=app.pid,window=win);return win

def normal_close(win,label):
 command(['wmctrl','-ic',win]);code=app.wait(timeout=35);event('NORMAL_CLOSE',label=label,exit_code=code)
 if code!=0:raise RuntimeError('Actual normal close returned '+str(code))

def nodes():
 try:
  r=command(['/usr/bin/python3',str(Path(os.environ['EXACT_OBSERVER_DIRECTORY'])/'desktop-observe.py')],check=False)
  data=json.loads(r.stdout) if r.returncode==0 else {'rows':[],'error':r.stderr[:1000]}
 except Exception as e:data={'rows':[],'error':repr(e)}
 return [(n,n.get('name',''),n.get('role',''),n.get('text','')) for n in data.get('rows',[])]


def dump(label):
 rows=nodes();(OUT/(label+'.accessibility.json')).write_text(json.dumps([{'name':n,'role':r,'text':t} for _,n,r,t in rows],indent=2)+'\n');return rows

def find(names,timeout=35):
 end=time.monotonic()+timeout
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError('App exited while waiting for '+str(names))
  for node,name,role,text in nodes():
   if name.casefold() in [x.casefold() for x in names]:
    try:
     if node.get('showing') and node.get('enabled'):return node
    except Exception:pass
  time.sleep(.5)
 dump('missing-control');raise LookupError('Actual enabled accessible control unavailable: '+str(names))

def click(names,timeout=35):
 n=find(names,timeout);rect=n.get('rectangle')
 if not rect or rect[2]<=0 or rect[3]<=0:raise LookupError('Actual control has no visible extent '+str(names))
 command(['xdotool','mousemove',str(rect[0]+rect[2]//2),str(rect[1]+rect[3]//2),'click','1']);event('ACTUAL_UI_CLICK',control=n['name'],rectangle=rect);return n

def wait_home(label):
 end=time.monotonic()+180;attempt=0
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError('App exited before actual Home '+str(app.returncode))
  attempt+=1;rows=nodes();names={name.casefold() for n,name,_,_ in rows if n.get('showing')}
  has_apps={'search apps','open chat','open spaces','open files','open write'}.issubset(names)
  if 'search or enter web address' in names and (has_apps or 'home dashboard' in names or {'edit dashboard page','open home discover model catalogue'}.issubset(names)):
   event('ACTUAL_HOME_READY',method='AT-SPI',surface='Home Apps' if has_apps else 'Home Dashboard',Dashboard_acceptance='NOT CLAIMED',label=label);shot(label+'-actual-home');return
  p=shot(label+'-waiting-'+str(attempt))
  try:
   r=command(['tesseract',p,'stdout','--psm','11'],check=False);text=r.stdout.lower();(OUT/(label+'-waiting-'+str(attempt)+'.ocr.txt')).write_text(r.stdout)
   if (all(x in text for x in ['welcome','edit page','discover models']) or all(x in text for x in ['apps','24 apps','select an app','automations','spaces','write'])) and 'checking required home services' not in text and 'could not start' not in text:
    event('ACTUAL_HOME_READY',method='Visible screenshot OCR; review required',label=label);return
  except Exception as e:event('OCR_OBSERVATION_UNAVAILABLE',error=repr(e))
  time.sleep(2)
 raise RuntimeError('Actual Home NOT OBSERVED within180seconds; Loading window is not readiness')


def db_messages():
 p=PROFILE/'haven.db'
 if not p.is_file():return []
 try:
  with sqlite3.connect(p.as_uri()+'?mode=ro',uri=True,timeout=1) as c:
   c.execute('PRAGMA query_only=ON');c.row_factory=sqlite3.Row
   return [dict(r) for r in c.execute('SELECT id,conversation_id,role,content,model_name,created_at FROM messages WHERE conversation_id=(SELECT conversation_id FROM messages WHERE role=1 AND content=? ORDER BY rowid DESC LIMIT 1) ORDER BY rowid',(PROMPT,))]
 except sqlite3.Error as e:event('READ_ONLY_PERSISTENCE_OBSERVATION_UNAVAILABLE',error=str(e));return []
try:
 installed=json.loads(Path('/usr/share/doc/9to1-preview/source-and-build.json').read_text());assert installed['source_head']==HEAD and installed['build_kind']=='LESSON_PREVIEW_ARM64';receipt['installed_package']='SOURCE_PROVENANCE_MATCH';receipt['installed_build_provenance']=installed
 wm_log=(OUT/'openbox.stderr.log').open('w');logs.append(wm_log);wm=subprocess.Popen(['openbox'],stdout=wm_log,stderr=subprocess.STDOUT);wait_wm()
 receipt['startup_window_and_close']='INDEPENDENT_FRESH_HOME_CHECK_IN_SEPARATE_RECEIPT; prior failure retained'
 w=launch('actual-chat-launch');wait_home('02-home');shot('02-home-before-navigation');xkey('ctrl+l');type_text('9to1://spaces');xkey('Return');time.sleep(4);shot('03-spaces');dump('03-spaces')
 click(['Open Chat']);time.sleep(2);shot('03-chat-selected');click(['Start a new chat in this Space']);time.sleep(2);click(['Message']);type_text(PROMPT);shot('04-real-prompt-before-send');dump('04-real-prompt-before-send');click(['Send message'],timeout=180)
 end=time.monotonic()+180;assistant=None;observations=[];ui_growth=[]
 while time.monotonic()<end:
  if app.poll() is not None:raise RuntimeError('App exited during actual response')
  rows=db_messages();answers=[r for r in rows if r['role']==2 and r['content'].strip()]
  ui='\n'.join(n+'\n'+t for _,n,_,t in nodes());ui_growth.append({'at':now(),'text_sha256':hashlib.sha256(ui.encode()).hexdigest(),'characters':len(ui)})
  if answers:
   assistant=answers[-1];observations.append({'at':now(),'message_id':assistant['id'],'content':assistant['content'],'model_name':assistant['model_name']})
   if len(observations)>=3 and observations[-1]['content']==observations[-3]['content']:break
  if len(ui_growth)%3==0:shot('05-response-sample-'+str(len(ui_growth)))
  time.sleep(2)
 receipt['response_observations']=observations;receipt['UI_text_timeline']=ui_growth
 if assistant is None:raise RuntimeError('No actual persisted assistant response within180seconds; no synthetic answer substituted')
 if not assistant['model_name']:raise RuntimeError('Response lacks real recorded model provenance')
 receipt['actual_UI_chat']='USER_PROMPT_AND_ASSISTANT_PERSISTED_NEEDS_SCREENSHOT_REVIEW';receipt['assistant']=assistant;shot('06-response-complete');dump('06-response-complete')
 variants={x['content'] for x in observations}
 if len(variants)>1:receipt['streaming']='MULTIPLE_PERSISTED_CONTENT_STATES_OBSERVED_NEEDS_UI_REVIEW'
 normal_close(w,'after-response');persisted=db_messages();assert any(r['id']==assistant['id'] and r['content']==assistant['content'] for r in persisted);receipt['persistence_database']='SAME_ASSISTANT_ID_CONTENT_AFTER_NORMAL_CLOSE';(OUT/'actual-prompt-and-response.json').write_text(json.dumps(persisted,indent=2)+'\n')
 w=launch('reopen-same-created-profile');time.sleep(5);shot('07-reopened-same-profile');rows=dump('07-reopened-same-profile');visible='\n'.join(n+'\n'+t for _,n,_,t in rows)
 receipt['visual_reopen']='PROMPT_AND_RESPONSE_VISIBLE' if PROMPT in visible and assistant['content'].strip()[:80] in visible else 'NOT VERIFIED; screenshot review or real recent-chat selection required'
 normal_close(w,'after-reopen');receipt['status']='CHECKPOINT_ACTUAL_UI_JOURNEY_REQUIRES_INDEPENDENT_SCREENSHOT_REVIEW';receipt['full_app_acceptance']='NOT ACCEPTED'
except LookupError as e:
 receipt['status']='BLOCKED_ACTUAL_UI_CONTROL_UNAVAILABLE';receipt['error']=repr(e);shot('blocked-ui-controls')
except BaseException as e:
 receipt['status']='FAIL';receipt['error']=repr(e);receipt['traceback']=traceback.format_exc()
 try:shot('failure-screen')
 except Exception:pass
finally:
 if app is not None and app.poll() is None:
  try:
   wins=command(['wmctrl','-lp'],False).stdout.splitlines()
   for line in wins:
    fields=line.split()
    if len(fields)>2 and fields[2]==str(app.pid):command(['wmctrl','-ic',fields[0]],False)
   app.wait(timeout=15)
  except Exception:
   app.terminate()
   try:app.wait(timeout=10)
   except subprocess.TimeoutExpired:app.kill();app.wait()
   receipt['owned_app_failure_cleanup']='SIGTERM_OR_SIGKILL; not normal-close acceptance'
 if wm is not None and wm.poll() is None:wm.terminate();wm.wait(timeout=10)
 for f in logs:f.close()
 receipt['ended_at']=now();receipt['raw_profile_database_uploaded']=False;save()
sys.exit(0 if receipt.get('status')=='CHECKPOINT_ACTUAL_UI_JOURNEY_REQUIRES_INDEPENDENT_SCREENSHOT_REVIEW' else 1)
