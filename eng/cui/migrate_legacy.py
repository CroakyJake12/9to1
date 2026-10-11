#!/usr/bin/env python3
"""Explicit, reviewable one-time source migration. Never imported by a shipping loader.

Only XML-shaped legacy source with known equivalents is transformed. Canonical CUI
shorthand is retained byte-for-byte when no legacy syntax is present. Unknown or
ambiguous visual constructs require a reviewed app adapter instead of a guessed
Object or Definition wrapper. No source is overwritten without --apply.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
VISUAL = {'Page','Container','Text','Image','Audio','Video','Object','Button','Input','Layer'}
NONVISUAL = {'Cui','Actions','Action','Theme','DefaultTheme','Style','Property','Variable','Prefab','ActionEvent','If','Else','Repeat','Import','Keyframe','Anchor','Definition'}
DONOR = {'Grid','StackPanel','WrapPanel','ScrollViewer','TextBlock','Border','TextBox','ComboBox','CheckBox','RadioButton','Slider','ListBox','Panel','Canvas','Menu','MenuItem','ContextMenu','Separator','ContentControl','UserControl','DockPanel','TabControl','TabItem','ProgressBar','ItemsControl','TreeView'}
EXCLUDED_PARTS = {'vendor','Source','reference','handoff','artifacts','scratch','releases','node_modules','bin','obj'}
HISTORICAL = {'apps/Web/Tests/PictureAdmission/Fixtures/Admission01/Picture.cui', 'apps/Web/Tests/PictureAdmission/Fixtures/ProductionOriginal/Picture.cui'}
EXCLUDED_PLATFORM_DECLARATIONS = {'9to1 Workspace/Picture/App.axaml','9to1 Workspace/Picture/MainWindow.axaml'}
ATTRS = {
    'columndefinitions':'ColumnSizes', 'rowdefinitions':'RowSizes',
    'gridcolumn':'Column', 'gridrow':'Row', 'gridcolumnspan':'ColumnSpan', 'gridrowspan':'RowSpan',
    'spacing':'CellGap','gap':'CellGap','borderbrush':'BorderColor','borderthickness':'BorderWidth',
    'isenabled':'Interactive','enabled':'Interactive','isvisible':'Active','visible':'Active',
    'ischecked':'Checked','acceptsreturn':'Multiline','watermark':'PlaceholderText',
    'automationid':'ID','accessibilitylabel':'AccessibleName','cliptobounds':'Clip',
}
SIMPLE = {
    'Grid':('Container','Grid'),'Panel':('Container','Overlay'),'Border':('Container','Overlay'),
    'ScrollViewer':('Container','Overlay'),'TextBlock':('Text',None),'TextBox':('Input','Text'),
    'ComboBox':('Input','Select'),'ListBox':('Input','List'),'CheckBox':('Input','Checkbox'),
    'RadioButton':('Input','Radio'),'Slider':('Input','Slider'),'Canvas':('Container','Absolute'),
    'Menu':('Container','Menu'),'ContextMenu':('Container','Context Menu'),'MenuItem':('Button','Menu Item'),
}
class MigrationError(ValueError): pass

def key(value: str) -> str:
    return value.replace('-','').replace('.','').replace('_','').lower()

def sizes(value: str) -> str:
    if value.startswith('{'): return value
    return ' '.join('1fr' if item == '*' else item[:-1]+'fr' if item.endswith('*') else item
                    for item in re.split(r'[ ,]+',value.strip()) if item)

def migrate(source: str, path: str = '<input>') -> tuple[str,list[str]]:
    if '/Boards/hui/' in path and re.search(r'\bLayout=',source):
        raise MigrationError(f'{path}: the actual host still uses HavenMarkupParser; migrate that host and its scene contracts before changing markup semantics.')
    legacy = bool(re.search(r'<\s*/?\s*(?:'+ '|'.join(DONOR)+r')\b',source,re.I))
    try:
        root = ET.fromstring(source, parser=ET.XMLParser(target=ET.TreeBuilder(insert_comments=True)))
    except ET.ParseError as error:
        if not legacy: return source, ['Canonical non-XML CUI preserved; production compiler remains authoritative.']
        raise MigrationError(f'{path}: legacy/non-XML input requires a manual migration: {error}') from error
    notes: list[str] = []
    preserved_whitespace = []
    for element in root.iter():
        if not isinstance(element.tag,str): continue
        if element.tag.startswith('{'):
            raise MigrationError(f'{path}: namespaced donor/application element {element.tag}; migrate its code-behind explicitly.')
        tag = next((known for known in VISUAL | NONVISUAL | DONOR if known.lower() == element.tag.lower()),element.tag)
        element.tag = tag
        original = dict(element.attrib)
        normalized = {key(k):v for k,v in original.items()}
        if tag == 'Definition' and normalized.get('name') in DONOR:
            raise MigrationError(f'{path}: forbidden donor type cannot be re-exported as a Definition.')
        if normalized.get('whitespace','').lower() == 'preserve':
            preserved_whitespace.extend((node,node.text,node.tail) for node in element.iter())
        if tag in SIMPLE:
            element.tag, kind = SIMPLE[tag]
            if kind: element.set('Type',kind)
        elif tag == 'StackPanel':
            orientation = normalized.get('orientation','Vertical')
            if orientation not in ('Vertical','Horizontal'): raise MigrationError(f'{path}: dynamic or unknown StackPanel orientation must be reviewed.')
            element.tag = 'Container'; element.set('Type',orientation+' Stack')
        elif tag == 'WrapPanel':
            orientation = normalized.get('orientation','Horizontal')
            if orientation not in ('Vertical','Horizontal'): raise MigrationError(f'{path}: dynamic or unknown WrapPanel orientation must be reviewed.')
            element.tag = 'Container'; element.set('Type','Wrap')
        elif tag == 'DefaultTheme':
            element.tag = 'Theme'; element.set('Name','user_preference' if normalized.get('value','Default') == 'Default' else normalized['value'])
        elif tag in DONOR:
            raise MigrationError(f'{path}: {tag} requires a reviewed canonical Type or specialised adapter; no generic Object conversion was made.')
        elif tag not in VISUAL | NONVISUAL:
            raise MigrationError(f'{path}: custom element {tag} requires a reviewed Definition or genuine registered adapter.')
        for attribute,value in original.items():
            normalized_name = key(attribute)
            if normalized_name in ('canvasleft','canvastop','canvasright','canvasbottom'):
                raise MigrationError(f'{path}: donor absolute coordinate {attribute} needs an explicit CUI coordinate-mode/origin mapping; centred proportional X/Y are not native pixel offsets.')
            if normalized_name == 'orientation' and tag == 'StackPanel':
                element.attrib.pop(attribute,None); continue
            if tag == 'DefaultTheme' and normalized_name == 'value':
                element.attrib.pop(attribute,None); continue
            if normalized_name in ('click','xclass','command','class') and (attribute.startswith('x:') or normalized_name == 'click' or normalized_name == 'command' and tag != 'Action'):
                raise MigrationError(f'{path}: event/code-behind attribute {attribute} needs an explicit typed action adapter.')
            target = ATTRS.get(normalized_name,attribute)
            if normalized_name in ('columns','rows') and (tag == 'Grid' or element.tag == 'Container' and element.get('Type') == 'Grid'):
                # Old native Grid columns/rows always encode definitions. Canonical numeric Rows/Columns remain counts.
                if tag == 'Grid' or not value.isdigit(): target = 'ColumnSizes' if normalized_name == 'columns' else 'RowSizes'
            if normalized_name in ('horizontalscrollbarvisibility','verticalscrollbarvisibility'):
                target = 'HorizontalScrolling' if normalized_name.startswith('horizontal') else 'VerticalScrolling'
                if value not in ('Disabled','Auto','Visible','Hidden'): raise MigrationError(f'{path}: dynamic scrollbar policy requires review.')
                value = 'False' if value == 'Disabled' else 'True'
            if element.tag == 'Input' and normalized_name == 'text': target = 'Value'
            if element.tag == 'Button' and normalized_name == 'header': target = 'Content'
            if target in ('RowSizes','ColumnSizes'): value = sizes(value)
            if target != attribute:
                element.attrib.pop(attribute,None)
                existing = next((v for k,v in element.attrib.items() if key(k) == key(target)),None)
                if existing is not None and existing != value: raise MigrationError(f'{path}: conflicting {attribute}/{target} values cannot be guessed.')
                element.set(target,value)
        if tag == 'ScrollViewer':
            if not any(key(k) == 'verticalscrolling' for k in element.attrib): element.set('VerticalScrolling','True')
            if not any(key(k) == 'horizontalscrolling' for k in element.attrib): element.set('HorizontalScrolling','False')
        if tag in DONOR: notes.append(f'{tag} -> {element.tag}'+(' Type='+element.get('Type') if element.get('Type') else ''))
    # A real Page establishes the application scope. Keep original authored IDs on their controls.
    if root.tag == 'Cui' and not any(node.tag == 'Page' for node in root.iter()):
        host = next((node for node in root if node.tag == 'Theme'),root)
        visual_children = [node for node in host if node.tag in VISUAL or node.tag in ('If','Repeat')]
        if visual_children:
            page = ET.Element('Page')
            first = list(host).index(visual_children[0]); host.insert(first,page)
            for node in visual_children: host.remove(node); page.append(node)
            notes.append('Page scope added; existing IDs and typed actions retained.')
    ET.indent(root,space='  ')
    for node,text,tail in preserved_whitespace: node.text=text; node.tail=tail
    result = ET.tostring(root,encoding='unicode')+'\n'
    return result, notes

def migrate_csharp_raw_strings(source: str, path: str) -> tuple[str,list[str]]:
    notes=[]
    pattern=re.compile(r'(?P<open>"""[^\r\n]*\r?\n)(?P<body>.*?)(?P<indent>^[ \t]*)(?P<close>""")',re.MULTILINE|re.DOTALL)
    def replace(match):
        body=match['body']
        if not body.lstrip().startswith('<Cui'): return match[0]
        converted,changes=migrate(body.strip(),path+' [embedded CUI]'); notes.extend(changes)
        return match['open']+''.join(match['indent']+line+'\n' for line in converted.rstrip().splitlines())+match['indent']+match['close']
    return pattern.sub(replace,source),notes

def tracked_markup() -> list[str]:
    paths = subprocess.check_output(['git','-C',str(ROOT),'ls-files','-z','--cached','--others','--exclude-standard','*.cui','*.axaml','*.hui','*.cui-theme','*.cui-prefab']).decode().split('\0')
    return sorted({p for p in paths if p and not EXCLUDED_PARTS.intersection(Path(p).parts)})

def inventory() -> dict:
    result=[]
    for relative in tracked_markup():
        path=ROOT/relative; text=path.read_text(encoding='utf-8-sig')
        tags=sorted(set(re.findall(r'<\s*([A-Za-z][\w.-]*)\b',text)))
        legacy=sorted(DONOR.intersection(tags))
        custom=sorted(set(tags)-VISUAL-NONVISUAL-DONOR)
        historical=relative in HISTORICAL or any(p.lower() in ('tests','test','fixtures') for p in Path(relative).parts)
        project=[]
        parent=path.parent
        while parent!=ROOT and not project:
            project=[str(p.relative_to(ROOT)).replace('\\','/') for p in parent.glob('*.csproj')]; parent=parent.parent
        required_adapter=relative in {'9to1 Workspace/Data/Cui/DataWorkspace.cui','9to1 Workspace/Mail/UI/MailWorkspace.cui','9to1 Workspace/MiniComputer/MiniComputer.cui',
            '9to1 Workspace/Spaces/UI/Chat/ChatSpace.cui','9to1 Workspace/Spaces/UI/Home/SpacesHome.cui','9to1 Workspace/Terminal/Terminal.cui',
            '9to1 Workspace/Boards/hui/Views/Boards.cui','9to1 Workspace/Boards/hui/Views/Boards.Freeform.cui'}
        result.append({'path':relative,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'projects':project,'tags':tags,
            'legacyTags':legacy,'customTagsRequiringRegistryReview':custom,'classification':'historical-or-test' if historical else 'first-party',
            'status':'review-test-intent' if historical else 'retained-excluded-platform-declaration' if relative in EXCLUDED_PLATFORM_DECLARATIONS else 'legacy-app-input' if path.suffix in ('.axaml','.hui') else 'manual-runtime-adapter-required' if required_adapter else 'legacy-vocabulary' if legacy else 'custom-definition-review' if custom else 'canonical-candidate',
            'runtimeAcceptance':'NOT RUN'})
    return {'specSha256':'7C812DF54C5AD1177559C4D3E0E04EA935DDE3DC02D6D712914BEB55CA44136F','sourceHead':subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD']).decode().strip(),'surfaces':result}

def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths',nargs='*'); parser.add_argument('--apply',action='store_true'); parser.add_argument('--inventory',type=Path); parser.add_argument('--report',type=Path)
    args=parser.parse_args(); report=[]; failed=False
    for name in args.paths:
        path=(ROOT/name).resolve()
        if not path.is_relative_to(ROOT): raise MigrationError('Migration paths must stay within the selected checkout.')
        before=path.read_bytes(); source=before.decode('utf-8-sig')
        try:
            after,notes=migrate_csharp_raw_strings(source,name) if path.suffix == '.cs' else migrate(source,name)
            payload=after.encode('utf-8')
            if before.startswith(b'\xef\xbb\xbf'): payload=b'\xef\xbb\xbf'+payload
            report.append({'path':name,'beforeSha256':hashlib.sha256(before).hexdigest(),'afterSha256':hashlib.sha256(payload).hexdigest(),'changed':payload!=before,'notes':notes,'state':'applied' if args.apply else 'proposed'})
            if args.apply and payload!=before:
                if path.read_bytes()!=before: raise MigrationError(f'{name}: source changed while preparing migration.')
                path.write_bytes(payload)
        except MigrationError as error:
            failed=True; report.append({'path':name,'state':'requires-manual-adapter','diagnostic':str(error)})
    if args.inventory: args.inventory.write_text(json.dumps(inventory(),indent=2)+'\n',encoding='utf-8')
    if args.report: args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    for item in report: print(json.dumps(item))
    return 1 if failed else 0
if __name__=='__main__': raise SystemExit(main())
