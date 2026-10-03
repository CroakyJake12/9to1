"""Build conservative paragraph coverage from the fetched canonical revision.

This is an evidence generator, not product code or an acceptance runner.
"""
import collections
import hashlib
import json
import re
from pathlib import Path

ROOT = Path('/workspace/team-b-evidence/sol-happy-20261003')
OUT = ROOT / 'b2'
REPO = Path('/workspace/team-b-worktree')
SOURCE = json.loads((ROOT / 'source/paragraphs.json').read_text())
MANIFEST = json.loads((ROOT / 'source/manifest.json').read_text())
APPS = {
    'Write': (426395, 456068), 'Present': (456068, 488942),
    'Canvas': (488942, 526502), 'Boards': (526502, 555689),
    'Data': (688672, 734947), 'Forms': (734947, 771098),
}
COMMON = [(1, 57079), (102889, 114171), (178434, 184824),
          (1238694, 1253145), (1299027, 1315675), (1315675, 1342106)]
LOCATIONS = {
    'Write': ['9to1 Workspace/shared/src/Haven.Application/Write', '9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Write'],
    'Present': ['9to1 Workspace/shared/src/Haven.Application/Present', '9to1 Workspace/shared/src/Haven.Core/Present'],
    'Canvas': ['9to1 Workspace/shared/src/Haven.Application/Canvas', '9to1 Workspace/Canvas'],
    'Boards': ['9to1 Workspace/Boards/contract', '9to1 Workspace/Boards/hui', '9to1 Workspace/Boards/app'],
    'Data': ['9to1 Workspace/Data/App', '9to1 Workspace/Data/Cui', '9to1 Workspace/Data/workers'],
    'Forms': ['9to1 Workspace/shared/src/Haven.Application/Forms', '9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Forms'],
}
TEST_FILES = {
    'Write': ['WriteSelectionFormattingTests.cs', 'WriteWordProcessorTests.cs', 'WriteTableEditingTests.cs', 'WriteDocxFidelityTests.cs', 'WriteReviewedAiAndPageBreakTests.cs', 'WriteNativeDocumentPackageStoreTests.cs'],
    'Present': ['PresentProductionTests.cs', 'PresentAdvancedAuthoringTests.cs', 'PresentStructuredObjectTests.cs'],
    'Canvas': ['CanvasArtifactCodecTests.cs', 'CanvasAppSurfaceTests.cs', 'CanvasWorkspacePersistenceTests.cs', 'CanvasProductionTests.cs', 'CanvasBoardLifecycleTests.cs'],
    'Boards': ['HavenBoardContractTests.cs', 'HavenRichBoardV3Tests.cs', 'HavenBoardCollaborationContractTests.cs', 'HavenBoardGroupCardCrudTests.cs'],
    'Data': ['Program.cs', 'RuntimeProgram.cs', 'test_workers.py', 'test_calc_formula_corpus.py', 'test_calc_structure.py', 'test_calc_filter.py', 'test_calc_sort.py', 'test_duckdb_publish.py'],
    'Forms': ['FormsSubmissionStoreTests.cs', 'FormsStorageRuntimeProgram.cs', 'MapsFormsDonorTests.cs'],
}
GROUPS = [
 ('variants', r'variant|branch|merge preview', 'Variants panel; explicit base/source/target and branch-qualified editor', 'Owning app Variants capability; Team A contract not located'),
 ('security', r'permission|trust|privacy|secret|authori[sz]|protected|access policy|risk|sandbox', 'Account/object access, permission prompt, share/revocation and denial surfaces', 'Home/CAKE ID broker and owning object ACL; Team C service required'),
 ('recovery', r'recover|crash|interrupted|autosave|save failure|partial|idempoten|rollback|backup', 'Save/sync/error state; close/reopen/history/recovery', 'Owning app repository plus Files revisions; real hosted persistence required'),
 ('collaboration', r'collaborat|concurren|conflict|realtime|presence|reconnect|offline', 'Two authenticated clients editing same IDs; presence and conflict resolution', 'Owning revision actions and Team C collaboration service'),
 ('ai', r'dulche|\bAI\b|semantic context|agent|scoped chat', 'Persistent AI bar/read-write controls and app-specific protections', 'Shared contextual AI coordinator; authorised owning-app actions'),
 ('interop', r'cross.app|clipboard|copy/paste|embed|canonical reference|source reference|source provenance|shared object|shared productivity', 'Copy/paste/live embed/open original and source permission states', 'Home Shared Productivity Engine; source owning namespace; Files canonical identity'),
 ('formats', r'import|export|format|\.9to1|pptx|docx|odt|ods|xlsx|pdf|schema.version', 'Open/import/export compatibility reports and populated round trips', 'Owning codec plus donor/backend engine; explicit capability and compatibility report'),
 ('accessibility', r'accessib|keyboard|screen.reader|focus|zoom|narrow|touch|scal|reduced.motion|contrast', 'Keyboard, accessibility tree, focus, narrow/touch, 200% scaling and themes', 'CUI component runtime and owning editor input/selection semantics'),
 ('performance', r'performan|large|virtuali|cach|cull|incremental|resource|bounded|paging|pag[eination]|stream', 'Populated workload, editing/scrolling/open-close performance and cleanup', 'Owning engine and CUI renderer; declared browser/runtime baseline OPEN'),
 ('donor', r'donor|upstream|licen|LibreOffice|Rnote|AppFlowy', 'Selected donor user-visible feature and QoL workflow inventory', 'Pinned donor source and exhaustive parity manifest; no approved browser exception'),
 ('api', r'9to1\.|API|typed.*action|event|job|namespace|structured.*error', 'Repeat each UI action through the canonical domain API', 'Specified canonical namespace; located symbols are evidence only, not proved exhaustive'),
 ('authoring', r'.', 'Owning app editor, palette/toolbar/inspector and semantic selection', 'Existing owning domain engines; production browser renderer/host not located'),
]

def groups(text):
    matches = [g for g in GROUPS[:-1] if re.search(g[1], text, re.I)]
    return matches or [GROUPS[-1]]

def requirement_level(text):
    if re.search(r'\b(MUST|SHALL|REQUIRED)\b', text, re.I): return 'MANDATORY'
    if re.search(r'\b(SHOULD|RECOMMENDED)\b', text, re.I): return 'SHOULD_DEFAULT_REQUIRED'
    if re.search(r'\b(MAY|OPTIONAL)\b', text, re.I): return 'OPTIONAL_OR_CONDITIONAL_REVIEW'
    return 'CONTRACT_PROSE_OR_SCHEMA_REQUIRES_REVIEW'

test_inventory = {}
for app, names in TEST_FILES.items():
    files = [p for p in REPO.rglob('*') if p.is_file() and p.name in names and '/Source/' not in str(p) and '/vendor/' not in str(p)]
    if app == 'Data': files = [p for p in files if '/Data/Tests/' in str(p)]
    entries = []
    for path in files:
        content = path.read_text(errors='replace')
        symbols = re.findall(r'public\s+(?:async\s+)?(?:void|Task|ValueTask)\s+(\w+)\s*\(', content)
        entries.append({'file': str(path.relative_to(REPO)), 'candidate_test_symbols': symbols,
                        'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                        'outcome': 'NOT_RUN', 'scope': 'native/domain regression; not browser acceptance'})
    test_inventory[app] = entries

requirements = []
shared = []
heading = 'General Rules'
for paragraph in SOURCE:
    index, text = paragraph['index'], paragraph['text'].strip()
    app = next((a for a, (lo, hi) in APPS.items() if lo <= index < hi), None)
    common = any(lo <= index < hi for lo, hi in COMMON)
    if not app and not common: continue
    if not text: continue
    if paragraph['heading'] != 'NORMAL_TEXT':
        heading = text
        continue
    if text in ('{', '}', 'These are minimum canonical entry points.'): continue
    rid = f'B2-{app or "COMMON"}-P{index}'
    matched = groups(text)
    entry = {
        'requirement_id': rid, 'source_revision': MANIFEST['revisionId'],
        'source_document': MANIFEST['documentId'], 'source_start': index,
        'source_end': paragraph['end'], 'section': heading,
        'canonical_text': text, 'normative_level': requirement_level(text),
        'platform': 'browser/web', 'app': app or 'shared',
        'requirement_state': 'BLOCKED', 'test_outcome': 'NOT_RUN',
        'production_browser_ui_observed': False,
        'specified_ui_surfaces': [g[2] for g in matched],
        'domain_action_owner': [g[3] for g in matched],
        'test_or_procedure_ids': [f'BP-{app or "ALL"}-{g[0].upper()}' for g in matched] + [f'BP-SPEC-P{index}'],
        'expected_assertions': [text],
        'blocking_dependencies': ['B1 browser/CUI app host and compatible shared editor renderer',
                                  'Team A approved shared schemas/domain contracts',
                                  'Team C authenticated production-equivalent non-production services',
                                  'Authorised deployed build and declared browser/platform matrix'],
        'exception': None,
    }
    # Native bootstrap is precisely scoped by canonical app rules, not a browser exemption.
    if re.search(r'When Home is absent at process start', text):
        entry['applicability_note'] = ('Native startup assertion; browser uses authenticated shared services instead. '
                                      'Exact canonical basis: paragraph 103976 onward under Home runtime dependency '
                                      '(included common contract); this does not exclude app web authoring.')
    (requirements if app else shared).append(entry)

procedures = []
for app in [*APPS, 'ALL']:
    for name, _, ui, owner in GROUPS:
        procedures.append({
            'procedure_id': f'BP-{app}-{name.upper()}', 'status': 'AUTHORED_NOT_EXECUTED',
            'app': app, 'surface': ui, 'domain_owner': owner,
            'prerequisites': ['Exact deployment/frontend/backend identities, authorised fixture accounts',
                              'Supported browser/version and clean isolated profile',
                              'Populated fixture with stable object IDs and initial durable revision',
                              'Backend inspection or independent client through authorised owning APIs'],
            'steps': [
                'Select all mapped requirements and expand each compound clause into explicit assertions; do not reduce a paragraph to one success message.',
                'Capture initial IDs, semantic content, revisions, ACL, branch/session and relevant settings through the canonical service.',
                f'Execute each required {name} operation through the specified real {app} surface; record keyboard/pointer interactions and trace.',
                'Assert the exact canonical expected clause for every mapped requirement, including negative conditions and optional capability preservation.',
                'Inspect actual changed content/side effects through the owning API and a second clean client; compare stable IDs/revisions and preserve originals.',
                'Repeat applicable operation with denied/read-only/revoked permissions, stale revision, disconnected backend or interrupted save in isolated fixtures.',
                'Reopen/refresh/deep-link and verify durable semantic outcome, history, branch and meaningful error/recovery state.',
                'Record observed values, discovered/executed assertion counts, logs/traces/screenshots, artifact hashes and exact tested commit. Missing assertions remain NOT_RUN, never PASS.'
            ],
            'expected': 'Every mapped canonical clause is an independent required oracle; preserve all mandatory and default-required SHOULD assertions.',
            'automation_status': 'No browser acceptance runner exists yet; these are reproducible manual procedures once dependencies are available.'
        })

for req in requirements + shared:
    procedures.append({
        'procedure_id': f'BP-SPEC-P{req["source_start"]}',
        'status': 'AUTHORED_NOT_EXECUTED', 'source_requirement_id': req['requirement_id'],
        'app': req['app'], 'setup': 'Use populated canonical fixture and clean authorised staging browser profile; preserve original revision.',
        'interaction': req['specified_ui_surfaces'], 'domain_action_owner': req['domain_action_owner'],
        'expected_assertions': req['expected_assertions'],
        'verification': 'Compare semantic before/after values and IDs via actual owning API and second client; assert each source clause, then reopen. Audit permission, stale-revision and interruption negative controls where relevant.',
        'observed': None, 'outcome': 'NOT_RUN',
        'limitation': 'Paragraph-specific oracle is retained verbatim. Detailed fixture/action expansion remains mandatory before execution; authored mapping does not establish operation/UI implementation.'
    })

for name, value in [('requirements.json', requirements), ('shared-requirements.json', shared),
                    ('procedures.json', procedures), ('candidate-domain-tests.json', test_inventory)]:
    (OUT / name).write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n')

summary = {'source_revision': MANIFEST['revisionId'], 'repository_baseline': '98a08827c9fbc486fe987da73f4aee8398120d31',
           'source_sha256': hashlib.sha256((ROOT/'source/canonical.txt').read_bytes()).hexdigest(),
           'app_clause_counts': dict(collections.Counter(x['app'] for x in requirements)),
           'shared_clause_count': len(shared), 'procedure_count': len(procedures),
           'verified_browser_requirements': 0, 'browser_outcome': 'NOT_RUN',
           'scope_note': 'All nonempty source clauses/schema/API lines retained conservatively, not claimed atomic requirements. Compound clauses need assertion expansion; optional/conditional prose explicitly labelled for review.',
           'production_changes': [], 'domain_regression_runs': []}
(OUT/'manifest.json').write_text(json.dumps(summary, indent=2)+'\n')
print(json.dumps(summary, indent=2))
