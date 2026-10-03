import struct,uuid
def pdb_documents(data):
 assert data[:4]==b'BSJB'; n=struct.unpack_from('<I',data,12)[0]; pos=(16+n+3)&~3; _,count=struct.unpack_from('<HH',data,pos);pos+=4; streams={}
 for _ in range(count):
  off,size=struct.unpack_from('<II',data,pos);pos+=8;end=data.index(0,pos);name=data[pos:end].decode();pos=(end+4)&~3;streams[name]=data[off:off+size]
 tables=streams['#~']; heap=tables[6]; valid=struct.unpack_from('<Q',tables,8)[0];pos=24; rows={}
 for i in range(64):
  if valid>>i&1:rows[i]=struct.unpack_from('<I',tables,pos)[0];pos+=4
 assert not any(i<48 for i in rows); blob=streams['#Blob']; guids=streams['#GUID']; bw=4 if heap&4 else 2;gw=4 if heap&2 else 2
 def uint(data,at):
  v=data[at]
  if v<128:return v,at+1
  if v<192:return ((v&63)<<8)|data[at+1],at+2
  return ((v&31)<<24)|(data[at+1]<<16)|(data[at+2]<<8)|data[at+3],at+4
 def getblob(at):
  size,p=uint(blob,at);return blob[p:p+size]
 result={}
 for _ in range(rows.get(48,0)):
  vals=[]
  for width in (bw,gw,bw,gw):vals.append(int.from_bytes(tables[pos:pos+width],'little'));pos+=width
  name,algo,digest,lang=vals; pathdata=getblob(name); separator=chr(pathdata[0]);q=1;parts=[]
  while q<len(pathdata):v,q=uint(pathdata,q);parts.append(getblob(v).decode())
  path=separator.join(parts); algorithm=str(uuid.UUID(bytes_le=guids[(algo-1)*16:algo*16]));assert algorithm in ('8829d00f-11b8-4213-878b-770e8597ac16','ff1816ec-aa5e-4d10-87f7-6f4963833460');digest=getblob(digest).hex();assert len(digest)==(64 if algorithm=='8829d00f-11b8-4213-878b-770e8597ac16' else 40)
  result[path]={'digest':digest,'algorithm':algorithm,'hashName':'sha256' if len(digest)==64 else 'sha1'}
 return result

def metadata_streams(data):
 assert data[:4]==b'BSJB';n=struct.unpack_from('<I',data,12)[0];pos=(16+n+3)&~3;_,count=struct.unpack_from('<HH',data,pos);pos+=4;result={}
 for _ in range(count):
  off,size=struct.unpack_from('<II',data,pos);pos+=8;end=data.index(0,pos);name=data[pos:end].decode();pos=(end+4)&~3
  assert name not in result and off+size<=len(data);result[name]=data[off:off+size]
 return result

def assert_actual_pair(dll,pdb):
 # Portable PDB content identity must match the actual PE CodeView record and stamp.
 assert dll[:2]==b'MZ';pe=struct.unpack_from('<I',dll,0x3c)[0];assert dll[pe:pe+4]==b'PE\0\0'
 sections=struct.unpack_from('<H',dll,pe+6)[0];optSize=struct.unpack_from('<H',dll,pe+20)[0];opt=pe+24;magic=struct.unpack_from('<H',dll,opt)[0];assert magic in (0x10b,0x20b)
 directory=opt+(96 if magic==0x10b else 112);rva,size=struct.unpack_from('<II',dll,directory+6*8);assert rva and size and size%28==0
 def rva_offset(value):
  for i in range(sections):
   row=opt+optSize+i*40;virtualSize,address,rawSize,raw=struct.unpack_from('<IIII',dll,row+8)
   if address<=value<address+max(virtualSize,rawSize):
    offset=raw+(value-address);assert offset<len(dll);return offset
  raise ValueError('actual PE debug directory RVA outside sections')
 at=rva_offset(rva);matches=[]
 for i in range(size//28):
  row=at+i*28;stamp=struct.unpack_from('<I',dll,row+4)[0];kind,amount,address,pointer=struct.unpack_from('<IIII',dll,row+12)
  if kind==2:
   content=dll[pointer:pointer+amount];assert len(content)==amount and content[:4]==b'RSDS' and struct.unpack_from('<I',content,20)[0]==1
   matches.append((content[4:20],stamp))
 assert len(matches)==1
 identifier=metadata_streams(pdb)['#Pdb'][:20];assert len(identifier)==20
 assert identifier[:16]==matches[0][0] and struct.unpack_from('<I',identifier,16)[0]==matches[0][1], 'actual DLL/PDB identity mismatch'
 return {'portablePdbId':identifier.hex(),'codeViewGuid':str(uuid.UUID(bytes_le=matches[0][0])),'codeViewStamp':matches[0][1]}
