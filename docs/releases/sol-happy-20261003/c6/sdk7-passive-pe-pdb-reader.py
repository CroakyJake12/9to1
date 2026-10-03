from pathlib import Path
import struct,uuid,zlib,json,hashlib
r=Path('/workspace/team-c/c3-admin-symbol-errors-validation');ev=r/'artifacts/desktop-visible-owning';sha=lambda z:hashlib.sha256(z).hexdigest()
def u32(z,o):return struct.unpack_from('<I',z,o)[0]
def pdbid(z):
 assert z[:4]==b'BSJB';pos=(16+u32(z,12)+3)&~3;n=struct.unpack_from('<H',z,pos+2)[0];pos+=4
 for _ in range(n):
  off,size=struct.unpack_from('<II',z,pos);pos+=8;end=z.index(b'\0',pos);name=z[pos:end];pos=(end+4)&~3
  if name==b'#Pdb':return z[off:off+20].hex()
 raise ValueError('Missing#Pdb')
def pe_debug(z):
 pe=u32(z,60);assert z[pe:pe+4]==b'PE\0\0';coff=pe+4;n=struct.unpack_from('<H',z,coff+2)[0];sz=struct.unpack_from('<H',z,coff+16)[0];opt=coff+20;magic=struct.unpack_from('<H',z,opt)[0];dd=opt+(112 if magic==0x20b else 96);rva,dsz=struct.unpack_from('<II',z,dd+6*8);sec=opt+sz
 def physical(v):
  for i in range(n):
   ss=sec+i*40;virt,va,rs,rp=struct.unpack_from('<IIII',z,ss+8)
   if va<=v<va+max(virt,rs):return rp+v-va
  raise ValueError('RVA outsidesections')
 at=physical(rva);res=[]
 for o in range(at,at+dsz,28):
  flags,stamp,maj,minr,typ,size,addr,ptr=struct.unpack_from('<IIHHIIII',z,o);res.append((typ,stamp,ptr,z[ptr:ptr+size]))
 return res
checks=[];errors=[];docs=0;inputs=0;paired=0
for config in ['debug','release']:
 a=json.loads((ev/config/'complete-compiled-source-pairs.json').read_text());ids=0;sourceerrors=[]
 for p in a:
  bins={q['path']:q for q in p['pairs']};peitem=next(q for q in p['pairs']if q['path'].endswith('.dll'));pe=(r/peitem['path']).read_bytes();assert sha(pe)==peitem['sha256'] and len(pe)==peitem['bytes'];debug=pe_debug(pe);cv=next(t for t in debug if t[0]==2);assert cv[3][:4]==b'RSDS';cvGuid=str(uuid.UUID(bytes_le=cv[3][4:20]));assert cvGuid==p['identity']['codeViewGuid'] and cv[1]==p['identity']['codeViewStamp']
  if p['symbols']['kind']=='embedded-portable-pdb':
   e=next(t for t in debug if t[0]==17);assert e[3][:4]==b'MPDB';assert e[2]==p['symbols']['origin']['debugRecordOffset'];assert sha(e[3])==p['symbols']['origin']['debugRecordSha256'];symbol=zlib.decompress(e[3][8:],-15);assert len(symbol)==u32(e[3],4)
  else:
   pp=next(q for q in p['pairs']if q['path'].endswith('.pdb'));symbol=(r/pp['path']).read_bytes();assert sha(symbol)==pp['sha256']
  assert len(symbol)==p['symbols']['bytes'] and sha(symbol)==p['symbols']['sha256'];actual=pdbid(symbol);assert actual==p['identity']['portablePdbId'];assert str(uuid.UUID(bytes_le=bytes.fromhex(actual[:32])))==cvGuid and int.from_bytes(bytes.fromhex(actual[32:40]),'little')==cv[1];ids+=1;paired+=1
  for doc in p['allPdbDocuments']:
   f=Path(doc['document']);z=f.read_bytes();assert hashlib.new(doc['hashName'],z).hexdigest()==doc['digest'];docs+=1
  inputs+=len(p['compileInputs'])
 checks.append({'configuration':config,'compiledProjects':len(a),'physicalPECodeViewPortablePdbIdentitiesVerified':ids,'allPhysicalSourceDocumentHashesVerified':True})
print(json.dumps({'configurations':checks,'physicalPairs':paired,'physicalDocumentOccurrences':docs,'compileInputOccurrences':inputs,'errors':errors,'payloadExecution':False},indent=2))
Path('/tmp/c6-sdk7-pair-result.json').write_text(json.dumps({'configurations':checks,'physicalPairs':paired,'physicalDocumentOccurrences':docs,'compileInputOccurrences':inputs,'errors':errors,'payloadExecution':False},indent=2)+'\n')
