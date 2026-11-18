from pathlib import Path
import json,shutil,zipfile,hashlib,os
ROOT=Path(__file__).resolve().parents[1];SPEC=ROOT/'spec-revision';OUT=ROOT/'output';SHOTS=ROOT/'mockups/screens';PACK=ROOT/'mockups/implementation-pack';DEST=SPEC/'delivery-pack'
M=json.loads((SPEC/'modules.json').read_text());SCREENS=json.loads((ROOT/'mockups/manifest.json').read_text());LOOK={s['id']:s for s in SCREENS}
shutil.rmtree(DEST,ignore_errors=True);DEST.mkdir()
for folder in ['source','modules','technical','history','catalog']:shutil.copytree(SPEC/folder,DEST/folder)
for filename in ['modules.json','owner_overrides.json','coverage_report.json','validation_report.json']:shutil.copy2(SPEC/filename,DEST/filename)
(DEST/'designs').mkdir()
for s in SCREENS:shutil.copy2(SHOTS/(s['id']+'.png'),DEST/'designs'/(s['id']+'.png'))
for key in ['professional','glow','playful','frosted','bubble','metallic','retro','western']:
 shutil.copy2(SHOTS/('theme-present-'+key+'.png'),DEST/'designs'/('theme-present-'+key+'.png'))
shutil.copy2(OUT/'9to1_Interface_Atlas.html',DEST/'9to1_Interface_Atlas.html')
shutil.copy2(PACK/'tokens.json',DEST/'tokens.json')
shutil.copy2(ROOT/'mockups/manifest.json',DEST/'screen-manifest.json')

def design_section(m):
 parts=['# Exact designs and implementation rules','','These figures are the exact current atlas compositions. Their stable screen IDs match the Word specification and editable source. Open the atlas for full-size theme and interaction inspection.','']
 for sid in m['screen_ids']:
  s=LOOK[sid];parts += ['## '+s['title'],'',f'![{s["title"]}](../designs/{sid}.png)','',f'Screen ID: `{sid}`. Source: {s["source"]}.','']
  parts += ['- '+n for n in s['notes']]+['']
 if m['id']=='cui':
  parts+=['## Eight current themes on the same editor','']
  for key,name in [('professional','Professional'),('glow','Glow'),('playful','Playful'),('frosted','Frosted Glass'),('bubble','Bubble'),('metallic','Metallic'),('retro','Retro'),('western','Western')]:parts+=['### '+name,'',f'![{name} applied to Present](../designs/theme-present-{key}.png)','']
 return '\n'.join(parts) if m['screen_ids'] or m['id']=='cui' else ''
for m in M:
 if not m['screen_ids'] and m['id']!='cui':continue
 for key in ['sourcefile','complete_sourcefile']:
  path=DEST/m[key];path.write_text(path.read_text()+'\n\n'+design_section(m))
# A simple entry point keeps focused agent tasks out of the complete source corpus.
readme=['# 9to1 Development Specification v3','','This pack contains the current app contracts, exact reference designs and the retained technical specification. The illustrated Word specification and standalone Spaces Word document are provided separately.','','## Use one module at a time','','1. Open `modules/<module>.md` for its prerequisites, overview, current rules and exact designs.','2. Use that module\'s technical reading map to load the smaller chapters needed for the task.','3. Use `modules/<module>.complete.md` when you need the module\'s entire retained technical contract in one copyable file.','4. Read prerequisite contracts as shared interfaces; a dependency does not require every dependent app UI to be completed.','','For Spaces, start with `modules/spaces.md`. Its prerequisites cover CUI, Dulche, Node Editor, the Assistants system and the canonical identity, permission and persistence interfaces. The chapter includes generated UI, thinking, subagents, live artifacts and generated files.','','The current master document is organised by the same modules. Its images and the PNGs here come from the same offline atlas source.','','## Current authority','','Latest owner revisions and the v3 override ledger control within their stated scope. Only presentation slides 1–16 are authoritative. The older Study behaviour is retained inside the current wrapper. The eight current themes are Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western. Cinematic is labelled legacy compatibility.','','The `source/` directory preserves the baseline and incorporated documents. `catalog/` accounts for every source block; `history/` preserves replaced wording and historical process instructions. These directories are provenance and technical reference, not a command to read all material before every task.','','## Module index','']
for m in M:readme+=['- ['+m['title']+']('+m['sourcefile']+') — '+str(len(m['screen_ids']))+' design states; '+str(len(m['technical_chunks']))+' technical chapters.']
readme += ['','## Reading boundaries','','The current primary chapters are concise entry contracts. Detailed source requirements, data models, APIs, recovery semantics and donor parity remain in the named technical chapters. Source accounting does not imply that every historical sentence has been independently rewritten or that production implementation and acceptance tests are complete.','']
(DEST/'README.md').write_text('\n'.join(readme))
# Keep original agent authoring code separate from product source, but reproducible.
(DEST/'authoring').mkdir()
for n in ['build_module_pack.py','verify_module_pack.py','module_content.py','create_documents.py','package_deliverables.py']:shutil.copy2(SPEC/n,DEST/'authoring'/n)
(DEST/'authoring/README.md').write_text('Authoring source for the specification. Input sources are preserved under ../source and the canonical atlas source is in the separate implementation pack. Adjust the workspace input paths before rebuilding in another environment.\n')
# Add a direct implementation handoff to the design source package.
implementation_map=['# Specification v3 reading map','','The specification modules are in the separately supplied `9to1_Modular_Specification.zip`. Extract that pack and open its `README.md`; the paths below refer to that pack, not this design-source folder.','','Start Spaces work with `modules/spaces.md`, then load only the named technical chapters needed for the task. The illustrated Word master and standalone Spaces Word document are supplied separately.','','## Module paths','']
for m in M:implementation_map+=['- '+m['title']+' — `'+m['sourcefile']+'`.']
(PACK/'reference/Specification_v3_reading_map.md').write_text('\n'.join(implementation_map)+'\n')
shutil.copy2(SPEC/'owner_overrides.json',PACK/'reference/owner-overrides-v3.json')
shutil.copy2(SPEC/'coverage_report.json',PACK/'reference/specification-coverage-v3.json')
for root,target in [(DEST,OUT/'9to1_Modular_Specification.zip'),(PACK,OUT/'9to1_Implementation_Pack.zip')]:
 files=sorted(p for p in root.rglob('*') if p.is_file() and p.name!='checksums.json')
 checks={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files};(root/'checksums.json').write_text(json.dumps(checks,indent=2))
 pending=target.with_suffix(target.suffix+'.pending')
 with zipfile.ZipFile(pending,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
  for p in sorted(root.rglob('*')):
   if p.is_file():z.write(p,str(p.relative_to(root)))
 with pending.open('rb') as f:os.fsync(f.fileno())
 os.replace(pending,target)
 print(json.dumps({'file':str(target),'files':len(files)+1,'bytes':target.stat().st_size}))
