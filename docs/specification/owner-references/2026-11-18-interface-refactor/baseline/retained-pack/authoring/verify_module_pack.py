#!/usr/bin/env python3
"""Verify source accounting, generated cross-references and module integrity."""
import json, hashlib, collections, re
from pathlib import Path
from build_module_pack import validate_dependencies
from module_content import MODULES

P=Path(__file__).resolve().parent
read=lambda x: json.loads((P/x).read_text())
modules=read('modules.json');blocks=read('catalog/source_blocks.json');sources=read('catalog/source_manifest.json')
errors=[];checks=[]
def check(ok,description):
    checks.append({'check':description,'pass':bool(ok)})
    if not ok:errors.append(description)

check(len(modules)==len({m['id'] for m in modules})==len(MODULES),'Module identities are unique and match the authored roster')
check(len(blocks)==len({b['id'] for b in blocks}),'Source block identities are unique')
all_words=0
for s in sources:
    raw=(P/s['path']).read_bytes();text=raw.decode('utf-8-sig');lines=text.splitlines()
    matching=[b for b in blocks if b['source']==s['id']]
    covers=collections.Counter(i for b in matching for i in range(b['line_start'],b['line_end']+1))
    missing=[i for i,l in enumerate(lines,1) if l.strip() and covers[i]!=1]
    faithful=all('\n'.join(lines[b['line_start']-1:b['line_end']]).lstrip('\ufeff')==b['original_text'].lstrip('\ufeff') for b in matching)
    words=sum(len(b['original_text'].split()) for b in matching)
    check(hashlib.sha256(raw).hexdigest()==s['sha256'],s['id']+': archive digest matches manifest')
    check(not missing,s['id']+': every nonblank source line is covered exactly once')
    check(faithful,s['id']+': every original source block matches its archived source lines')
    check(words==len(text.split())==s['words'],s['id']+': source/block/manifest word counts agree')
    all_words+=words

active={'retained','amended','duplicate-reference'}
referenced=set();screen_ids=[];requirement_ids=[]
for m in modules:
    check((P/m['sourcefile']).exists() and (P/m['complete_sourcefile']).exists(),m['id']+': primary and complete module files exist')
    complete=(P/m['complete_sourcefile']).read_text()
    bs=[b for b in blocks if b['module']==m['id']]
    check(set(m['source_block_ids'])=={b['id'] for b in bs},m['id']+': all assigned source blocks appear in module ledger')
    check(all(b['active_text'] in complete for b in bs if b['status'] in active and not b.get('duplicate_of')),m['id']+': all active technical text is retained in complete module')
    for c in m['technical_chunks']:
        referenced.add(c['path'])
        check((P/c['path']).exists() and c.get('subjects') and c.get('load_for'),c['id']+': named chunk, subjects and load guidance exist')
    screen_ids+=m['screen_ids']
    requirement_ids+=re.findall(r'\*\*(V3-[A-Z0-9-]+-\d{2})\*\*',m['primary_markdown'])
check(len(screen_ids)==len(set(screen_ids))==len(read('catalog/screen_mapping.json')),'Each registered screen has exactly one primary module')
check(len(requirement_ids)==len(set(requirement_ids)),'Current requirement IDs are unique')
check(all(b['module'] in MODULES for b in blocks),'No source block is assigned to an undefined module')
check(all('systemlication' not in b['active_text'] for b in blocks),'No malformed app/application editorial substitutions remain')
try:
    deps=validate_dependencies(MODULES);check(True,'Prerequisite contracts form an acyclic graph')
except Exception as exc:
    deps=[];check(False,str(exc))
orphans={str(f.relative_to(P)) for f in (P/'technical').glob('*.md')}-referenced
check(not orphans,'No stale unreferenced generated technical chunks remain')
report={'passed':not errors,'checks':len(checks),'failed_checks':errors,'source_words':all_words,'source_blocks':len(blocks),'modules':len(modules),'screens':len(screen_ids),'unique_current_requirement_ids':len(requirement_ids),'technical_chunks':len(referenced),'source_accounting_is_not_runtime_acceptance':True,'details':checks}
(P/'validation_report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k!='details'},indent=2))
raise SystemExit(0 if not errors else 1)
