"""Source minima only; actual full discovery/TRX must independently execute every case."""
import re

def masked(source):
    result=list(source); i=0; length=len(source)
    def erase(start,end):
        for j in range(start,end):
            if result[j] not in '\r\n': result[j]=' '
    while i<length:
        start=i
        if source.startswith('//',i):
            end=source.find('\n',i); i=length if end<0 else end; erase(start,i)
        elif source.startswith('/*',i):
            end=source.find('*/',i+2)
            if end<0: raise ValueError('Unterminated source comment')
            i=end+2; erase(start,i)
        elif source[i] in ('"',"'") or source.startswith('@"',i):
            verbatim=source.startswith('@"',i); quote='"' if verbatim else source[i]
            raw=0
            if quote=='"' and not verbatim:
                while i+raw<length and source[i+raw]=='"': raw+=1
            if raw>=3:
                end=source.find('"'*raw,i+raw)
                if end<0: raise ValueError('Unterminated raw source string')
                i=end+raw
            else:
                i+=2 if verbatim else 1
                while i<length:
                    if source[i]==quote:
                        if verbatim and i+1<length and source[i+1]==quote: i+=2; continue
                        i+=1; break
                    if not verbatim and source[i]=='\\': i+=2
                    else: i+=1
                else: raise ValueError('Unterminated source string')
            erase(start,i)
        else: i+=1
    return ''.join(result)

def fixture_inventory(source):
    code=masked(source)
    namespaces=re.findall(r'\bnamespace\s+([A-Za-z_][\w.]*)\s*[;{]',code)
    classes=re.findall(r'\bclass\s+([A-Za-z_]\w*)',code)
    if len(namespaces)!=1 or not classes: raise ValueError('Exact single test namespace/class required')
    pattern=re.compile(r'((?:\s*\[[^]\n]*\]\s*)+)public\s+(?:(?:async|static|new|virtual|override|sealed)\s+)*(?:Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?|void)\s+([A-Za-z_]\w*)\s*\(',re.M)
    rows=[]
    for match in pattern.finditer(code):
        attrs=match.group(1)
        kinds=re.findall(r'\[\s*(?:[\w.]+\.)?(Fact|Theory)(?:Attribute)?\b',attrs)
        if not kinds: continue
        if len(kinds)!=1: raise ValueError('Ambiguous original test kind')
        minimum=1 if kinds[0]=='Fact' else max(1,len(re.findall(r'\[\s*(?:[\w.]+\.)?InlineData(?:Attribute)?\b',attrs)))
        rows.append({'fullClass':namespaces[0]+'.'+classes[0],'method':match.group(2),'minimumPassedCases':minimum,'kind':kinds[0]})
    attributed=len(re.findall(r'\[\s*(?:[\w.]+\.)?(?:Fact|Theory)(?:Attribute)?\b',code))
    if len(rows)!=attributed or not rows: raise ValueError('Whole attributed test inventory incomplete')
    return rows
