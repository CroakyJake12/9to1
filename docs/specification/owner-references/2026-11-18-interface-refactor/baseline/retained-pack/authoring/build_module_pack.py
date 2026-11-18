#!/usr/bin/env python3
"""Build an auditable v3 specification module pack from the supplied v2 source.

No network access. Original bytes are preserved. Every non-blank source block gets
a stable source ID, destination and explicit retention/amendment/history status.
"""
from __future__ import annotations
import hashlib, json, re, shutil, subprocess
from pathlib import Path
from collections import Counter, defaultdict

ROOT=Path(__file__).resolve().parents[1]
OUT=Path(__file__).resolve().parent
SOURCE=ROOT/'upload/9to1_Development_Specification_v2_Expanded_2026-10-27(1).md'
ANNEX=ROOT/'professional_annex.md'
COMPANION=OUT/'spaces_companion.md'
STUDY=OUT/'study_annex.md'
SCALABILITY=OUT/'scalability_annex.md'

# Inclusive source-line boundaries; these retain complete technical chapters.
RANGES=[
 (1,283,'authority'),(284,375,'platform'),(376,435,'privacy'),(436,547,'permissions'),
 (548,1167,'delivery'),(1168,1726,'cui'),(1727,1748,'platform'),(1749,1790,'shared-ai'),
 (1791,1856,'platform'),(1857,2083,'identity'),(2084,2143,'browse'),(2144,2243,'home'),
 (2244,2255,'notifications'),(2256,2337,'models'),(2338,2351,'dulche'),(2352,2359,'permissions'),
 (2360,2377,'mesh'),(2378,2417,'shared-ai'),(2418,2499,'productivity-engine'),(2500,2511,'settings'),
 (2512,2520,'identity'),(2521,2532,'notifications'),(2533,2544,'go-search'),(2545,2674,'home'),
 (2675,2706,'node-editor'),(2707,2755,'admin'),(2756,3146,'files'),(3147,3556,'connect'),
 (3557,3687,'games'),(3688,4201,'spaces'),(4202,4587,'vision-voice'),(4588,5007,'ai-studio'),
 (5008,5254,'play'),(5255,5296,'persistence'),(5297,5756,'write'),(5757,6157,'present'),
 (6158,6554,'canvas'),(6555,7260,'boards'),(7261,7427,'planner'),(7428,7633,'automations'),
 (7634,7830,'picture'),(7831,8035,'wave'),(8036,8211,'motion'),(8212,8499,'data'),
 (8500,8727,'forms'),(8728,9042,'build-code'),(9043,9682,'build-stacks'),(9683,10003,'build-sites'),
 (10004,10233,'terminal'),(10234,10521,'mini-computer'),(10522,10617,'browse'),
 (10618,10708,'maps'),(10709,10751,'launcher'),(10752,10790,'shelf'),
 (10791,10814,'dulche'),(10815,10982,'dulche-translate'),(10983,11247,'assistants'),
 (11248,11362,'dulche'),(11363,11565,'subagents'),(11566,11639,'models'),
 (11640,11864,'dulche'),(11865,11982,'den-memory'),(11983,12032,'generative-ui'),
 (12033,12067,'den-memory'),(12068,12234,'plugins-mcp'),(12235,12306,'integrations'),
 (12307,12438,'os'),(12439,12471,'go-search'),(12472,12480,'models'),(12481,12541,'os'),
 (12542,12594,'delivery'),(12595,12650,'files'),(12651,12669,'link'),(12670,12684,'prototype'),
 (12685,12698,'paths'),(12699,12712,'replay'),(12713,12726,'variants'),(12727,12754,'platform'),
 (12755,12921,'cloud-services'),(12922,12949,'delivery'),(12950,12959,'home'),
 (12960,13051,'spaces'),(13052,13057,'models'),(13058,13163,'spaces'),(13164,13239,'cards'),
 (13240,13246,'planner'),(13247,13263,'platform'),(13264,13267,'delivery'),
 (13268,13319,'dulche'),(13320,13333,'delivery'),(13334,13344,'assistants'),
 (13345,13392,'authority'),(13393,13398,'spaces'),(13399,13512,'platform'),
 (13513,13631,'delivery'),(13632,15235,'acceptance'),
]
PREFIX_MODULE={
 'PRESENT':'present','WRITE':'write','CUI':'cui','APP-VIS':'cui','ADM':'admin','LAUNCH':'platform',
 'DEN-PRIV':'den-memory','TF-F':'files','TF-L':'link','TF-P':'prototype','TF-H':'paths','TF-R':'replay',
 'TF-V':'variants','TF-X':'platform','SH':'cloud-services',
 'UX27-AUTH':'authority','UX27-HOST':'browse','UX27-SHARED':'cui','UX27-BOARD':'boards',
 'UX27-SPACES':'spaces','UX27-AUTO':'automations','UX27-BUILD':'build-code','UX27-WRITE':'write',
 'UX27-PRESENT':'present','UX27-CANVAS':'canvas','UX27-DATA':'data','UX27-FILES':'files',
}
OTHER={1:'planner',2:'planner',3:'connect',4:'connect',5:'forms',6:'cards',7:'picture',8:'wave',9:'motion',10:'maps',11:'shelf',12:'terminal',13:'ai-studio',14:'integrations',15:'games',16:'play',17:'mesh'}
SCENARIO_CHAPTER={'Boards':'boards','Shared UI':'cui','Host':'browse','Spaces':'spaces','Automations':'automations','Build':'build-code','Write':'write','Present':'present','Canvas':'canvas','Data':'data','Files':'files','Other apps':'acceptance','Integration':'platform'}
CLAUSE_RE=re.compile(r'\b(?:TF-[A-Z]\d{2}|[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-\d{2,3})\b')

OVERRIDES=[
 {'id':'V3-OWNER-001','title':'Visual authority and implementation scope','modules':['authority','cui','spaces','present'],'rule':'Latest owner directions and this v3 module pack control. Presentation slides 1–16 are authoritative. Later slides are historical inspiration; the older Study concept remains an approved rough layout/behaviour reference with the current wrapper. Mock-ups define design intent, not product-verification status.'},
 {'id':'V3-OWNER-002','title':'Global Assistants and Specialists','modules':['assistants','home','spaces','shared-ai'],'rule':'Assistants and Specialists are global systems accessible from Home and editors. Editor collaborator avatars/pickers expose them in the current artifact context. Spaces retains their conversation views. Persistent identity, Main threads, memory, tools, permissions and durable Task/Runs are shared canonical entities.'},
 {'id':'V3-OWNER-003','title':'Notifications owns ecosystem activity','modules':['notifications','home','settings'],'rule':'Remove Events as a user-facing navigation destination. Notifications owns notification history and typed ecosystem activity. Existing EventID, provenance, audit and deep-link contracts remain valid backend contracts and are not deleted.'},
 {'id':'V3-OWNER-004','title':'New Tab icon rail','modules':['browse','home'],'rule':'New Tab uses the compact icon rail shown in the current mock-up. Apps, Dashboard, Models and New Tab remain discoverable semantic destinations. This changes navigation presentation, not the canonical Home services or tab identities.'},
 {'id':'V3-OWNER-005','title':'Eight current themes and specified material treatments','modules':['cui','settings'],'rule':'The current themes are Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, plus Create Theme. Cinematic is legacy compatibility. Bubble uses watery dimensional surfaces and wobble with reduced-motion alternatives; Western uses wood/sepia treatment; Retro uses a monospace treatment. The Professional annex remains normative. Layout topology and authored content do not change with theme.'},
 {'id':'V3-OWNER-006','title':'Spaces conversation permissions','modules':['spaces','permissions','shared-ai','settings'],'rule':'Spaces uses an icon labelled Conversation permissions. It controls current-conversation context/action scope and cannot widen durable grants. It is distinct from editor AI Read-only/Write and from document Edit/Suggest/View.'},
 {'id':'V3-OWNER-007','title':'Model discovery alongside local management','modules':['models','home','settings'],'rule':'Models is a discovery and comparison experience with local installed-model management available as a distinct useful view. Provider identity, capabilities, hardware fit and unknown metadata remain truthful.'},
 {'id':'V3-OWNER-008','title':'Spaces generative output and execution visibility','modules':['spaces','generative-ui','subagents','shared-ai'],'rule':'Provide typed interactive Generative UI, visible subagent progress/detail, high-level thinking/work summaries, live semantic work in Present and Canvas, and durable generated-file output cards. Open resolves the canonical artifact; Download appears only when verified durable bytes exist. Failed export preserves saved native artifacts. Never expose private internal chain-of-thought.'},
 {'id':'V3-OWNER-009','title':'Copyable app modules','modules':['authority'],'rule':'Each app module is organised as Prerequisites, Overview, Layouts & visible states, Features & behaviour, Canonical entities & persistence, Permissions & recovery, Acceptance checks and Source clause IDs. Modules include the exact current mock-up mapping and named retained technical subchunks.'},
 {'id':'V3-OWNER-010','title':'Consistent Home identity','modules':['cui','platform','home'],'rule':'Use the house Home icon consistently in app/host navigation; preserve semantic Home targets and current app-specific identity.'},
 {'id':'V3-OWNER-011','title':'Boards meaningful editing states','modules':['boards'],'rule':'Represent page Add/type, page options, insertion-between-blocks, typing/caret, selected blocks, dragging and ink/tool options. Preserve local native editing without a required Files account and independent Edit/View, Locked and Snap semantics.'},
 {'id':'V3-OWNER-012','title':'Write document layouts','modules':['write'],'rule':'Show Continuous and actual paginated Pages/Book layouts of the same canonical document with correct selection and contextual controls; changing view never creates a duplicate document.'},
 {'id':'V3-OWNER-013','title':'Present Actions and custom shapes','modules':['present'],'rule':'Provide explicit meaningful Actions and custom-shape creation/editing states within the approved three-tier Present shell, preserving canonical object/action semantics, notes and manipulation.'},
 {'id':'V3-OWNER-014','title':'Files selection and content padding','modules':['files'],'rule':'Provide comfortable content and selected-row padding so icons, names and metadata do not collide with selection backgrounds or table edges; preserve real file-management density and operations.'},
 {'id':'V3-OWNER-015','title':'Planner event nib','modules':['planner'],'rule':'Planner event cards use a clear colour nib and readable interior padding, while event identity, time/recurrence and state remain authoritative.'},
 {'id':'V3-OWNER-016','title':'Cards neighbouring offsets','modules':['cards'],'rule':'Focused Cards show visibly offset neighbouring cards with clear front/back and Edit versus View semantics; vertical stack is the default and supported horizontal orientation remains available.'},
 {'id':'V3-OWNER-017','title':'Native emoji','modules':['cui','spaces'],'rule':'Emoji characters use native platform emoji rendering. Do not substitute text-glyph boxes or custom application icons for emoji; keep coherent app/control icon assets separate.'},
 {'id':'V3-RECON-001','title':'Confirmed discrete effort mapping supersedes ambiguous percentage mapping','modules':['models','dulche'],'rule':'SPUI-133 explicitly supersedes application of the older arbitrary-percentage floor formula to the visible effort presets. Visible choices map bijectively to actual provider LevelIDs/ordinals. Any retained arbitrary-percent legacy API must be separately versioned and cannot shift or duplicate the displayed tier choices.'},
 {'id':'V3-RECON-002','title':'Expired delivery schedules and worker assignments','modules':['authority','delivery','assistants'],'rule':'Historical dated schedules and named worker/session assignments are provenance, not current product requirements or execution authorisation. Compatible product/runtime, packaging, platform, security and acceptance requirements remain active; original process wording is retained in the history ledger.'},
]

def module_for_line(n):
    if 231<=n<=264:return 'assistants'
    for a,b,m in RANGES:
        if a<=n<=b:return m
    return 'authority'

def split_source(text,source_key):
    lines=text.splitlines()
    blocks=[];current=[];start=1;scenario_module='acceptance';other_module=None;companion_module='spaces'
    boundaries=({a for a,b,m in RANGES}|{b+1 for a,b,m in RANGES}|set(range(231,266))) if source_key=='v2' else set()
    for n,line in enumerate(lines+[''],1):
        if not line.strip() or (current and n in boundaries):
            if current:
                raw='\n'.join(current);first=next((x.strip() for x in current if x.strip()),'')
                line_start=start;line_end=n-1
                mod=module_for_line(line_start) if source_key=='v2' else 'cui'
                if source_key=='companion':
                    sm=re.match(r'SPUI-(\d{3})',first)
                    if sm:
                        no=int(sm[1]);companion_module='models' if 133<=no<=146 else 'assistants' if 154<=no<=163 else 'dulche' if no==194 else 'privacy' if 195<=no<=198 else 'acceptance' if no==199 else 'permissions' if no==200 else 'spaces'
                    mod=companion_module
                if source_key=='study':mod='spaces'
                if source_key=='scalability':
                    mod='files' if first.startswith('SCALE-009') else 'platform'
                if source_key=='v2' and line_start>=13632:
                    title=re.match(r'^##\s+(.+)$',first)
                    if title:scenario_module=SCENARIO_CHAPTER.get(title[1],scenario_module)
                    mod=scenario_module
                if source_key=='v2' and 13399<=line_start<=13512:
                    mm=re.search(r'UX27-OTHER-(\d{3})',first)
                    if mm:other_module=OTHER.get(int(mm[1]),'platform')
                    mod=other_module or mod
                    if mod=='play' and first.startswith('Experiences'):mod='spaces'
                refs=list(dict.fromkeys(CLAUSE_RE.findall(raw)))
                # Only the defining prefix, never a cross-reference in the paragraph.
                clean=re.sub(r'^#+\s*','',first)
                defid=CLAUSE_RE.match(clean)
                if defid:
                    code=defid[0]
                    for prefix,target in sorted(PREFIX_MODULE.items(),key=lambda x:-len(x[0])):
                        if code.startswith(prefix+'-') or (prefix.startswith('TF-') and code.startswith(prefix)):mod=target;break
                if source_key=='annex':
                    if clean.startswith(('PRESENT-','### Present','## Present')):mod='present'
                    if clean.startswith('APP-VIS-'):mod='cui'
                blocks.append({'id':f'{source_key.upper()}-B{len(blocks)+1:04d}','source':source_key,'line_start':line_start,'line_end':line_end,'module':mod,'original_text':raw,'active_text':raw,'source_refs':refs,'sha256':hashlib.sha256(raw.encode()).hexdigest(),'status':'retained','amendments':[]})
                current=[]
            if line.strip():start=n;current.append(line)
        else:
            if not current:start=n
            current.append(line)
    return blocks

def amend_block(block):
    t=block['active_text'];changes=[]
    def replace(old,new,rule):
        nonlocal t
        if old in t:
            count=t.count(old);t=t.replace(old,new);changes.append({'rule':rule,'original':old,'replacement':new,'occurrences':count})
    def regex_replace(pattern,new,rule):
        nonlocal t
        matches=re.findall(pattern,t)
        if matches:
            t,n=re.subn(pattern,new,t);changes.append({'rule':rule,'original_pattern':pattern,'replacement':new,'occurrences':n})
    replace('Assistants remains a Space rather than a replacement standalone app.','Assistants and Specialists are global systems available through Home and editors; Spaces retains their canonical conversation views.','V3-OWNER-002')
    replace('Assistants as a Space rather than a mandatory standalone app','the current global Assistants/Specialists system, with canonical conversation views retained in Spaces','V3-OWNER-002')
    replace('standalone Assistants/Specialists experiences','global Assistants/Specialists experiences','V3-OWNER-002')
    replace('standalone Assistants','global Assistants','V3-OWNER-002')
    regex_replace(r'\bAssistants app\b','Assistants system','V3-OWNER-002')
    replace('Assistants remains the specified Space.','Assistants is the global system available through Home and editors, with its canonical conversation views retained in Spaces.','V3-OWNER-002')
    replace('Assistants is a standalone 9to1 application built as a thin, purpose-built product wrapper over the shared Spaces and agentic runtime. It is NOT a Space inside the Spaces app, MUST NOT be represented as a specially tagged Space, and MUST NOT require the user to enter the normal Spaces app or create/manage a Space in order to create, open or manage an Assistant. Opening Assistants launches the dedicated Assistants product experience; opening an Assistant opens that Assistant\'s own conversation/work surface.', 'Assistants and Specialists are global systems built over the shared conversation and agentic runtime. Home and editors provide direct access without requiring a user to create or enter a Space. Spaces retains canonical Assistant conversation views. Opening an Assistant preserves its own identity and conversation/work state. The current scope does not mandate a separate standalone Assistants package.', 'V3-OWNER-002')
    replace('product ownership, navigation, Assistant creation/configuration, identity, proactive controls and Assistant-specific management belong to the global Assistants system.', 'product ownership, identity, proactive controls and Assistant-specific management belong to the global Assistants system exposed through Home and editors, with conversation views also available in Spaces.', 'V3-OWNER-002')
    replace('The actual global Assistants application and its end-to-end user workflows must exist.', 'The actual global Assistants system and its end-to-end user workflows must exist in Home, editors and canonical conversation views.', 'V3-OWNER-002')
    replace('Product definition and standalone-app boundary','Product definition and global-system boundary','V3-OWNER-002')
    replace('CONFIRMED & SUPERSEDING EARLIER V2: ASSISTANTS is now its own Space within Spaces, with its icon in the rail; not a separately packaged mandatory standalone Assistants application.', 'V3 OWNER REVISION: ASSISTANTS and SPECIALISTS are global systems available through Home and editors. Spaces retains their canonical conversation views; no separately packaged mandatory Assistants application is implied.', 'V3-OWNER-002')
    replace('CONFIRMED & SUPERSEDING EARLIER V2: ASSISTANTS is now its own Space within Spaces, with its icon in the rail; not a separately packaged mandatory global Assistants application.', 'V3 OWNER REVISION: ASSISTANTS and SPECIALISTS are global systems available through Home and editors. Spaces retains their canonical conversation views; no separately packaged mandatory Assistants application is implied.', 'V3-OWNER-002')
    replace('Assistants as Space with complete persistent functionality','global Assistants with Spaces conversation views and complete persistent functionality','V3-OWNER-002')
    replace('Assistants remains a protected first-party SPACE on the far-left rail, not a mandatory standalone app.', 'Assistants and Specialists remain global systems available through Home and editors. Spaces retains protected first-party Assistant conversation views on its rail; this placement is not their only access point.', 'V3-OWNER-002')
    replace('Home Events/Notifications','Home Notifications activity','V3-OWNER-003')
    replace('Events, Agents, Spaces and other surfaces MAY deep-link','Notifications, Agents, Spaces and other surfaces MAY deep-link','V3-OWNER-003')
    replace('Planner is not Home Events, which is an ecosystem activity/history timeline','Planner is not Home Notifications activity, which is an ecosystem activity/history timeline','V3-OWNER-003')
    replace('Planner, Events, Spaces','Planner, Notifications, Spaces','V3-OWNER-003')
    replace('Planner/Events can attach','Planner/Notifications activity can attach','V3-OWNER-003')
    replace('Planner, Events, Boards','Planner, Notifications, Boards','V3-OWNER-003')
    replace('Library, Events, Mesh, Settings','Library, Notifications, Mesh, Settings','V3-OWNER-003')
    replace('AI layout changes MUST be attributable in Events/audit history','AI layout changes MUST be attributable in Notifications activity/audit history','V3-OWNER-003')
    if block['module']=='notifications':
        replace('Events is Home’s ecosystem activity/timeline surface.','Notifications includes Home’s ecosystem activity/timeline surface.','V3-OWNER-003')
        replace('Events MUST','Activity history MUST','V3-OWNER-003')
        replace('Events MAY','Activity history MAY','V3-OWNER-003')
        if t.strip()=='Events':replace('Events','Notifications — activity history','V3-OWNER-003')
    old_roster='Built-in Theme names:\n* Professional.\n* Glow.\n* Playful.\n* Bubble.\n* Cinematic.'
    new_roster='Current built-in Theme names:\n* Professional.\n* Glow.\n* Playful.\n* Frosted Glass.\n* Bubble.\n* Metallic.\n* Retro.\n* Western.\nCinematic is retained only as explicit legacy compatibility. Create Theme supports compatible custom definitions.'
    replace(old_roster,new_roster,'V3-OWNER-005')
    replace('Test all five built-in Themes and a custom theme','Test all eight current built-in Themes, Cinematic compatibility and a custom theme','V3-OWNER-005')
    replace('all five built-in CUI Themes (Professional, Glow, Playful, Bubble and Cinematic)','all eight current built-in CUI Themes (Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western), explicit Cinematic compatibility','V3-OWNER-005')
    replace('built-in/custom inherited Themes including Professional, Glow, Playful, Bubble and Cinematic','built-in/custom inherited Themes including Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, with Cinematic legacy compatibility','V3-OWNER-005')
    replace('NOT a sixth built-in theme','NOT an additional built-in theme','V3-OWNER-005')
    replace('(Professional, Glow, Playful, Bubble, Cinematic or an available compatible custom theme)','(Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro, Western or an available compatible custom theme; Cinematic remains an explicit legacy choice)','V3-OWNER-005')
    replace('Reasoning percentages map to ordered supported levels: for n levels use min(n-1, floor(p*n/100)) for p in 0..100; reject out-of-range percentages. With five levels, 0-<20 selects the first, 20-<40 the second, and 80-100 the fifth.', 'Legacy arbitrary-percentage compatibility only: the older API describes min(n-1, floor(p*n/100)) for arbitrary p in 0..100. SPUI-133 supersedes applying that formula to visible discrete effort presets: bind each displayed preset directly and bijectively to the actual supported provider LevelID/ordinal. Any arbitrary-percent legacy API compatibility must be separately versioned; it must not shift the first visible preset to the second tier or duplicate selectable tiers.', 'V3-RECON-001')
    replace('Target completion: 17–18 October 2026, Europe/London.', 'Historical delivery target (17–18 October 2026, Europe/London): archived; not a current deadline.', 'V3-RECON-002')
    replace('Target: finish 9to1 this weekend, 17–18 October (Europe/London).', 'Historical delivery target (17–18 October, Europe/London): archived; not a current deadline.', 'V3-RECON-002')
    if block['source']=='v2' and block['line_start']<=283:
        regex_replace(r'Current default worker split:[^\n]+', 'Historical named worker/PR-path assignment is archived in the original source ledger. Implementation must use current explicit ownership and preserve existing work; this paragraph does not assign present workers, sessions or credentials.', 'V3-RECON-002')
        regex_replace(r'Delivery target:[^\n]+', 'Historical 6 October 2026 delivery-schedule paragraph is archived in the original source ledger. The adjacent substantive native/web, real-login and acceptance requirements remain active; no expired deadline or new live-deployment authority is imposed.', 'V3-RECON-002')
    if block['source']=='v2':
        replace('New Tab shares that sidebar and adds a New Tab view.','New Tab exposes those destinations through a compact icon rail and adds its New Tab view.','V3-OWNER-004')
        replace('New Tab shares them and adds a New Tab view.','New Tab exposes them through a compact icon rail and adds a New Tab view.','V3-OWNER-004')
        replace('New Tab shares those views plus its New Tab canvas.','New Tab exposes those destinations through a compact icon rail alongside its New Tab canvas.','V3-OWNER-004')
    if block['source']=='companion' and (t.lstrip().startswith(('PROPOSED UI (NOT YET APPROVED):','PROPOSED (NOT LOCKED):','OPEN DECISION REGISTER','OPEN VISUAL/UX DETAILS:','HANDOFF POLICY')) or t.lstrip().startswith('A. RESOLVED (13 OCT):')):
        block['status']='open-or-proposed';block['history_reason']='Companion explicitly marks this visual option or decision register proposed/open. It is retained for provenance and does not override the current owner-approved mock-ups or manufacture a missing product decision.'
    # Process history is preserved separately, never promoted to a current deadline.
    if block['module']=='delivery' and (re.search(r'\b(?:deadline|Target completion|16–18 October|17–18 October|hard deadline)\b',t,re.I) or 548<=block['line_start']<=1167):
        block['status']='historical-process'
        block['history_reason']='Retained delivery/process provenance. Historical schedules and worker-operation instructions are not new product behaviour or current execution authorisation.'
    if block['source']=='v2' and 13320<=block['line_start']<=13333:
        block['status']='deferred-scope';block['history_reason']='Source explicitly marks future Android-ROM/Training Lab work outside current release scope; preserved without inventing its undefined contract.'
    if block['source']=='v2' and block['line_start'] in (231,232,233,234,235,258,259,260,262):
        block['status']='historical-process';block['history_reason']='Historical implementation priority, worker/active-session or packaging directive. Current global Assistant workflows remain required through the adjacent retained product clauses; the old separate-package/deadline instruction is superseded.'
    if re.fullmatch(r'(?:<!--.*?-->\s*)+',t,re.S):block['status']='structural-marker'
    if changes:
        block['active_text']=t;block['amendments']=changes
        if block['status']=='retained':block['status']='amended'
    return block

def get_screen_metadata():
    # Evaluate declarative registrations only; never load the browser viewer.
    js=r'''const fs=require('fs'),vm=require('vm');const p=process.argv[1];const build=fs.readFileSync(p+'/build.mjs','utf8');const mm=build.match(/const modules=\[([^\]]+)\]/);let names=mm?[...mm[1].matchAll(/'([^']+)'/g)].map(m=>m[1]):[];const extras=fs.readdirSync(p+'/src').filter(n=>n.endsWith('.js')&&!names.includes(n));names=[...names.filter(n=>n!=='viewer.js'),...extras.filter(n=>n!=='viewer.js')];if(!names.includes('base.js'))names.unshift('base.js');names=names.filter(n=>fs.existsSync(p+'/src/'+n));const c={ICONS:{},console:{log(){}}};vm.createContext(c);vm.runInContext(names.map(n=>fs.readFileSync(p+'/src/'+n,'utf8')).join('\n')+'\nglobalThis.meta=SCREENS.map(({render,...x})=>x);',c);process.stdout.write(JSON.stringify(c.meta));'''
    try:
        result=subprocess.run(['node','-e',js,str(ROOT/'mockups')],capture_output=True,text=True,check=True)
        return json.loads(result.stdout)
    except Exception as e:
        fallback=ROOT/'mockups/manifest.json'
        if fallback.exists():return json.loads(fallback.read_text())
        raise RuntimeError('Unable to read mock-up metadata: '+str(e))

def screen_module(s):
    i=s['id'];a=s.get('app','')
    if i.startswith('settings-'):return 'settings'
    if i=='appearance':return 'settings'
    if i in ('custom-theme','components','theme-gallery','themes'):return 'cui'
    if i.startswith(('theme-','component-')):return 'cui'
    if i=='collaboration-review' or i.startswith(('home-assistant','home-specialist','collaborators-picker')):return 'assistants'
    if i.startswith('host-notif'):return 'notifications'
    if i.startswith(('host-model','home-model')):return 'models'
    if i.startswith('home-mesh'):return 'mesh'
    if i.startswith(('host-','newtab-')):return 'browse'
    if i.startswith('home-'):return 'home'
    if i in ('account','onboarding'):return 'identity'
    if i.startswith(('spaces-','study-')):return 'spaces'
    if i.startswith(('build-sites','sites-')):return 'build-sites'
    if i.startswith(('build-stacks','stacks-')):return 'build-stacks'
    if i.startswith(('build-code','build-')):return 'build-code'
    if i.startswith('ai-studio'):return 'ai-studio'
    if i=='variants' or i.startswith('variants-'):return 'variants'
    if i.startswith('os-'):return 'os'
    if i.startswith('launcher'):return 'launcher'
    if i.startswith(('vision-','voice-')):return 'vision-voice'
    if i.startswith('mobile-'):
        for token,mod in [('permissions','spaces'),('chat','spaces'),('space','spaces'),('study','spaces'),('board','boards'),('write','write'),('present','present'),('canvas','canvas'),('mail','connect')]:
            if token in i:return mod
        return 'launcher'
    appmap={'Write':'write','Present':'present','Boards':'boards','Canvas':'canvas','Data':'data','Files':'files','Forms':'forms','Planner':'planner','Automations':'automations','Picture':'picture','Wave':'wave','Motion':'motion','Build':'build-code','AI Studio':'ai-studio','Terminal':'terminal','Connect':'connect','Mail':'connect','Maps':'maps','Shelf':'shelf','Play':'play','Games':'games','Mini Computer':'mini-computer','Link':'link','Prototype':'prototype','Paths':'paths','Replay':'replay','Cards':'cards','Admin':'admin','Launcher':'launcher','9to1OS':'os','Spaces':'spaces','Home':'home','Browse':'browse','Vision & Voice':'vision-voice','Voice & Vision':'vision-voice'}
    return appmap.get(a,'platform')

def headings_catalog(text,source):
    lines=text.splitlines();out=[]
    for ix,line in enumerate(lines):
        s=line.strip()
        if not s:continue
        is_md=bool(re.match(r'^#{1,6}\s',s));is_clause=bool(CLAUSE_RE.match(re.sub(r'^#+\s*','',s)))
        is_plain=(len(s)<100 and not re.search(r'[.;:]$',s) and not s.startswith(('*','-','<','|','http','9to1.')) and (ix==0 or not lines[ix-1].strip()) and (ix+1==len(lines) or not lines[ix+1].strip()))
        if is_md or is_clause or is_plain:out.append({'source':source,'line':ix+1,'type':'clause' if is_clause else 'markdown' if is_md else 'heading-candidate','text':s[:220]})
    return out

def chunk_subjects(chunk, module_title):
    """Describe chunks with their actual leading/dominant source headings."""
    out=[]
    generic={'Overview','Acceptance','Acceptance checklist','Expected outcome','Verification','Specification','Default layout','Identity','9to1 Development Specification','Shared','Purpose','Purpose and required outcome','Journey','Required evidence'}
    for b in chunk:
        for line in b['active_text'].splitlines():
            s=line.strip();clean=re.sub(r'^#{1,6}\s*','',s)
            clause=CLAUSE_RE.match(clean)
            if clause:
                clean=clean[clause.end():].lstrip(' —–:-')
                if not clean:continue
                # Clause titles frequently include a short leading label before full prose.
                clean=re.split(r'(?<=[.!?])\s+',clean)[0].rstrip('.:')
            elif not (s.startswith('#') or (3<=len(clean)<=95 and 1<=len(clean.split())<=12 and not re.search(r'[.;:]$',clean) and not clean.startswith(('*','-','•','<','|','http','9to1.','return ','public ','enum ','class ','//')))):
                continue
            clean=re.sub(r'^\d+(?:\.\d+)*\.?\s+(?:[—–-]\s*)?','',clean).strip()
            if not clean or clean in generic or clean.startswith('Pass ') or len(clean)>125 or re.search(r'[{}]|MUST|SHOULD|\[\]|\(\);',clean):continue
            if clean not in out:out.append(clean)
    if not out:
        txt='\n'.join(b['active_text'] for b in chunk).lower()
        tags=[('permissions and approvals',r'permission|approval|authoris'),('canonical entities and persistence',r'persistence|canonical|revision|storage'),('runtime and recovery',r'runtime|recovery|retry|failure'),('editor layout and interaction',r'layout|toolbar|sidebar|selection'),('API and integration contracts',r'api|integration|interface'),('acceptance scenarios',r'acceptance|verify|scenario')]
        out=[x for x,_ in sorted(tags,key=lambda x:len(re.findall(x[1],txt)),reverse=True)[:3]]
    return out[:10]

def validate_dependencies(modules):
    state={};order=[]
    def visit(mid,path):
        if state.get(mid)==1:raise RuntimeError('Prerequisite cycle: '+' → '.join(path+[mid]))
        if state.get(mid)==2:return
        state[mid]=1
        for d in modules[mid].get('prerequisites',[]):
            if d not in modules:raise RuntimeError('Unknown prerequisite '+d+' in '+mid)
            visit(d,path+[mid])
        state[mid]=2;order.append(mid)
    for mid in modules:visit(mid,[])
    return order

def build():
    from module_content import MODULES, COMMON_GROUPS, GROUP_LABELS
    for d in ['source','modules','technical','history','catalog']: (OUT/d).mkdir(parents=True,exist_ok=True)
    shutil.copyfile(SOURCE,OUT/'source/original_v2.md')
    shutil.copyfile(ANNEX,OUT/'source/professional_annex.md')
    companion=COMPANION.read_text(encoding='utf-8-sig') if COMPANION.exists() else ''
    if companion:shutil.copyfile(COMPANION,OUT/'source/spaces_companion.md')
    study=STUDY.read_text(encoding='utf-8-sig') if STUDY.exists() else ''
    scalability=SCALABILITY.read_text(encoding='utf-8-sig') if SCALABILITY.exists() else ''
    if study:shutil.copyfile(STUDY,OUT/'source/study_annex.md')
    if scalability:shutil.copyfile(SCALABILITY,OUT/'source/scalability_annex.md')
    v2=SOURCE.read_text(encoding='utf-8-sig');annex=ANNEX.read_text()
    dependency_order=validate_dependencies(MODULES)
    blocks=[amend_block(b) for b in split_source(v2,'v2')+split_source(annex,'annex')+split_source(companion,'companion')+split_source(study,'study')+split_source(scalability,'scalability')]
    # Exact repeated paragraphs within a module are references to the first copy.
    seen={}
    for b in blocks:
        if len(b['active_text'])<300 or b['status'] not in ('retained','amended'):continue
        key=(b['module'],hashlib.sha256(b['active_text'].encode()).hexdigest())
        if key in seen:b['duplicate_of']=seen[key];b['status']='duplicate-reference'
        else:seen[key]=b['id']
    metadata=get_screen_metadata()
    by_module=defaultdict(list)
    for b in blocks:by_module[b['module']].append(b)
    for mod in by_module:
        if mod not in MODULES:raise RuntimeError('Missing module definition '+mod)
    modules=[]
    for order,(mid,m) in enumerate(MODULES.items(),1):
        src=by_module[mid]
        active=[b for b in src if b['status'] in ('retained','amended','duplicate-reference')]
        screens=[s for s in metadata if screen_module(s)==mid]
        reqs={k:list(v) for k,v in m.get('requirements',{}).items()}
        for k,v in COMMON_GROUPS.items():reqs.setdefault(k,list(v))
        refs=list(dict.fromkeys([r for b in active for r in b['source_refs']]+m.get('source_refs',[])))
        owner=[o['id'] for o in OVERRIDES if mid in o['modules']]
        parts=[];part=[];words=0
        for b in active:
            w=len(b['active_text'].split())
            if part and words+w>2400:parts.append(part);part=[];words=0
            part.append(b);words+=w
        if part:parts.append(part)
        technical=[]
        for j,chunk in enumerate(parts,1):
            f=OUT/'technical'/f'{mid}-{j:02d}.md'
            subjects=chunk_subjects(chunk,m['title'])
            short='; '.join(subjects[:2])
            if len(short)>130:short=subjects[0][:125]
            title=f'{m["title"]} — {short} (part {j:02d})'
            lines=['# '+title,'','Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.','']
            for b in chunk:
                lines += [f'### {b["id"]} · {b["source"]} lines {b["line_start"]}–{b["line_end"]}','']
                if b.get('duplicate_of'):lines+=['Exact duplicate of '+b['duplicate_of']+'. The repeated text is omitted here; the source ledger retains both locations.',''];continue
                if b['amendments']:lines+=['V3 editorial amendment: '+', '.join(sorted({a['rule'] for a in b['amendments']}))+'. Original wording is preserved in the replacement ledger.','']
                lines += [b['active_text'],'']
            f.write_text('\n'.join(lines))
            technical.append({'id':f'{mid}-{j:02d}','title':title,'subjects':subjects,'load_for':'Load this chunk for '+', '.join(subjects[:5])+'.','path':str(f.relative_to(OUT)),'source_block_ids':[b['id'] for b in chunk],'words':sum(len(b['active_text'].split()) for b in chunk if not b.get('duplicate_of'))})
        primary=['# '+m['title'],'',f'Module ID: `{mid}` · 9to1 Development Specification v3','', '## Prerequisites','']
        for dep in m.get('prerequisites',[]):
            d=MODULES.get(dep,{'title':dep,'contract':''});primary+=['- **'+d['title']+'** — '+d.get('contract','Use the canonical contract described by that module.')]
        if not m.get('prerequisites'):primary+=['This module establishes the shared foundation and has no prerequisite product UI.']
        primary+=['','## Overview','',m['overview'],'']
        if owner:primary+=['Current owner overrides: '+', '.join(owner)+'.','']
        for key,title in GROUP_LABELS.items():
            primary += ['## '+title,'']
            for n,r in enumerate(reqs.get(key,[]),1):primary += [f'- **V3-{mid.upper()}-{key[:3].upper()}-{n:02d}** — {r}']
            primary+=['']
        primary+=['## Exact mock-up and state map','']
        if screens:
            for s in screens:primary += [f'- **{s["title"]}** — screen `{s["id"]}`. '+s.get('tag','')+'.']
        else:primary+=['This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.']
        primary+=['','## Source clause IDs','',', '.join(refs) if refs else 'Named baseline section(s) retained in the technical-source chapters below.','', '## Technical reading map','']
        for ch in technical:primary+=['- '+ch['title']+f' — `{ch["path"]}` ({ch["words"]:,} words; '+', '.join(ch['source_block_ids'][:3])+('…' if len(ch['source_block_ids'])>3 else '')+').']
        primary+=['','Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.','']
        primary_text='\n'.join(primary)
        module_path=OUT/'modules'/f'{mid}.md';module_path.write_text(primary_text)
        # One explicitly copyable file also includes all active technical subchunks.
        full_path=OUT/'modules'/f'{mid}.complete.md'
        full_path.write_text(primary_text+'\n\n# Retained technical requirements\n\n'+'\n\n'.join((OUT/x['path']).read_text() for x in technical))
        item={'id':mid,'title':m['title'],'kind':m.get('kind','app'),'order':order,'prerequisites':m.get('prerequisites',[]),'prerequisite_contracts':[{'id':d,'title':MODULES[d]['title'],'contract':MODULES[d].get('contract','')} for d in m.get('prerequisites',[]) if d in MODULES],'overview':m['overview'],'requirements':reqs,'source_refs':refs,'owner_overrides':owner,'sourcefile':str(module_path.relative_to(OUT)),'complete_sourcefile':str(full_path.relative_to(OUT)),'technical_chunks':technical,'source_block_ids':[b['id'] for b in src],'screen_ids':[s['id'] for s in screens],'screens':screens,'primary_markdown':primary_text,'retained_technical_words':sum(c['words'] for c in technical)}
        modules.append(item)
    history=[b for b in blocks if b['status'] in ('historical-process','deferred-scope','open-or-proposed') or b['amendments']]
    hist=['# Historical and superseded source ledger','','This file preserves original text for provenance. Replaced wording and historical operation instructions do not override current module requirements.','']
    for b in history:
        hist+=['## '+b['id']+f' · {b["module"]} · {b["source"]} lines {b["line_start"]}–{b["line_end"]}','',b.get('history_reason','Explicit wording replaced by '+', '.join(sorted({a['rule'] for a in b['amendments']}))) if b['amendments'] or b.get('history_reason') else '', '',b['original_text'],'']
    (OUT/'history/superseded-and-process.md').write_text('\n'.join(hist))
    catalog=headings_catalog(v2,'v2')+headings_catalog(annex,'annex')+headings_catalog(companion,'companion')+headings_catalog(study,'study')+headings_catalog(scalability,'scalability')
    ledger=[]
    for b in blocks:
        x={k:v for k,v in b.items() if k not in ('original_text','active_text')};x['original_words']=len(b['original_text'].split());x['active_words']=len(b['active_text'].split());ledger.append(x)
    report={'source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'source_words':len(v2.split()),'source_lines':len(v2.splitlines()),'annex_words':len(annex.split()),'companion_words':len(companion.split()),'companion_retrieved':bool(companion),'blocks_total':len(blocks),'blocks_by_status':dict(Counter(b['status'] for b in blocks)),'modules':len(modules),'primary_words':sum(len(m['primary_markdown'].split()) for m in modules),'technical_chunks':sum(len(m['technical_chunks']) for m in modules),'screens':len(metadata),'mapped_screens':sum(len(m['screens']) for m in modules),'unique_screen_ids':len(set(s['id'] for s in metadata)),'amended_blocks':sum(bool(b['amendments']) for b in blocks),'replacement_occurrences':sum(a['occurrences'] for b in blocks for a in b['amendments']),'unassigned_blocks':0,'original_bytes_preserved':True,'prerequisite_cycles':0,'dependency_order':dependency_order,'unretrieved_incorporated_references':['Future-chat evergreen handoff link: provenance only; not incorporated as an unseen active product clause.'],'limits':['Coverage measures source accounting and retention, not independent semantic rewriting of every source word or product acceptance.','Later undiscovered incompatible clauses require precise adjudication; the owner override ledger takes precedence only within its declared scope.','Original UI diagrams/ASCII card sketches remain source provenance where current mock-ups supersede their presentation.','No production status, backend test pass, deployment or real account action is asserted by this pack.']}
    report.update({'study_words':len(study.split()),'study_retrieved':bool(study),'scalability_words':len(scalability.split()),'scalability_retrieved':bool(scalability),'all_source_words':sum(len(t.split()) for t in [v2,annex,companion,study,scalability]),'authored_contract_words':sum(len(m['overview'].split())+sum(len(r.split()) for rr in m['requirements'].values() for r in rr) for m in modules)})
    source_defs=[('v2','source/original_v2.md',None,v2),('annex','source/professional_annex.md','https://docs.google.com/document/d/12iLllJXQl_YdGM1J-MTbZuAyrhZN-oTpvdedRfo47nY/edit',annex),('companion','source/spaces_companion.md','https://docs.google.com/document/d/12AbDnWynZjVbKZ5XhkkiGMmJkINOSqMfnPuHuI6XDBE/edit',companion),('study','source/study_annex.md','https://docs.google.com/document/d/17Q_KpQjNgjEAtdVq9HwsnMYveFn6zfIbZBl3es5bptg/edit',study),('scalability','source/scalability_annex.md','https://docs.google.com/document/d/1B7XG1fuW7auRuzXMRZQeuq2dp59pqrS0jHckg9B-ol4/edit',scalability)]
    source_manifest=[{'id':key,'path':path,'url':url,'words':len(txt.split()),'lines':len(txt.splitlines()),'sha256':hashlib.sha256((OUT/path).read_bytes()).hexdigest(),'acquisition':'Supplied attached file, original bytes preserved' if key=='v2' else 'Incorporated document read through connected Google Drive; text preserved','retrieved_date':'2026-10-28'} for key,path,url,txt in source_defs if txt]
    for filename,data in [('modules.json',modules),('owner_overrides.json',OVERRIDES),('catalog/source_manifest.json',source_manifest),('catalog/source_ledger.json',ledger),('catalog/heading_catalog.json',catalog),('catalog/source_blocks.json',blocks),('catalog/screen_mapping.json',[{'screen_id':s['id'],'module':screen_module(s),'title':s['title']} for s in metadata]),('coverage_report.json',report)]:
        (OUT/filename).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    top=sorted(modules,key=lambda m:m['retained_technical_words'],reverse=True)[:12]
    (OUT/'README.md').write_text('# 9to1 Development Specification v3 — module source pack\n\n'+
       'Use `modules.json` for Word authoring. Each object contains the requested section groups, prerequisite contracts, exact screen IDs, full screen notes, primary Markdown and retained technical-chunk paths. `modules/<id>.complete.md` is the copyable single-app unit including its retained backend contracts.\n\n'+
       'The original v2, normative Professional annex, incorporated Spaces companion, Study UI annex and Scalable Content annex are preserved under `source/`; exact source URLs/digests are in `catalog/source_manifest.json`. Every source block is accounted for in `catalog/source_ledger.json`; exact text and amended text are in `catalog/source_blocks.json`. Owner overrides and replaced original wording are separate from active content. No original file was modified.\n\n'+
       '## Coverage\n\n'+f'- Original source: {report["source_words"]:,} words; {report["source_lines"]:,} lines.\n- All five sources: {report["all_source_words"]:,} words.\n- Current authored contract: {report["authored_contract_words"]:,} words, excluding references, prerequisites, screen/technical reading maps.\n- Accounted blocks: {report["blocks_total"]:,}; unassigned: 0.\n- Modules: {len(modules)}; technical subchunks: {report["technical_chunks"]}.\n- Screens mapped: {report["mapped_screens"]}/{report["screens"]}.\n- Explicitly amended blocks: {report["amended_blocks"]}.\n\n'+
       '## Largest retained technical modules\n\n'+'\n'.join(f'- {m["title"]}: {m["retained_technical_words"]:,} words in {len(m["technical_chunks"])} subchunks.' for m in top)+'\n\n## Refresh\n\nRun `python spec-revision/build_module_pack.py` after the final mock-up registrations are available. This refreshes screen metadata and maps without editing any mock-up file.\n')
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':build()
