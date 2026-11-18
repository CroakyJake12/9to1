from pathlib import Path
import json,re,zipfile
from docx import Document
from docx.shared import Inches,Pt,RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH,WD_BREAK
from docx.enum.table import WD_TABLE_ALIGNMENT,WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.enum.style import WD_STYLE_TYPE

ROOT=Path(__file__).resolve().parents[1]
SPEC=Path(__file__).resolve().parent
OUT=ROOT/'output'
SHOTS=ROOT/'mockups/screens'
M=json.loads((SPEC/'modules.json').read_text())
SCREENS={s['id']:s for s in json.loads((ROOT/'mockups/manifest.json').read_text())}
COVERAGE=json.loads((SPEC/'coverage_report.json').read_text())
OVERRIDES=json.loads((SPEC/'owner_overrides.json').read_text())
GROUPS=[('layout','Layouts and visible states'),('behaviour','Features and behaviour'),('entities','Canonical entities and persistence'),('permissions','Permissions and recovery'),('acceptance','Acceptance checks')]
# Accept the source pack's semantic group labels without duplicating a requirement.
GROUP_NAMES={'layout':'Layouts and visible states','layouts':'Layouts and visible states','features':'Features and behaviour','behaviour':'Features and behaviour','behavior':'Features and behaviour','entities':'Canonical entities and persistence','persistence':'Canonical entities and persistence','permissions':'Permissions and recovery','recovery':'Permissions and recovery','acceptance':'Acceptance checks','verification':'Acceptance checks'}
THEMES=[('professional','Professional'),('glow','Glow'),('playful','Playful'),('frosted','Frosted Glass'),('bubble','Bubble'),('metallic','Metallic'),('retro','Retro'),('western','Western')]
THEME_RULES={
 'professional':'Professional uses restrained dark or light surfaces and the app accent. It is the only current theme that may request Gaussian or Mica material; this reference shows the opaque performance fallback.',
 'glow':'Glow uses luminous accent boundaries and controlled highlights. Keep foreground labels readable and the native app identity stable.',
 'playful':'Playful uses warmer colours, softer corners and expressive accents. Preserve the hierarchy, control meanings and density of the current layout.',
 'frosted':'Frosted Glass uses layered colour, translucent-looking borders and surface highlights. Do not introduce a Gaussian filter under the current material rule.',
 'bubble':'Bubble uses watery depth, glossy rounded controls and dimensional bubbles. Button presses may wobble for 520 ms. Reduced motion replaces the wobble with a stationary pressed state.',
 'metallic':'Metallic uses restrained brushed-looking highlights, fine edges and cool neutral surfaces. Keep focus and selected states distinct from decorative reflections.',
 'retro':'Retro uses terminal-style monospace chrome, ANSI accents, crisp rules and restrained raster effects. Authored document fonts and colours stay unchanged.',
 'western':'Western uses the bundled wood texture on host and selected control surfaces, warm paper tones and sepia chrome icons. It suggests an old film without tinting authored document content or running an expensive continuous filter.'}
bookmark_id=1

def clean(t):
 return str(t).replace('**','').replace('`','').replace('\u00a0',' ')
def title_clean(t):
 return re.sub(r'\s+',' ',re.sub(r'[^\w\s]',' ',str(t))).strip()
def keep(p,value=True):p.paragraph_format.keep_with_next=value

def bookmark(p,name):
 global bookmark_id
 a=OxmlElement('w:bookmarkStart');a.set(qn('w:id'),str(bookmark_id));a.set(qn('w:name'),name)
 b=OxmlElement('w:bookmarkEnd');b.set(qn('w:id'),str(bookmark_id));bookmark_id+=1
 p._p.insert(0,a);p._p.append(b)
def internal_link(p,label,name):
 h=OxmlElement('w:hyperlink');h.set(qn('w:anchor'),name)
 r=OxmlElement('w:r');pr=OxmlElement('w:rPr');c=OxmlElement('w:color');c.set(qn('w:val'),'285E8C');pr.append(c);r.append(pr)
 t=OxmlElement('w:t');t.text=label;r.append(t);h.append(r);p._p.append(h)
def external_link(p,label,url):
 rel=p.part.relate_to(url,'http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink',is_external=True)
 h=OxmlElement('w:hyperlink');h.set(qn('r:id'),rel);r=OxmlElement('w:r');t=OxmlElement('w:t');t.text=label;r.append(t);h.append(r);p._p.append(h)
def field(p,code):
 r=p.add_run();a=OxmlElement('w:fldChar');a.set(qn('w:fldCharType'),'begin');i=OxmlElement('w:instrText');i.text=code;s=OxmlElement('w:fldChar');s.set(qn('w:fldCharType'),'separate');t=OxmlElement('w:t');t.text='1';z=OxmlElement('w:fldChar');z.set(qn('w:fldCharType'),'end')
 for x in (a,i,s,t,z):r._r.append(x)
def setup(subject):
 d=Document();sec=d.sections[0];sec.page_width=Inches(8.5);sec.page_height=Inches(11)
 sec.top_margin=Inches(.66);sec.bottom_margin=Inches(.64);sec.left_margin=Inches(.75);sec.right_margin=Inches(.75)
 sec.header_distance=Inches(.25);sec.footer_distance=Inches(.25)
 styles=d.styles
 for border in list(styles.element.iter(qn('w:pBdr'))):border.getparent().remove(border)
 for name in ('Normal','Body Text','List Bullet','List Number','Title','Subtitle','Heading 1','Heading 2','Heading 3','Caption'):
  styles[name].font.name='Arial';styles[name].font.color.rgb=RGBColor(0,0,0)
 n=styles['Normal'];n.font.size=Pt(11);n.paragraph_format.space_after=Pt(6);n.paragraph_format.line_spacing=1.1
 n.paragraph_format.widow_control=True
 for name,size in [('Title',32),('Heading 1',24),('Heading 2',14),('Heading 3',11.5)]:
  st=styles[name];st.font.size=Pt(size);st.font.bold=True;st.paragraph_format.space_before=Pt(12 if name!='Title' else 0);st.paragraph_format.space_after=Pt(7);st.paragraph_format.keep_with_next=True
  if name=='Heading 1':st.paragraph_format.page_break_before=True
 styles['Subtitle'].font.italic=False;styles['Subtitle'].font.size=Pt(16);styles['Subtitle'].font.color.rgb=RGBColor(50,50,50)
 for name in ('List Bullet','List Number'):
  styles[name].font.size=Pt(11);styles[name].paragraph_format.space_after=Pt(5);styles[name].paragraph_format.line_spacing=1.06;styles[name].paragraph_format.keep_together=True
 for name,size,col in [('Meta',9.2,'59616A'),('Screen Notes',10.4,'222222'),('Source Note',9,'59616A'),('TOC Entry',11,'000000')]:
  st=styles.add_style(name,WD_STYLE_TYPE.PARAGRAPH);st.base_style=styles['Normal'];st.font.name='Arial';st.font.size=Pt(size);st.font.color.rgb=RGBColor.from_string(col);st.paragraph_format.space_after=Pt(5);st.paragraph_format.line_spacing=1.06
 styles['Screen Notes'].paragraph_format.keep_together=True
 styles['TOC Entry'].paragraph_format.space_after=Pt(5)
 styles['Caption'].font.size=Pt(9);styles['Caption'].font.italic=False
 header=sec.header.paragraphs[0];header.style=styles['Meta'];header.add_run('9to1 Development Specification v3')
 footer=sec.footer.paragraphs[0];footer.style=styles['Meta'];footer.alignment=WD_ALIGN_PARAGRAPH.RIGHT;footer.add_run('28 October 2026    ');field(footer,'PAGE')
 d.core_properties.title=subject;d.core_properties.subject='Current visual layouts, features, prerequisites and implementation contracts';d.core_properties.author='9to1';d.core_properties.keywords='9to1, specification, v3, UI, Spaces, Assistants, CUI, Dulche'
 return d

def p(d,t,style=None):return d.add_paragraph(clean(t),style)
def bullet(d,t):return p(d,t,'List Bullet')
def h(d,t,level=2):return d.add_heading(title_clean(t),level)
def labelpara(d,label,text,style=None):
 q=d.add_paragraph(style=style);q.add_run(label+' ').bold=True;q.add_run(clean(text));return q

def start(d,title,subtitle,standalone=False):
 d.add_paragraph(title,'Title');d.add_paragraph(subtitle,'Subtitle')
 p(d,'Version 3   28 October 2026','Meta')
 p(d,'This specification defines the current 9to1 interface and the behaviour that implements it. Each module begins with the foundations it depends on, then describes its layout, features, data ownership, permission boundaries and acceptance checks. The exact mock-ups follow the module they implement.')
 if standalone:
  p(d,'This document contains the Spaces module, its prerequisite contracts and its current reference screens. It can be read independently for Spaces work. The companion module pack supplies the named technical clauses, complete Markdown module and source ledger.')
 else:
  p(d,'Use the contents links or Word navigation pane to open a single module. Copy from that module heading to the next module heading when handing work to an implementation model. The companion module pack also provides a separate current Markdown file and a complete technical file for each module.')
 p(d,'Current owner decisions control the visual specification. Only source presentation slides 1 through 16 are authoritative. The approved older Study concept remains a behaviour reference inside the current wrapper. Later presentation designs do not define the current interface.')
 p(d,'The HTML atlas and these images share the same authored layout source. Desktop references are 1440 by 900 logical pixels; mobile references are 390 by 844. The figures retain their exact proportions. Use the atlas or original PNGs for full-size geometry and theme inspection.')
 p(d,'The mock-ups use illustrative content and state fixtures. Product implementation must supply actual data, durable save and export results, permission decisions and service state. The companion technical clauses remain part of the implementation contract.')
 h(d,'Using the module pack')
 bullet(d,'modules/<module>.md: current copyable requirements, prerequisite contracts, exact state map and technical reading map.')
 bullet(d,'modules/<module>.complete.md: that module with its retained technical clauses collected in one file.')
 bullet(d,'technical/: smaller named source chapters for focused implementation tasks. Load only the chapters required for the work.')
 bullet(d,'catalog/ and history/: source accounting, clause IDs, explicit overrides, and preserved superseded wording.')


def contents(d,mods):
 h(d,'Contents',1);bookmark(d.paragraphs[-1],'contents')
 p(d,'Select a module to jump to it. Shared contracts precede the apps that use them.','Meta')
 for m in mods:
  q=d.add_paragraph(style='TOC Entry');internal_link(q,m['title'],'module_'+m['id'].replace('-','_'))


def authority(d):
 h(d,'Current decisions',1)
 p(d,'The following owner decisions take precedence within their stated scope. They update the current layouts while retaining unrelated technical and data contracts.')
 for o in OVERRIDES:
  label=labelpara(d,o['id'],o['title']);label.paragraph_format.space_after=Pt(2);keep(label)
  p(d,o['rule'])
 h(d,'Source contract',1)
 p(d,'Retained technical requirements come from the expanded V2 and its incorporated Professional, Spaces, Study and scalability documents. Current owner decisions and explicit supersessions control conflicting UI wording. Product acceptance remains separate from this design reference.')
 h(d,'Presentation authority')
 p(d,'Haven AI Generative UI update - Copy(1).pptx supplies the authoritative visual references on slides 1 through 16. The approved older Study concept informs learning behaviour within the current wrapper.')
 h(d,'Retained specification sources')
 source_descriptions={
  'v2':('Expanded Development Specification v2','Product boundaries, shared systems, APIs, storage, donor integration, recovery and acceptance contracts.'),
  'annex':('Professional Present and Appearance annex','Current theme roster, material restrictions, preference ownership and authored-content invariants.'),
  'companion':('Spaces interface companion','Conversation structure, Groups and Projects, Space Builder, source context, voice and run behaviour.'),
  'study':('Study UI and acceptance annex','Study navigation, learning sessions, source policy, evidence-based proficiency and canonical learning artifacts.'),
  'scalability':('Scalable Content annex','Large-content behaviour, incremental rendering and recovery without arbitrary product size caps.')}
 for source in json.loads((SPEC/'catalog/source_manifest.json').read_text()):
  name,description=source_descriptions[source['id']]
  q=labelpara(d,name+'.',description);keep(q)
  q=p(d,source['path']+'    '+format(source['words'],',')+' words','Meta')
  if source.get('url'):
   keep(q);q=d.add_paragraph(style='Meta');external_link(q,'Open incorporated source',source['url'])
 h(d,'Source accounting and focused reading')
 p(d,'The companion pack preserves all five source documents and accounts for every nonblank source line. The source ledger records original text, active text, destination and amendment status. Superseded wording and historical process directions remain separate from current product rules.')
 p(d,'Start with the app module, then load the technical chapters named for the task. The catalog supplies complete clause IDs and source digests. This retains implementation detail while keeping the current app contract independently readable.')


def module(d,m,standalone=False):
 h(d,m['title'],1);bookmark(d.paragraphs[-1],'module_'+m['id'].replace('-','_'))
 p(d,'Module '+m['id']+'    Current contract and exact reference designs','Meta')
 h(d,'Prerequisites')
 deps=m.get('prerequisite_contracts',[])
 if deps:
  for dep in deps:labelpara(d,dep['title']+'.',dep.get('contract','Use its canonical contract.'))
 else:p(d,'This module establishes a shared foundation. Its contract can be implemented before dependent product interfaces.')
 h(d,'Overview');p(d,m['overview'])
 if m.get('owner_overrides'):p(d,'Current overrides: '+', '.join(m['owner_overrides'])+'.','Meta')
 for key,reqs in m.get('requirements',{}).items():
  if not reqs:continue
  h(d,GROUP_NAMES.get(key,key.replace('_',' ').title()))
  for n,r in enumerate(reqs,1):
   q=d.add_paragraph(style='List Bullet');rr=q.add_run(f'V3-{m["id"].upper()}-{key[:3].upper()}-{n:02d}  ');rr.bold=True;rr.font.size=Pt(9.5);q.add_run(clean(r))
 h(d,'Exact design states')
 ids=[i for i in m.get('screen_ids',[]) if i in SCREENS]
 if ids:
  p(d,'The following figures are the current reference for this module. Their interface geometry and notes are part of this contract. Each figure has a stable screen ID shared with the atlas and source pack.')
  for sid in ids:
   q=d.add_paragraph(style='Meta');internal_link(q,SCREENS[sid]['title']+'  ['+sid+']','screen_'+sid.replace('-','_'))
 else:p(d,'Dependent app screens exercise this shared contract. It does not create another standalone app.')
 h(d,'Technical reading map')
 chunks=m.get('technical_chunks',[])
 if chunks:
  p(d,'Use these smaller chapters for detailed APIs, data models, donor integration, platform and recovery requirements. The complete Markdown module includes them in source order.')
  for c in chunks:
   label=c.get('title',c['id']).replace(' — retained technical requirements','')
   # Boards has seven reading-map entries. Avoid repeating the module title
   # on every entry so its source excerpt remains on this contract page.
   if m['id']=='boards' and label.startswith(m['title']+' — '):
    label=label[len(m['title']+' — '):]
   labelpara(d,label+'.',c['path']+' ('+format(c['words'],',')+' words).','Meta')
 else:p(d,'This module adds current shared rules and exact visual contracts. Its prerequisite modules supply the underlying implementation interfaces.')
 refs=m.get('source_refs',[])
 if refs:
  # Full inventories live in the module and ledger. Compact excerpts prevent
  # reference-only spill pages immediately before these modules' figures.
  excerpt_limit=8 if m['id'] in ('cui','present','boards') else 24
  excerpt=', '.join(refs[:excerpt_limit])+(' and '+str(len(refs)-excerpt_limit)+' additional IDs in the module source and source ledger.' if len(refs)>excerpt_limit else '.')
  if m['id']=='boards':labelpara(d,'Source clause IDs:',excerpt,'Source Note')
  else:
   h(d,'Source clause IDs')
   p(d,excerpt,'Source Note')
 for sid in ids:screen(d,SCREENS[sid],m['title'])
 if m['id']=='cui':theme_figures(d)


def screen(d,s,module_title):
 q=h(d,s['title'],2);q.paragraph_format.page_break_before=True;q.paragraph_format.space_before=Pt(0);bookmark(q,'screen_'+s['id'].replace('-','_'))
 theme=dict(THEMES).get(s.get('previewTheme','professional'),'Professional')
 dimensions=s.get('size',{'w':1440,'h':900})
 p(d,f'{s["id"]}    {theme} dark    {dimensions["w"]} by {dimensions["h"]} logical pixels','Meta')
 q=d.add_paragraph();q.paragraph_format.space_after=Pt(5)
 if dimensions['w']<600:
  q.alignment=WD_ALIGN_PARAGRAPH.CENTER;width=2.45
 else:width=7.0
 shape=q.add_run().add_picture(str(SHOTS/(s['id']+'.png')),width=Inches(width))
 shape._inline.docPr.set('descr',s['title']+'. '+s['notes'][0])
 p(d,'Exact reference composition from the offline atlas.','Caption')
 h(d,'Implementation rules',3)
 for n,note in enumerate(s['notes'],1):
  q=d.add_paragraph(style='Screen Notes');q.add_run(str(n)+'. ').bold=True;q.add_run(clean(note))
 p(d,'Source: '+s['source'],'Source Note')


def theme_figures(d):
 for key,name in THEMES:
  q=h(d,name+' theme across the editor',2);q.paragraph_format.page_break_before=True
  p(d,'The same Present document and geometry in '+name+'.','Meta')
  q=d.add_paragraph();sh=q.add_run().add_picture(str(SHOTS/('theme-present-'+key+'.png')),width=Inches(7));sh._inline.docPr.set('descr',name+' theme applied to Present chrome while the slide content is unchanged.')
  p(d,THEME_RULES[key])
  p(d,'All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.')
  p(d,'Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.','Source Note')


def create_all():
 d=setup('9to1 Development Specification v3')
 start(d,'9to1 Development Specification','Current layouts and implementation contracts')
 contents(d,M);authority(d)
 for m in M:module(d,m)
 path=OUT/'9to1_Development_Specification_v3.docx';d.save(path)
 sp=next(m for m in M if m['id']=='spaces');d=setup('9to1 Spaces Specification v3')
 start(d,'9to1 Spaces Specification','Standalone module and exact reference screens',True)
 module(d,sp,True)
 path2=OUT/'9to1_Spaces_Specification.docx';d.save(path2)
 for x in (path,path2):
  with zipfile.ZipFile(x) as z:
   xml=z.read('word/document.xml').decode();print(json.dumps({'file':str(x),'bytes':x.stat().st_size,'pictures':xml.count('<pic:pic>'),'headings':xml.count('w:val="Heading'),'explicit_page_breaks':xml.count('w:type="page"')}))

if __name__=='__main__':create_all()
