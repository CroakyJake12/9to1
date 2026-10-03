#!/usr/bin/env python3
"""Rebuild B5 source-backed planning evidence; this does not execute product tests."""
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

BASE = Path(__file__).resolve().parent
SOURCE = BASE.parent / 'source'
REPO = Path('/workspace/team-b-worktree')
PARAGRAPHS = json.loads((SOURCE / 'paragraphs.json').read_text())
MANIFEST = json.loads((SOURCE / 'manifest.json').read_text())
RANGES = [
    ('general-and-agentic', 1, 57534),
    ('common-apps-and-web', 102889, 114171),
    ('accounts-business', 114171, 144371),
    ('admin', 199546, 218588),
    ('connect', 245455, 270648),
    ('ai', 320600, 426377),
    ('automations', 580796, 603719),
    ('external-integrations', 1163377, 1191693),
    ('terra-form-common', 1238694, 1253145),
    ('terra-form-cross-app', 1303935, 1315675),
    ('sol-happy', 1315675, 1342106),
]

def write(name, obj):
    (BASE / name).write_text(json.dumps(obj, indent=2, ensure_ascii=False) + '\n')

def category(text):
    if re.match(r'^OPEN\b', text):
        return 'OPEN'
    if re.search(r'\b(must|shall|required|acceptance|will|needs? to)\b', text, re.I):
        return 'MANDATORY'
    if re.search(r'\b(should|recommended)\b', text, re.I):
        return 'SHOULD_DEFAULT_REQUIRED'
    if re.search(r'\b(may|optional)\b', text, re.I):
        return 'OPTIONAL_OR_CONDITIONAL'
    return 'PRODUCT_PROSE_OR_LIST_ITEM'

def scope(row):
    index = row['index']
    if 320600 <= index < 355446:
        return 'Spaces'
    if 355446 <= index < 383080:
        return 'VisionVoice'
    if 383080 <= index < 410681:
        return 'AIStudio'
    if 410681 <= index < 426377:
        return 'Play'
    for name, a, b in RANGES:
        if a <= index < b:
            return name
    raise ValueError(index)

tests = json.loads((BASE / 'procedures.json').read_text())
test_ids = {p['id'] for p in tests['procedures']}

def procedures(s, heading, text):
    t = (heading + ' ' + text).lower()
    selected = {'B5-COMMON-API', 'B5-COMMON-CONTINUITY'}
    if s == 'accounts-business':
        selected.add('B5-ACCOUNT-AUTH')
        if any(x in t for x in ['profile', 'username', 'croakyjake', 'pronoun', 'provision']):
            selected.add('B5-ACCOUNT-PROFILE')
        if any(x in t for x in ['subscription', 'plan', 'threshold', 'dust', 'resource', 'billing', 'seat', 'business']):
            selected.update(['B5-ACCOUNT-PLAN', 'B5-BILLING-LIFECYCLE'])
        if any(x in t for x in ['pool', 'rollover', 'credit', 'reserve', 'settle', 'concurrent']):
            selected.add('B5-USAGE-CONCURRENCY')
        if 'biz-' in heading.lower() or 'organisation' in t:
            selected.add('B5-ORG-ISOLATION')
        if 'mesh' in t or 'device' in t:
            selected.add('B5-ADMIN-DEVICES')
        if 'agent' in t:
            selected.add('B5-TEAM-AGENT')
    elif s == 'admin':
        selected.update(['B5-ADMIN-LIFECYCLE', 'B5-ORG-ISOLATION'])
        if any(x in t for x in ['member', 'invite', 'role', 'seat', 'owner']):
            selected.add('B5-ADMIN-MEMBERS')
        if any(x in t for x in ['policy', 'forced', 'plugin', 'default model', 'restriction']):
            selected.add('B5-ADMIN-POLICY')
        if any(x in t for x in ['billing', 'invoice', 'quote', 'purchase']):
            selected.add('B5-BILLING-LIFECYCLE')
        if any(x in t for x in ['usage', 'allocation', 'rollover', 'credit']):
            selected.add('B5-USAGE-CONCURRENCY')
        if any(x in t for x in ['device', 'mesh', 'endpoint']):
            selected.add('B5-ADMIN-DEVICES')
        if 'agent' in t:
            selected.add('B5-TEAM-AGENT')
        if any(x in t for x in ['audit', 'bulk', 'job', 'rollback', 'failure', 'recovery', 'export', 'import']):
            selected.add('B5-ADMIN-AUDIT')
    elif s == 'connect':
        selected.update(['B5-CONNECT-MESSAGING', 'B5-CONNECT-ENCRYPTION', 'B5-CONNECT-DONOR'])
        if any(x in t for x in ['call', 'record', 'media', 'camera', 'screen']):
            selected.add('B5-CONNECT-CALLS')
        if any(x in t for x in ['record', 'transcription', 'ai participation']):
            selected.add('B5-CONNECT-CONSENT')
        if any(x in t for x in ['sms', 'rcs', 'agent', 'email', 'transport']):
            selected.add('B5-CONNECT-TRANSPORT')
        if any(x in t for x in ['block', 'mute', 'contact settings', 'disappear', 'expir']):
            selected.add('B5-CONNECT-PRIVACY')
        if any(x in t for x in ['report', 'moderation', 'incident', 'enforcement', 'appeal', 'ban']):
            selected.add('B5-CONNECT-SAFETY')
    elif s == 'Spaces':
        selected.add('B5-SPACES-STRUCTURE')
        if any(x in t for x in ['conversation', 'branch', 'stop', 'resume', 'retry', 'request', 'long-running', 'action graph']):
            selected.add('B5-SPACES-RUNS')
        if any(x in t for x in ['context', 'source', 'memory', 'den', 'web access', 'retrieval', 'citation']):
            selected.add('B5-SPACES-CONTEXT')
        if any(x in t for x in ['graph', 'extension', 'sidebar', 'plugin']):
            selected.add('B5-SPACES-GRAPHS')
        if any(x in t for x in ['study', 'task', 'shopping', 'research', 'translate', 'experience']):
            selected.add('B5-SPACES-TYPES')
        if any(x in t for x in ['artifact', 'cross-app', 'productivity', 'shared object', 'edit', 'canvas', 'write']):
            selected.add('B5-CROSSAPP-SEMANTIC')
        if 'agent' in t:
            selected.add('B5-TEAM-AGENT')
    elif s == 'AIStudio':
        selected.add('B5-STUDIO-PROJECTS')
        if any(x in t for x in ['harness', 'playground', 'runtime', 'test', 'install', 'tool', 'skill', 'plugin', 'mcp']):
            selected.add('B5-STUDIO-RUNTIME')
        if any(x in t for x in ['agent', 'avatar', 'animation']):
            selected.add('B5-STUDIO-AGENT')
        if any(x in t for x in ['evaluation', 'grader', 'test suite', 'comparison', 'baseline']):
            selected.add('B5-STUDIO-EVALUATIONS')
        if any(x in t for x in ['schema', 'router', 'generative', 'run profile', 'resource', 'template', 'documentation', 'dependency']):
            selected.add('B5-STUDIO-RESOURCES')
        if any(x in t for x in ['replay', 'context inspector', 'permission simul', 'context report']):
            selected.add('B5-STUDIO-DEBUG')
    elif s == 'VisionVoice':
        selected.update(['B5-VOICE-SESSION', 'B5-VOICE-CAPTURE'])
        if 'listener' in t or 'ambient' in t:
            selected.add('B5-VOICE-LISTENER')
        if 'translate' in t or 'language' in t:
            selected.add('B5-VOICE-TRANSLATE')
        if 'dictation' in t:
            selected.add('B5-VOICE-DICTATION')
        if 'overlay' in t or 'automation' in t:
            selected.add('B5-VOICE-OVERLAY')
    elif s == 'Play':
        selected.update(['B5-PLAY-MATCH', 'B5-PLAY-ISOLATION'])
    elif s == 'automations':
        selected.add('B5-AUTOMATION-GRAPH')
        if any(x in t for x in ['wait', 'approval', 'run', 'trigger', 'retry', 'loop', 'restart', 'location', 'target']):
            selected.add('B5-AUTOMATION-DURABLE')
        if any(x in t for x in ['device', 'third-party', 'provider', 'connection', 'capability']):
            selected.add('B5-AUTOMATION-CAPABILITIES')
    elif s == 'external-integrations':
        selected.update(['B5-INTEGRATIONS-CONNECTION', 'B5-INTEGRATIONS-PACKAGE'])
    elif s.startswith('terra-form'):
        selected.add('B5-TERRAFORM-INTEROP')
    elif s == 'sol-happy':
        selected.add('B5-ONLINE-RELEASE')
        if any(x in t for x in ['billing', 'stripe', 'payment', 'subscription']):
            selected.add('B5-BILLING-LIFECYCLE')
        if any(x in t for x in ['ai', 'long-running', 'job', 'dust', 'reserve', 'settle']):
            selected.add('B5-AUTOMATION-DURABLE')
        if any(x in t for x in ['account', 'auth', 'session', 'sign-in']):
            selected.add('B5-ACCOUNT-AUTH')
    else:
        selected.add('B5-COMMON-QUALITY')
    if any(x in t for x in ['permission', 'trust', 'privacy', 'revok', 'secret', 'local-only', 'denied', 'read-only']):
        selected.add('B5-COMMON-PERMISSIONS')
    if any(x in t for x in ['keyboard', 'accessible', 'accessibility', 'screen-reader', 'theme', 'zoom', 'motion', 'narrow', 'touch']):
        selected.add('B5-COMMON-ACCESSIBILITY')
    if any(x in t for x in ['performance', 'large', 'resource', 'incremental', 'pagination', 'responsive']):
        selected.add('B5-COMMON-RESOURCE')
    return sorted(selected)

domain_paths = {
    'accounts-business': ['apps/Web/Auth/account-api-client.js'],
    'external-integrations': ['9to1 Workspace/shared/src/Haven.Application/ExternalConnections/ExternalConnectionRegistryService.cs', '9to1 Workspace/shared/src/Haven.Infrastructure/ExternalConnections/McpConnectionClient.cs', '9to1 Workspace/shared/src/Haven.Application/Extensions/NativePluginRuntime.cs'],
    'Spaces': ['9to1 Workspace/shared/src/Haven.Application/Spaces/SpaceRegistry.cs', '9to1 Workspace/shared/src/Haven.Application/Spaces/SpaceConversationService.cs', '9to1 Workspace/Spaces/Source/Chat/HavenChatSpaceBackend.cs', '9to1 Workspace/Spaces/Source/Tasks/TasksSpaceStateMachine.cs', '9to1 Workspace/Spaces/Source/Experiences/ExperienceStateRepository.cs'],
    'AIStudio': ['9to1 Workspace/Studio/HavenOS.Studio/AIStudio/AIStudioApi.cs'],
    'VisionVoice': ['9to1 Workspace/shared/src/Haven.Application/Call/VisionVoiceSessionService.cs', '9to1 Workspace/shared/src/Haven.Application/Call/VisionVoiceActionCatalog.cs'],
    'Play': ['9to1 Workspace/shared/src/Haven.Application/Play/PlayMatchService.cs'],
    'automations': ['9to1 Workspace/shared/src/Haven.Application/Automations/AutomationGraphRuntime.cs', '9to1 Workspace/shared/src/Haven.Application/Automations/ScheduledTaskRuntime.cs', '9to1 Workspace/shared/src/Haven.Application/Tools/AutomationToolRuntime.cs', '9to1 Workspace/shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.cs'],
    'connect': ['9to1 Workspace/shared/src/Haven.Application/CallAbstractions.cs', '9to1 Workspace/shared/src/Haven.Application/MailAbstractions.cs', '9to1 Workspace/Mail/Services/MailDomainService.cs', '9to1 Workspace/shared/src/Haven.Application/ExternalConnections/ExternalConnectionRegistryService.cs'],
}
inventory = []
for product, paths in domain_paths.items():
    for path in paths:
        file = REPO / path
        if not file.is_file():
            inventory.append({'product': product, 'path': str(file), 'exists': False})
            continue
        text = file.read_text()
        methods = []
        if file.suffix == '.js':
            for m in re.finditer(r'^  (?:async )?([a-zA-Z]\w*)\(', text, re.M):
                if m.group(1) != 'constructor':
                    methods.append({'name': m.group(1), 'line': text.count('\n', 0, m.start()) + 1})
        for m in re.finditer(r'^\s*public\s+(?:async\s+)?(?:Task(?:<[^\n]+?>)?|ValueTask(?:<[^\n]+?>)?|bool|void|IAsyncEnumerable<[^\n]+?>|[\w<>?,]+)\s+(\w+)\s*\(', text, re.M):
            methods.append({'name': m.group(1), 'line': text.count('\n', 0, m.start()) + 1})
        inventory.append({'product': product, 'path': str(file), 'exists': True, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'public_methods': methods, 'evidence_kind': 'STATIC_SOURCE_ONLY', 'hosted_protocol_verified': False})
write('existing-domain-inventory.json', inventory)

explicit_methods = {
 '9to1.Account.GetCurrent': 'getCurrent', '9to1.Account.ListSessions': 'listSessions',
 '9to1.Account.RevokeSession': 'revokeSession', '9to1.Account.RevokeAllOtherSessions': 'revokeAllOtherSessions',
 '9to1.Automations.List': 'GetAllAsync', '9to1.Automations.Create': 'UpsertAsync',
 '9to1.Automations.Update': 'UpsertAsync', '9to1.Automations.ListRuns': 'GetRunsAsync',
 '9to1.Automations.Validate': 'Validate',
 'Dulche.MCP.Connect': 'AddMcpAsync', 'Dulche.MCP.Disconnect': 'RemoveAsync',
 'Dulche.MCP.ListServers': 'GetAllAsync', 'Dulche.MCP.GetCapabilities': 'DiscoverCapabilitiesAsync',
 'Dulche.MCP.ListTools': 'DiscoverAsync', 'Dulche.MCP.CallTool': 'InvokeAsync',
 'Dulche.MCP.ReadResource': 'ReadResourceAsync', 'Dulche.MCP.GetPrompt': 'GetPromptAsync',
 'Dulche.Skill.ResolveFor': 'ResolveSkillsForAsync', 'Dulche.Plugin.Invoke': 'InvokeAsync',
 '9to1.Spaces.List': 'GetAllAsync', '9to1.Spaces.Get': 'GetAsync',
 '9to1.Spaces.Create': 'CreateAsync', '9to1.Spaces.Update': 'UpdateAsync',
 '9to1.Spaces.Delete': 'DeleteAsync', '9to1.Spaces.Restore': 'RestoreAsync',
 '9to1.Spaces.Move': 'MoveAsync', '9to1.Spaces.ListConversations': 'GetConversationsAsync',
 '9to1.Spaces.CreateConversation': 'CreateChatAsync', '9to1.Spaces.GetConversation': 'GetConversationAsync',
 '9to1.Spaces.Send': 'SendAsync', '9to1.Spaces.Regenerate': 'RegenerateAsync',
 '9to1.Spaces.BranchFrom': 'CreateBranchAsync', '9to1.Spaces.SwitchBranch': 'SwitchBranchAsync',
 '9to1.AIStudio.ListProjects': 'ListProjectsAsync', '9to1.AIStudio.CreateProject': 'CreateProjectAsync',
 '9to1.AIStudio.OpenProject': 'OpenProjectAsync', '9to1.AIStudio.ValidateProject': 'ValidateProjectAsync',
 '9to1.AIStudio.TestProject': 'TestProjectAsync', '9to1.AIStudio.ExportProject': 'ExportProjectAsync',
 '9to1.AIStudio.Harness.CreateFromTemplate': 'CreateHarnessFromTemplateAsync',
 '9to1.AIStudio.AgentBuilder.Create': 'CreateAgentAsync', '9to1.AIStudio.AgentBuilder.Open': 'OpenAgentAsync',
 '9to1.AIStudio.AgentBuilder.UpdateDraft': 'UpdateAgentDraftAsync', '9to1.AIStudio.AgentBuilder.Validate': 'ValidateAgentAsync',
 '9to1.AIStudio.AgentBuilder.Preview': 'PreviewAgentAsync', '9to1.AIStudio.AgentBuilder.Activate': 'ActivateAgentAsync',
 '9to1.AIStudio.AgentBuilder.Share': 'ShareAgentAsync', '9to1.AIStudio.ToolBuilder.Create': 'CreateToolAsync',
 '9to1.AIStudio.ToolBuilder.Validate': 'ValidateProjectAsync', '9to1.AIStudio.ToolBuilder.Install': 'InstallToolAsync',
 '9to1.AIStudio.Playground.Run': 'RunPlaygroundAsync', '9to1.AIStudio.Evaluations.Create': 'CreateEvaluationSuiteAsync',
 '9to1.AIStudio.Evaluations.Run': 'RunEvaluationAsync', '9to1.AIStudio.Tests.CreateSuite': 'CreateTestSuiteAsync',
 '9to1.AIStudio.Tests.Run': 'RunTestSuiteAsync', '9to1.AIStudio.Replay.Open': 'OpenReplayAsync',
 '9to1.AIStudio.Replay.RestartFrom': 'RestartReplayAsync', '9to1.AIStudio.Schemas.Create': 'CreateSchemaAsync',
 '9to1.AIStudio.ModelRouters.Create': 'CreateModelRouterAsync', '9to1.AIStudio.GenerativeUI.Create': 'CreateGenerativeUiAsync',
 '9to1.AIStudio.RunProfiles.Create': 'CreateRunProfileAsync', '9to1.AIStudio.ContextInspector.Get': 'InspectContextAsync',
 '9to1.AIStudio.PermissionSimulator.Evaluate': 'SimulatePermissionsAsync', '9to1.AIStudio.Dependencies.Get': 'InspectDependenciesAsync',
 '9to1.AIStudio.Compare': 'CompareAsync', '9to1.AIStudio.Documentation.Generate': 'GenerateDocumentationAsync',
 '9to1.VisionVoice.Start': 'StartAsync', '9to1.VisionVoice.SetMode': 'SetModeAsync',
}

def action_binding(product, action):
    expected = explicit_methods.get(action, action.rsplit('.', 1)[-1] + 'Async')
    matches = []
    for item in inventory:
        if item['product'] != product:
            continue
        if action.startswith(('Dulche.Plugin.', 'Dulche.Skill.')) and not item['path'].endswith('/NativePluginRuntime.cs'):
            continue
        if action in ['Dulche.MCP.CallTool', 'Dulche.MCP.GetPrompt', 'Dulche.MCP.ReadResource', 'Dulche.MCP.ListTools', 'Dulche.MCP.GetCapabilities'] and not item['path'].endswith('/McpConnectionClient.cs'):
            continue
        for method in item.get('public_methods', []):
            if method['name'] == expected:
                matches.append({'path': item['path'], 'method': expected, 'line': method['line'],
                    'evidence': 'STATIC_RELATED_METHOD_ONLY; complete canonical semantics/network binding unverified'})
    return matches or None

account_routes = {
 '9to1.Account.GetCurrent': ('GET', '/api/account/current', 'cake:account:read'),
 '9to1.Account.ListSessions': ('GET', '/api/account/sessions', 'cake:sessions:read'),
 '9to1.Account.RevokeSession': ('DELETE', '/api/account/sessions/{sessionId}', 'cake:sessions:revoke'),
 '9to1.Account.RevokeAllOtherSessions': ('POST', '/api/account/revoke-other-sessions', 'cake:sessions:revoke'),
}

rows = []
headings = []
for row in PARAGRAPHS:
    is_heading = row['heading'] != 'NORMAL_TEXT'
    if is_heading:
        level = 0 if row['heading'] == 'TITLE' else int(row['heading'].rsplit('_', 1)[1])
        headings = [(n, t) for n, t in headings if n < level]
        headings.append((level, row['text'].strip()))
    if is_heading or not row['text'].strip() or not any(a <= row['index'] < b for _, a, b in RANGES):
        continue
    text = row['text'].strip()
    s = scope(row)
    h = ' / '.join(t for _, t in headings if t)
    assigned = procedures(s, h, text)
    canonical_actions = re.findall(r'\b(?:9to1|Dulche)\.[\w.]+(?=\s*\()', text)
    record = {
        'id': f"B5-SPEC-{row['index']:07d}", 'source_tab': row['tab'],
        'source_start': row['index'], 'source_end': row['end'],
        'source_revision': MANIFEST['revisionId'], 'section': h,
        'product': s, 'text': text, 'normative_level': category(text),
        'canonical_actions': sorted(set(canonical_actions)),
        'requirement_state': 'BLOCKED',
        'test_outcome': 'NOT_RUN', 'test_ids': assigned,
        'expected': text,
        'implementation_references': [x['path'] for x in inventory if x['product'] == s and x.get('exists')],
        'implementation_mapping_status': 'RELATED_LOCAL_CONTRACTS_ONLY' if s in domain_paths else 'NO_VERIFIED_B5_DOMAIN_BINDING',
        'blocked_by': ['No verified authenticated Team C service contract/deployment identity or clean-browser integration fixture'],
        'coverage_review': 'DRAFT_REQUIRES_B6_CLAUSE_AND_DONOR_REVIEW',
        'applicability': 'Cross-platform requirements retained; native-specific checks are delegated platform gates, not deleted browser-denominator exceptions',
    }
    if record['normative_level'] == 'OPEN':
        record['blocked_by'] = ['Explicit canonical OPEN decision; do not choose a commercial/security/architecture default']
    if s in ['VisionVoice', 'Play', 'connect']:
        record['ownership_note'] = 'Coordinate browser surface ownership with B3; B5 maps service/auth integration and preserves full product criteria'
    if s.startswith('terra-form'):
        record['ownership_note'] = 'B4 owns Terra-form surfaces; B5 verifies Spaces/AI bar/canonical service interoperability'
    rows.append(record)

# Add a separately addressable action row for every explicit canonical call.
actions = []
for row in rows:
    for name in row['canonical_actions']:
        route = account_routes.get(name)
        actions.append({
            'id': f"{row['id']}-API-{name}", 'source_requirement': row['id'],
            'canonical_action': name, 'product': row['product'],
            'test_ids': row['test_ids'], 'test_outcome': 'NOT_RUN',
            'requirement_state': 'BLOCKED', 'server_route': route[1] if route else None,
            'server_method': route[0] if route else None,
            'server_scope': route[2] if route else None,
            'server_route_status': 'ACKNOWLEDGED_SOURCE_CONTRACT_ONLY; live deployment/browser integration NOT_RUN' if route else 'UNSPECIFIED_NOT_INVENTED',
            'protocol_provenance': '7cc52df0411e0e9e617918f6c5158283cc3199fc:cloud/cake-id-auth/src/resource-api.ts' if route else None,
            'existing_method_binding': action_binding(row['product'], name),
            'binding_gap': 'Client logic independently verified; issuer configuration, live server and browser surface acceptance blocked' if route else 'No owner-acknowledged authenticated network binding; local method presence alone is not action parity',
        })

# Admin declares action families in prose rather than standalone call syntax.
for row in rows:
    if 'Required action families are Organisations' not in row['text']:
        continue
    for family, members in re.findall(r'([A-Za-z/]+) \(([^)]+)\)', row['text']):
        for operation in members.split(','):
            operation = re.sub(r'\s+where cancellable$', '', operation.strip())
            if not re.fullmatch(r'[A-Za-z]+', operation):
                continue
            actions.append({'id': f"{row['id']}-API-Admin-{family}-{operation}", 'source_requirement': row['id'], 'canonical_action': f'9to1.Admin.{family}.{operation}', 'product': 'admin', 'test_ids': row['test_ids'], 'test_outcome': 'NOT_RUN', 'requirement_state': 'BLOCKED', 'server_route': None, 'server_route_status': 'UNSPECIFIED_NOT_INVENTED', 'existing_method_binding': None})

write('requirements.json', {'schema_version': 1, 'source': MANIFEST, 'ranges': RANGES, 'rows': rows})
write('canonical-actions.json', {'schema_version': 1, 'actions': actions})
summary = {'schema_version': 1, 'source_revision': MANIFEST['revisionId'], 'source_sha256': hashlib.sha256((SOURCE / 'canonical.txt').read_bytes()).hexdigest(), 'candidate_base': '98a0882', 'requirements': len(rows), 'canonical_action_occurrences': len(actions), 'procedures': len(test_ids), 'by_product': dict(Counter(x['product'] for x in rows)), 'by_normative_level': dict(Counter(x['normative_level'] for x in rows)), 'requirement_states': dict(Counter(x['requirement_state'] for x in rows)), 'test_outcomes': dict(Counter(x['test_outcome'] for x in rows)), 'behavior_verified': 0, 'coverage_status': 'SOURCE_PARAGRAPH_COVERAGE_ONLY; NOT CLAUSE-LEVEL OR DONOR-PARITY ACCEPTANCE', 'cross_chat_transport': 'Root reports Team C coordination readback 41e0ccc acknowledging Team B claims and protocol source7cc52df; root alone publishes B5 requests. This local evidence is not itself transport.', 'execution': 'No product acceptance tests executed by this evidence generator'}
write('summary.json', summary)

missing = sorted({t for row in rows for t in row['test_ids']} - test_ids)
assert not missing, missing
expected = {x['index'] for x in PARAGRAPHS if x['heading'] == 'NORMAL_TEXT' and x['text'].strip() and any(a <= x['index'] < b for _, a, b in RANGES)}
assert expected == {x['source_start'] for x in rows}
assert len({x['id'] for x in rows}) == len(rows)
print(json.dumps(summary, indent=2))
