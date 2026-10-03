"""Create explicit minimum-acceptance cases, keeping unexpanded clauses visible."""
import json
import re
from pathlib import Path

ROOT = Path('/workspace/team-b-evidence/sol-happy-20261003')
OUT = ROOT/'b2'
paragraphs = json.loads((ROOT/'source/paragraphs.json').read_text())
apps = {'Write':(426395,456068),'Present':(456068,488942),'Canvas':(488942,526502),
        'Boards':(526502,555689),'Data':(688672,734947),'Forms':(734947,771098)}

fixtures = {
 'Write':'A native document with nested Sections, custom text/table styles, text/image/table/review selections, digital/scanned/mixed PDFs and foreign DOCX/ODT references; preserve original bytes/revision.',
 'Present':'A native deck containing hidden slides, independent master/layout overrides, Unicode slide/notes text, two shared object types, a branching Actions graph, components, media, GLB, code and speaker notes; separate audience capture.',
 'Canvas':'Infinite and paged artifacts with pressure/tilt ink, multiple layers, overlapping strokes/objects, registered shared text/table/media/graph types, background and custom pens; a live Board embed plus explicit snapshot.',
 'Boards':'Two persisted Boards with nested Native/Write/Canvas/Chat/ScopedChat pages, absolute and flow blocks, free ink, H1–H6/custom styles, multi-expression Graph with sliders, source ingestion and independently owned referenced artifacts.',
 'Data':'A native project with formula workbook, XLSX/ODS fixtures, typed list/record/nested values, Product×Region×Month cube, keyed relational tables/junction, linked source and failed-refresh fixture; isolated external database with recoverable mutation protection.',
 'Forms':'A Draft and Published form with typed fields/TableInput, repeating child rows, deterministic+regex/math/graph/AI rubrics, historical responses, bidirectional Data schema, branching custom mode and server-hosted multiplayer/team/buzzer sessions.'
}

# These are domain properties dictated by the canonical clauses. No proposed
# JSON wire schema, guessed HTTP route or substitute provider is created.
extra = [
 (r'identity|identit|stable|same canonical', ['Compare owning artifact/page/object IDs before/after and through the second client; names/order/path changes do not replace IDs.']),
 (r'save|persist|reopen|restart|round.trip|retai', ['Save via the owning service; close, clear client state, reopen and compare meaningful content, typed properties, references and revision/history.']),
 (r'concurr|conflict|stale|last.writer', ['Create two clients at one base revision; retain independent edits and reject/surface incompatible same-entity edits; compare stored revisions and content, not only error labels.']),
 (r'AI.*[Rr]ead|Read.only.*AI|Read-only versus Write|typed mutation', ['Issue an AI mutation in Read-only and assert no canonical revision or content change; use Write only under host/ACL/review rules and verify owning typed action and audit identity.']),
 (r'View mode|Edit/View', ['Attempt UI/API/AI authoring in View; assert unchanged content/revision. View-safe interactions remain session-local until an explicit authorised save.']),
 (r'Locked|Unlocked|absolute coordinates', ['Change layout mode; compare saved object transforms to prove relocking does not silently move/reflow/remove objects; verify free ink and authoring permission remain independently controlled.']),
 (r'clipboard|copied|shared.object|private.*implement', ['Transfer populated structured content through the supported clipboard/embed path; inspect shared schema/style/assets/accessibility at both endpoints and reject a bitmap/private-model replacement.']),
 (r'permission|protected|authoris|access', ['Repeat under denied/read-only/revoked object/source access; assert no forbidden read/mutation/side effect and no leaked protected metadata; inspect backend ACL and audit.']),
 (r'crash|interrupt|recover|partial|failure', ['Interrupt only isolated fixture operation at a controlled boundary; preserve original/last durable revision and recoverable draft, report exact partial state, then retry without duplicated effects.']),
 (r'keyboard|screen.reader|accessib', ['Perform the criterion through keyboard only; inspect accessible roles/names/state, focus order/return, non-drag alternatives, 200% scaling and colour-independent status.']),
 (r'large|bounded|virtual|responsive|performance', ['Record populated fixture size, browser/platform/build, measured workload latency/memory and retained subscriptions after close; compare against declared baseline, which is currently OPEN.']),
 (r'donor|LibreOffice|Rnote|AppFlowy', ['Compare against selected legally usable donor revision and exhaustive manifest; retain feature/QoL/shortcut coverage or exact approved scoped exception, never infer parity from file open.']),
 (r'Graph|Actions|node|state machine', ['Inspect stable graph/node/port/target IDs and typed event/action effects; exercise valid branching plus invalid/cyclic/denied path, proving runtime budget/validation.']),
 (r'PDF|export|import|compatib', ['Use populated supported/unsupported format fixtures; compare semantic content/structure after export/import and require exact loss report/blocked operation for nonrepresentable data.']),
 (r'live|multiplayer|buzzer|timer|score|participant', ['Compare client views to authoritative session event sequence/timestamps; modify isolated client state and verify it cannot decide score/timing/elimination/reveal or duplicate participant identity.']),
 (r'variant|branch', ['Retain Main/base; create and edit named branch, reopen selected VariantID, compare source/base/target, preview overlapping conflict, use recoverable whole-version choice and reject stale merge.']),
]

cases = []
for app,(lo,hi) in apps.items():
 active=False
 serial=0
 for p in paragraphs:
  if not lo<=p['index']<hi:continue
  text=p['text'].strip()
  if text in ['Acceptance requirements','Release acceptance criteria']:
   active=True
   continue
  if active and p['heading']!='NORMAL_TEXT' and text:
   active=False
  if not active or not text:continue
  if text.startswith(('Write is implementation-complete','Present is release-conformant','Canvas is release-conformant','Boards is implementation-complete','Data is implementation-complete','Forms is implementation-complete','The release validation suite','Release conformance requires','Any acceptance','The release validation')):continue
  serial+=1
  expectations=[t.strip() for t in re.split(r';|(?<=[.!?])\s+(?=[A-Z])',text) if t.strip()]
  for pattern, assertions in extra:
   if re.search(pattern,text,re.I):expectations.extend(assertions)
  cases.append({
   'procedure_id':f'B2-AC-{app.upper()}-{serial:03}',
   'source_requirement_id':f'B2-{app}-P{p["index"]}',
   'source_start':p['index'], 'app':app,'canonical_acceptance_clause':text,
   'fixture':fixtures[app],
   'real_browser_ui':None,'domain_action_binding':'Owning app canonical UI/API action; concrete provider route not available/guessed.',
   'prerequisites':['Registered compatible B1 CUI/editor surface','Approved Team A domain model/runtime','Authorised Team C staging services','Supported browser matrix and fixture identity'],
   'steps':[
    'Capture exact tested deployment/commit/browser version and initial fixture IDs, revisions, permissions, semantic values and branch/session state.',
    'Perform each user operation named in the canonical acceptance clause through the actual owning browser surface; record trace and content-specific observations.',
    'Evaluate every expected assertion below against actual UI/domain state and independent backend/second-client readback.',
    'Capture visible error/read-only/conflict/failure state when required; preserve original fixture and rerun failed criterion after correcting its cause.'
   ],
   'expected_assertions':expectations,
   'decomposition_state':'PARTIAL_NEEDS_PER_CLAUSE_FIXTURE_ACTION_ASSERTION_REVIEW',
   'outcome':'NOT_RUN','observed':None,
   'coverage_warning':'Supplemental assertions concretise common guarantees; compound canonical clauses still require independent detailed assertion expansion. This file is not an executed acceptance suite.'
  })

(OUT/'acceptance-cases.json').write_text(json.dumps(cases,indent=2,ensure_ascii=False)+'\n')
requirements=json.loads((OUT/'requirements.json').read_text())
by_id={c['source_requirement_id']:c for c in cases}
for r in requirements:
 if r['requirement_id'] in by_id:
  c=by_id[r['requirement_id']]
  r['test_or_procedure_ids'].append(c['procedure_id'])
  r['decomposition_state']=c['decomposition_state']
(OUT/'requirements.json').write_text(json.dumps(requirements,indent=2,ensure_ascii=False)+'\n')
print(json.dumps({'acceptance_case_counts':{a:sum(c['app']==a for c in cases) for a in apps},'outcome':'ALL_NOT_RUN','decomposition':'PARTIAL'},indent=2))
