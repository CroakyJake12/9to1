import struct,uuid,json,pathlib
p=pathlib.Path('/workspace/.nuget/packages/taglibsharp/2.3.0/lib/netstandard2.0/TagLibSharp.pdb');b=p.read_bytes()
u16=lambda p:struct.unpack_from('<H',b,p)[0]
u32=lambda p:struct.unpack_from('<I',b,p)[0]
u64=lambda p:struct.unpack_from('<Q',b,p)[0]
assert b[:4]==b'BSJB'
i=16+u32(12);i=(i+3)//4*4;count=u16(i+2);i+=4;streams={}
for _ in range(count):
 off,size=u32(i),u32(i+4);i+=8;end=b.index(0,i);name=b[i:end].decode();i=(end+1+3)//4*4;streams[name]=(off,size)
blobbase=streams['#Blob'][0];guidbase=streams['#GUID'][0]
def compressed(pos):
 a=b[pos]
 if a<128:return a,pos+1
 if a<192:return ((a&63)<<8)|b[pos+1],pos+2
 return ((a&31)<<24)|(b[pos+1]<<16)|(b[pos+2]<<8)|b[pos+3],pos+4
def blob(index):
 pos=blobbase+index;n,pos=compressed(pos);return b[pos:pos+n]
def name(index):
 pos=blobbase+index;n,pos=compressed(pos);end=pos+n;sep=chr(b[pos]);pos+=1;parts=[]
 while pos<end:
  idx,pos=compressed(pos);parts.append(blob(idx).decode('utf8'))
 return sep.join(parts)
i=streams['#~'][0];heaps=b[i+6];valid=u64(i+8);i+=24;rows={}
for table in range(64):
 if valid&(1<<table):rows[table]=u32(i);i+=4
assert min(rows)==48,rows
bs=4 if heaps&4 else 2;gs=4 if heaps&2 else 2
read=lambda pos,size:int.from_bytes(b[pos:pos+size],'little')
documents=[]
for _ in range(rows[48]):
 ni=read(i,bs);i+=bs;alg=read(i,gs);i+=gs;hi=read(i,bs);i+=bs;lang=read(i,gs);i+=gs
 documents.append(dict(path=name(ni),algorithm=str(uuid.UUID(bytes_le=b[guidbase+(alg-1)*16:guidbase+alg*16])),checksum=blob(hi).hex()))
print(json.dumps(dict(pdb=str(p),documents=[d for d in documents if any(x in d['path'].replace('\\','/') for x in ['/Xmp/','/Image/','/Png/'])]),indent=2))
