#!/usr/bin/env python3
"""Export the final native Docs readback and assemble the portable 9to1 handoff.
No document content is reconstructed from earlier drafts or addition files.
Run only after doc-final.json is the final verified native document readback.
"""
from __future__ import annotations
import argparse, collections, datetime, hashlib, html, json, re, shutil, tempfile, zipfile
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

ROOT = Path(__file__).resolve().parents[2]
FINISH = ROOT / 'spec-work/finish'
PACKAGE_NAME = '9to1_Qwen_3_8_Handoff_2026-11-18'
ATLAS_NAME = '9to1_Interface_Atlas_v3.3_Editable.html'
ATLAS_FILE_ID = 'libfile_f795e6451ce081918985cc8ca205faf2'

def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))

def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''): h.update(block)
    return h.hexdigest()

def unwrap_document(data):
    for _ in range(6):
        if isinstance(data, dict) and ('tabs' in data or 'body' in data): return data
        if not isinstance(data, dict): break
        next_data = next((data[k] for k in ('structuredContent','document','doc','result') if isinstance(data.get(k),dict)), None)
        if next_data is None: break
        data = next_data
    raise ValueError('No native document tabs/body found in final readback')

def get_tabs(doc):
    result = []
    def visit(items):
        for i, tab in enumerate(items):
            props = tab.get('tabProperties', {})
            body = tab.get('body', tab.get('documentTab', {}).get('body', {}))
            result.append({'id':tab.get('id',tab.get('tabId',props.get('tabId',f'tab-{len(result)+1}'))),
                           'title':tab.get('title',props.get('title',f'Tab {len(result)+1}')),
                           'body':body, 'raw':tab})
            visit(tab.get('childTabs', []))
    if doc.get('tabs'): visit(doc['tabs'])
    elif doc.get('body'): result.append({'id':'t.0','title':doc.get('title','Specification'),'body':doc['body'],'raw':doc})
    return result

def walk_blocks(blocks):
    for block in blocks:
        yield block
        if 'table' in block:
            for row in block['table'].get('tableRows', []):
                for cell in row.get('tableCells', []): yield from walk_blocks(cell.get('content', []))
        if 'tableOfContents' in block: yield from walk_blocks(block['tableOfContents'].get('content', []))

def plain_paragraph(paragraph):
    return ''.join(x.get('textRun',{}).get('content','') for x in paragraph.get('elements', []))

def safe_anchor(tab, heading=None):
    return str(tab) + ('--' + str(heading) if heading else '')

def md_text(text):
    return re.sub(r'([\\`*\[\]<>_])', r'\\\1', text)

class Exporter:
    def __init__(self, doc, tabs, screens, stage, heading_map=None):
        self.doc, self.tabs, self.screens, self.stage = doc, tabs, screens, stage
        self.doc_id = doc.get('documentId','')
        self.headings = {}
        self.heading_tabs = collections.defaultdict(list)
        self.warnings = []
        self.counts = collections.Counter()
        for tab in tabs:
            for block in walk_blocks(tab['body'].get('content', [])):
                para=block.get('paragraph',{}); style=para.get('paragraphStyle',{})
                hid=style.get('headingId') or para.get('headingId')
                if hid:
                    self.headings[(tab['id'], hid)] = safe_anchor(tab['id'], hid)
                    self.heading_tabs[hid].append(tab['id'])
        # Native readback is authoritative; a supplied map only fills missing IDs.
        for h in (heading_map or {}).get('headings', []):
            tid=h.get('tab',h.get('tabId')); hid=h.get('id',h.get('headingId'))
            if tid and hid and (tid,hid) not in self.headings:
                self.headings[(tid,hid)]=safe_anchor(tid,hid)
                self.heading_tabs[hid].append(tid)

    def target_heading(self, hid, tid, current):
        if not hid: return '#' + safe_anchor(tid or current)
        chosen=tid or (current if (current,hid) in self.headings else None)
        if not chosen and len(set(self.heading_tabs.get(hid,[])))==1: chosen=self.heading_tabs[hid][0]
        if chosen and (chosen,hid) in self.headings: return '#' + self.headings[(chosen,hid)]
        self.warnings.append({'kind':'unresolved-heading','headingId':hid,'tabId':tid,'currentTab':current})
        return None

    def rewrite_link(self, link, label, current):
        visible=label.strip().strip('`')
        if visible.startswith('baseline/') and (self.stage/visible).is_file(): return visible
        if not link: return None
        if 'headingId' in link or 'tabId' in link:
            target=self.target_heading(link.get('headingId'),link.get('tabId'),current)
            if target: return target
        url=link.get('url','')
        if not url: return None
        parsed=urlparse(url); query=parse_qs(parsed.query); frag=unquote(parsed.fragment)
        fquery=parse_qs(frag)
        screen=(query.get('screen') or fquery.get('screen') or [None])[0]
        if not screen and frag in self.screens: screen=frag
        is_atlas=(ATLAS_FILE_ID in url or 'atlas' in parsed.path.lower())
        if not screen and is_atlas and visible in self.screens: screen=visible
        if screen in self.screens: return 'design/'+ATLAS_NAME+'#'+screen
        if is_atlas: return 'design/'+ATLAS_NAME
        is_same_doc=('docs.google.com/document/' in url and (not self.doc_id or self.doc_id in url))
        if is_same_doc:
            hid=(query.get('heading') or fquery.get('heading') or [None])[0]
            tid=(query.get('tab') or fquery.get('tab') or [None])[0]
            target=self.target_heading(hid,tid,current)
            if target: return target
        return url

    def inline(self, para, tab):
        md=[]; ht=[]; elements=para.get('elements',[])
        for idx, element in enumerate(elements):
            if 'textRun' in element:
                run=element['textRun']; text=run.get('content','')
                if idx==len(elements)-1: text=text.removesuffix('\n')
                style=run.get('textStyle',{}); m=md_text(text); h=html.escape(text).replace('\n','<br>\n')
                if style.get('bold'): m='**'+m+'**'; h='<strong>'+h+'</strong>'
                if style.get('italic'): m='*'+m+'*'; h='<em>'+h+'</em>'
                if style.get('strikethrough'): m='~~'+m+'~~'; h='<s>'+h+'</s>'
                if style.get('underline'): h='<u>'+h+'</u>'
                link=self.rewrite_link(style.get('link'),text,tab)
                if link: m='['+m+']('+link+')'; h='<a href="'+html.escape(link,quote=True)+'">'+h+'</a>'
                md.append(m); ht.append(h)
            elif 'person' in element:
                props=element['person'].get('personProperties',{}); text=props.get('name',props.get('email','Person'))
                md.append(md_text(text)); ht.append(html.escape(text)); self.warnings.append({'kind':'person-inline','tabId':tab})
            elif 'richLink' in element:
                props=element['richLink'].get('richLinkProperties',{}); text=props.get('title',props.get('uri','Link'))
                url=self.rewrite_link({'url':props.get('uri','')},text,tab)
                md.append('['+md_text(text)+']('+url+')' if url else md_text(text))
                ht.append('<a href="'+html.escape(url,quote=True)+'">'+html.escape(text)+'</a>' if url else html.escape(text))
            elif 'footnoteReference' in element:
                ident=element['footnoteReference'].get('footnoteId','unknown'); md.append('[^'+ident+']'); ht.append('<sup>'+html.escape(ident)+'</sup>')
            elif any(k in element for k in ['inlineObjectElement','equation','pageBreak','horizontalRule']):
                if 'horizontalRule' in element: md.append('\n---\n'); ht.append('<hr>')
                elif 'pageBreak' in element: ht.append('<span class="page-break"></span>')
                else:
                    self.warnings.append({'kind':'nontext-inline-retained-in-json','tabId':tab,'element':element})
                    md.append('[Non-text object: see native source JSON]'); ht.append('<span>[Non-text object: see native source JSON]</span>')
        return ''.join(md), ''.join(ht)

    def blocks(self, blocks, tab):
        md=[]; ht=[]
        for block in blocks:
            if 'paragraph' in block:
                para=block['paragraph']; self.counts['paragraphs']+=1
                m,h=self.inline(para,tab); style=para.get('paragraphStyle',{}); named=style.get('namedStyleType','NORMAL_TEXT')
                hid=style.get('headingId') or para.get('headingId'); anchor=safe_anchor(tab,hid) if hid else None
                if anchor: md.append('<a id="'+html.escape(anchor,quote=True)+'"></a>'); ht.append('<span id="'+html.escape(anchor,quote=True)+'"></span>')
                match=re.fullmatch(r'HEADING_([1-6])',named)
                if match:
                    level=int(match.group(1)); md.append('#'*level+' '+m); ht.append(f'<h{level}>'+h+f'</h{level}>')
                elif named=='TITLE': md.append('# '+m); ht.append('<h1>'+h+'</h1>')
                elif para.get('bullet'):
                    level=para['bullet'].get('nestingLevel',0); md.append('  '*level+'- '+m)
                    ht.append('<ul class="native-list" style="margin-left:'+str(level*1.25)+'rem"><li>'+h+'</li></ul>')
                elif m or h: md.append(m); ht.append('<p>'+h+'</p>')
                else: md.append(''); ht.append('<p class="blank"></p>')
            elif 'table' in block:
                self.counts['tables']+=1; rows=[]
                for row in block['table'].get('tableRows',[]):
                    cells=[]
                    for cell in row.get('tableCells',[]):
                        cm,ch=self.blocks(cell.get('content',[]),tab); cs=cell.get('tableCellStyle',{})
                        attr=''
                        for key,htmlkey in [('columnSpan','colspan'),('rowSpan','rowspan')]:
                            if cs.get(key,1)>1:attr+=' '+htmlkey+'="'+str(cs[key])+'"'
                        cells.append('<td'+attr+'>'+ch+'</td>')
                    rows.append('<tr>'+''.join(cells)+'</tr>')
                table='<table><tbody>'+''.join(rows)+'</tbody></table>'
                # Raw semantic HTML tables are valid Markdown and preserve paragraphs/links/spans faithfully.
                md.append(table); ht.append('<div class="table-wrap">'+table+'</div>')
            elif 'tableOfContents' in block:
                cm,ch=self.blocks(block['tableOfContents'].get('content',[]),tab);md.append(cm);ht.append('<nav>'+ch+'</nav>')
            elif 'sectionBreak' not in block:
                self.warnings.append({'kind':'unknown-block-retained-in-json','tabId':tab,'keys':list(block)})
        return '\n\n'.join(md), '\n'.join(ht)

    def render(self):
        md=[]; ht=[]; nav=[]
        for tab in self.tabs:
            tid=tab['id']; title=tab['title']; anchor=safe_anchor(tid)
            nav.append('<a href="#'+html.escape(anchor,quote=True)+'">'+html.escape(title)+'</a>')
            md += ['<a id="'+html.escape(anchor,quote=True)+'"></a>','# '+md_text(title)]
            ht.append('<section id="'+html.escape(anchor,quote=True)+'"><h1 class="tab-title">'+html.escape(title)+'</h1>')
            m,h=self.blocks(tab['body'].get('content',[]),tid);md.append(m);ht += [h,'</section>']
        footnotes=self.doc.get('footnotes',{})
        if footnotes:
            md.append('## Footnotes');ht.append('<section><h2>Footnotes</h2>')
            for ident,foot in footnotes.items():
                m,h=self.blocks(foot.get('content',[]),self.tabs[0]['id']);md.append('[^'+ident+']: '+m);ht.append('<h3>'+html.escape(ident)+'</h3>'+h)
            ht.append('</section>')
        css='''body{margin:0;background:#131416;color:#eceef4;font:16px/1.65 system-ui,sans-serif}a{color:#91b0ff}nav.toc{padding:1.4rem 2rem;background:#202228;display:flex;gap:.7rem 1.3rem;flex-wrap:wrap}main{max-width:1100px;margin:auto;padding:2rem}h1,h2,h3,h4,h5,h6{line-height:1.25;scroll-margin-top:1rem}section{margin-bottom:4rem}.tab-title{border-bottom:2px solid #315dff;padding-bottom:.6rem}table{border-collapse:collapse;width:100%;margin:1rem 0}td,th{border:1px solid #4a4e59;padding:.6rem;vertical-align:top}td p{margin:.2rem 0}.table-wrap{overflow:auto}.native-list{margin-top:.1rem;margin-bottom:.1rem}.blank{min-height:.25rem}code{overflow-wrap:anywhere}p,a{overflow-wrap:anywhere}@media(max-width:650px){main{padding:1rem}nav.toc{padding:1rem}table{font-size:.9rem}}@media print{body{background:white;color:black}a{color:#1641ad}nav.toc{display:none}main{max-width:none}td{border-color:#aaa}}'''
        title=self.doc.get('title','9to1 Development Specification')
        full='<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>'+html.escape(title)+'</title><style>'+css+'</style></head><body><nav class="toc" aria-label="Document tabs">'+''.join(nav)+'</nav><main>'+''.join(ht)+'</main></body></html>'
        return '\n\n'.join(md)+'\n', full


def localize_atlas_references(atlas_bytes, native_anchors, expected_sections=62):
    """Localise only the specification JSON value; preserve every other byte."""
    marker=re.search(rb'<script\b[^>]*\bid=["\']atlas-specification-links-code["\'][^>]*>',atlas_bytes)
    if not marker:raise ValueError('Atlas specification-links module is missing')
    script_end=atlas_bytes.find(b'</script>',marker.end())
    if script_end<0:raise ValueError('Atlas specification-links script is unterminated')
    body=atlas_bytes[marker.end():script_end]
    assignment=re.search(rb'\bconst\s+specification\s*=\s*',body)
    if not assignment:raise ValueError('Atlas specification JSON assignment is missing')
    start=marker.end()+assignment.end()
    value_text=atlas_bytes[start:script_end].decode('utf-8')
    specification, chars=json.JSONDecoder().raw_decode(value_text)
    end=start+len(value_text[:chars].encode('utf-8'))
    original=json.loads(json.dumps(specification))
    sections=specification.get('sections',{})
    if len(sections)!=expected_sections:
        raise ValueError(f'Expected {expected_sections} current Atlas specification sections, found {len(sections)}')
    targets=[]
    for key,section in sections.items():
        if not section.get('tabId') or not section.get('headingId'):
            raise ValueError('Atlas section lacks a native tab/heading target: '+key)
        anchor=safe_anchor(section['tabId'],section['headingId'])
        if anchor not in native_anchors:
            raise ValueError('Atlas section has no emitted native heading target: '+key+' -> '+anchor)
        section['url']='../SPECIFICATION.html#'+anchor
        targets.append({'section':key,'anchor':anchor,'url':section['url']})
    specification['documentUrl']='../SPECIFICATION.html'
    # Guard against accidentally changing labels, IDs, route mappings or other JSON fields.
    permitted=json.loads(json.dumps(original))
    permitted['documentUrl']=specification['documentUrl']
    for key in sections:permitted['sections'][key]['url']=sections[key]['url']
    if permitted!=specification:raise ValueError('Unexpected specification JSON mutation')
    replacement=json.dumps(specification,ensure_ascii=False,separators=(',',':')).encode('utf-8')
    localized=atlas_bytes[:start]+replacement+atlas_bytes[end:]
    outside_preserved=(localized[:start]==atlas_bytes[:start] and localized[start+len(replacement):]==atlas_bytes[end:])
    if not outside_preserved:raise ValueError('Atlas bytes outside specification JSON changed')
    report={'sourceAtlasSha256':hashlib.sha256(atlas_bytes).hexdigest(),
            'portableAtlasSha256':hashlib.sha256(localized).hexdigest(),
            'localizedModule':'atlas-specification-links-code','changedValue':'const specification JSON only',
            'sectionCount':len(sections),'nativeHeadingTargetsVerified':len(targets),
            'allOtherBytesPreserved':outside_preserved,'rootAtlasModified':False,'targets':targets}
    return localized,report

def copy_tree_verified(src,dest):
    if not src.is_dir():raise FileNotFoundError(src)
    shutil.copytree(src,dest)
    source_files=[p for p in src.rglob('*') if p.is_file()]
    for p in source_files:
        if digest(p)!=digest(dest/p.relative_to(src)):raise ValueError('Source copy mismatch: '+str(p))
    return len(source_files)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--doc',type=Path,default=FINISH/'doc-final.json')
    parser.add_argument('--headings',type=Path)
    parser.add_argument('--output',type=Path,default=ROOT/PACKAGE_NAME)
    parser.add_argument('--zip',dest='zip_path',type=Path,default=ROOT/(PACKAGE_NAME+'.zip'))
    parser.add_argument('--min-tabs',type=int,default=10)
    parser.add_argument('--overwrite',action='store_true')
    args=parser.parse_args()
    required=[args.doc,FINISH/'source-coverage-additions.json',ROOT/ATLAS_NAME,FINISH/'sources/9to1_Development_Specification_v3.1.docx',FINISH/'source-v31.txt']
    required += [FINISH/n for n in ['START_HERE.md','SOURCE_PRECEDENCE.md','QWEN_STARTER_PROMPT.md']]
    missing=[str(p) for p in required if not p.is_file()]
    if missing:raise SystemExit('Required final inputs missing; no assembly performed:\n'+'\n'.join(missing))
    doc=unwrap_document(read_json(args.doc));tabs=get_tabs(doc)
    if len(tabs)<args.min_tabs:raise SystemExit(f'Expected at least {args.min_tabs} tabs; found {len(tabs)}')
    if any(not any(plain_paragraph(b.get('paragraph',{})).strip() or 'table' in b for b in t['body'].get('content',[])) for t in tabs):
        raise SystemExit('A final document tab is empty; complete native readback before assembly')
    if args.output.exists() and (not args.overwrite or not (args.output/'PACKAGE_MANIFEST.json').is_file()):
        raise SystemExit('Output exists. --overwrite is allowed only for a previously assembled package.')
    stage=Path(tempfile.mkdtemp(prefix='.9to1-handoff-build-',dir=ROOT))
    try:
        (stage/'baseline').mkdir();(stage/'design').mkdir();(stage/'source').mkdir();(stage/'validation').mkdir()
        retained_count=copy_tree_verified(FINISH/'retained-pack',stage/'baseline/retained-pack')
        v31_count=copy_tree_verified(FINISH/'v31-retained',stage/'baseline/v31')
        shutil.copy2(required[3],stage/'baseline'/required[3].name)
        shutil.copy2(FINISH/'source-v31.txt',stage/'baseline/9to1_Development_Specification_v3.1.txt')
        shutil.copy2(ROOT/ATLAS_NAME,stage/'design'/ATLAS_NAME)
        shutil.copy2(args.doc,stage/'source/doc-final.json')
        shutil.copy2(FINISH/'source-v31-outline.json',stage/'source/source-v31-outline.json')
        shutil.copy2(FINISH/'source-coverage-additions.json',stage/'source/SCREEN_AND_SOURCE_INDEX.json')
        shutil.copy2(ROOT/'spec-work/screens-current.json',stage/'design/screens-current.json')
        for name in ['START_HERE.md','SOURCE_PRECEDENCE.md','QWEN_STARTER_PROMPT.md','build_portable.py']:
            shutil.copy2(FINISH/name,stage/name)
        for path in sorted((ROOT/'qa').glob('*report.json')):shutil.copy2(path,stage/'validation'/path.name)
        for name in ['architecture-validation.json','retained-source-verification.json']:
            if (FINISH/name).is_file():shutil.copy2(FINISH/name,stage/'validation'/name)
        screens={s['id'] for s in read_json(ROOT/'spec-work/screens-current.json')}
        headings=read_json(args.headings) if args.headings else None
        exporter=Exporter(doc,tabs,screens,stage,headings)
        md,ht=exporter.render()
        (stage/'SPECIFICATION.md').write_text(md,encoding='utf-8')
        (stage/'SPECIFICATION.html').write_text(ht,encoding='utf-8')
        source_atlas_bytes=(ROOT/ATLAS_NAME).read_bytes()
        native_anchors=set(re.findall(r'id="([^"]+)"',ht))
        portable_atlas_bytes,localization=localize_atlas_references(source_atlas_bytes,native_anchors)
        (stage/'design'/ATLAS_NAME).write_bytes(portable_atlas_bytes)
        if (ROOT/ATLAS_NAME).read_bytes()!=source_atlas_bytes:raise ValueError('Source Atlas changed during localization')
        (stage/'validation/atlas-localization-report.json').write_text(json.dumps(localization,ensure_ascii=False,indent=2)+'\n')
        heading_rows=[{'tabId':tid,'headingId':hid,'anchor':anchor} for (tid,hid),anchor in exporter.headings.items()]
        (stage/'source/heading-map.json').write_text(json.dumps(heading_rows,ensure_ascii=False,indent=2)+'\n')
        # All paragraph text stays available losslessly in the native JSON; table HTML uses the same renderer.
        report={'source':'final native Google Docs readback only','tabs':len(tabs),'counts':dict(exporter.counts),'warnings':exporter.warnings,'retainedFilesCopiedUnchanged':retained_count,'v31FilesCopiedUnchanged':v31_count,'renderedBrowserReview':False}
        (stage/'validation/export-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
        manifest={'package':PACKAGE_NAME,'assembledAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'documentId':doc.get('documentId'),'revisionId':doc.get('revisionId'),'documentSource':'source/doc-final.json','specification':['SPECIFICATION.md','SPECIFICATION.html'],'atlas':'design/'+ATLAS_NAME,'atlasSha256':digest(stage/'design'/ATLAS_NAME),'sourceAtlasSha256':localization['sourceAtlasSha256'],'portableAtlasSha256':localization['portableAtlasSha256'],'atlasLocalizationReport':'validation/atlas-localization-report.json','sourcePrecedence':'SOURCE_PRECEDENCE.md','retainedPack':'baseline/retained-pack','v31':'baseline/v31','screenAndSourceIndex':'source/SCREEN_AND_SOURCE_INDEX.json','scope':'Current specification and mockups plus unchanged retained product sources; unrelated tutoring kit excluded.','exportReport':'validation/export-report.json'}
        (stage/'PACKAGE_MANIFEST.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
        hashes={str(p.relative_to(stage)).replace('\\','/'):digest(p) for p in sorted(stage.rglob('*')) if p.is_file()}
        (stage/'CHECKSUMS.json').write_text(json.dumps(hashes,ensure_ascii=False,indent=2)+'\n')
        if args.output.exists():shutil.rmtree(args.output)
        stage.rename(args.output)
        with zipfile.ZipFile(args.zip_path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for p in sorted(args.output.rglob('*')):
                if p.is_file():z.write(p,Path(args.output.name)/p.relative_to(args.output))
        zip_hash=digest(args.zip_path)
        Path(str(args.zip_path)+'.sha256').write_text(zip_hash+'  '+args.zip_path.name+'\n')
        print(json.dumps({'output':str(args.output),'zip':str(args.zip_path),'zipSha256':zip_hash,'files':len(hashes)+1,'exportReport':report},ensure_ascii=False,indent=2))
    finally:
        if stage.exists():shutil.rmtree(stage)

if __name__=='__main__':main()
