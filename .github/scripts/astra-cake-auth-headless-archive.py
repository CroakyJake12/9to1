"""Fresh official headless archive issuance; no browser or maintained library execution."""
import argparse, hashlib, json, os, pathlib, platform, stat, urllib.parse, urllib.request, zipfile

P=pathlib.Path
DESCRIPTOR_SHA='545d52f8382c391e605562c330e9c1c534a16045898203037a49bb8bd769a946'
CORE_SHA='549070af3acabb3efcc4f55bfe6210f9f7c2fcf633cf7eaa59bfe60719969171'
VERSION='153.0.8010.12'
REVISION='1243'
URL='https://cdn.playwright.dev/builds/cft/'+VERSION+'/linux64/chrome-headless-shell-linux64.zip'
MAX_ARCHIVE=512*1024*1024
MAX_EXPANDED=2*1024*1024*1024

def sha(path):
 with P(path).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

def identity(path):
 s=P(path).lstat();return {'device':s.st_dev,'inode':s.st_ino,'mode':stat.S_IMODE(s.st_mode),'type':stat.S_IFMT(s.st_mode)}

def inventory(root):
 rows=[]
 for p in sorted(root.rglob('*')):
  s=p.lstat();r={'path':p.relative_to(root).as_posix(),'mode':stat.S_IMODE(s.st_mode)}
  if stat.S_ISDIR(s.st_mode):r['type']='directory'
  elif stat.S_ISREG(s.st_mode):r.update(type='file',bytes=s.st_size,sha256=sha(p))
  else:raise ValueError('Unknown/link installed archive node refused')
  rows.append(r)
 return rows

def install(harness,cache,archive_root,receipt):
 harness=P(harness);cache=P(cache);archive_root=P(archive_root);receipt=P(receipt)
 for p in [harness,cache,archive_root,receipt]:
  if not p.is_absolute():raise ValueError('Explicit absolute source/archive paths required')
 if harness.resolve(strict=True)!=harness:raise ValueError('Canonical harness path required')
 os_release=dict(line.rstrip().split('=',1) for line in P('/etc/os-release').read_text().splitlines() if '=' in line)
 if os_release.get('ID','').strip('"')!='ubuntu' or os_release.get('VERSION_ID','').strip('"')!='24.04' or platform.machine()!='x86_64':raise ValueError('Exact maintained Ubuntu24.04 x64 archive mapping required')
 core=harness/'node_modules/playwright-core'
 if json.loads((core/'package.json').read_text())['version']!='1.63.0':raise ValueError('Exact locked maintained core version required')
 descriptor=core/'browsers.json';maintained=core/'lib/coreBundle.js'
 if sha(descriptor)!=DESCRIPTOR_SHA or sha(maintained)!=CORE_SHA:raise ValueError('Maintained descriptor/source differs from pinned full npm package')
 entries=[r for r in json.loads(descriptor.read_text())['browsers'] if r['name']=='chromium-headless-shell']
 if len(entries)!=1 or str(entries[0]['revision'])!=REVISION or entries[0]['browserVersion']!=VERSION or entries[0].get('revisionOverrides',{}).get('ubuntu24.04-x64'):raise ValueError('Exact official browser descriptor required')
 source=maintained.read_text()
 for fragment in ['path: `builds/cft/${browserVersion}/${suffix}`','"https://cdn.playwright.dev"','"ubuntu24.04-x64": cftUrl("linux64/chrome-headless-shell-linux64.zip")','"linux-x64": ["chrome-headless-shell-linux64", "chrome-headless-shell"]']:
  if fragment not in source:raise ValueError('Pinned official URL/executable source mapping absent')
 for p in [cache,archive_root,receipt]:
  if p.exists() or p.is_symlink():raise ValueError('Fresh archive/cache/receipt paths required')
  if p.parent.resolve(strict=True)!=p.parent:raise ValueError('Canonical issued parent required')
 cache.mkdir(mode=0o700);archive_root.mkdir(mode=0o700)
 archive=archive_root/'chrome-headless-shell-linux64.zip';received=0;hash256=hashlib.sha256();hash512=hashlib.sha512()
 with urllib.request.urlopen(URL,timeout=120) as response,archive.open('xb') as target:
  final_url=response.geturl();headers=dict(response.headers.items())
  if urllib.parse.urlsplit(final_url).scheme!='https':raise ValueError('Official archive final redirect URL must remain HTTPS')
  while block:=response.read(1024*1024):
   received+=len(block)
   if received>MAX_ARCHIVE:raise ValueError('Explicit diagnostic archive-byte limit exceeded')
   hash256.update(block);hash512.update(block);target.write(block)
 if received==0:raise ValueError('Empty official browser archive refused')
 archive_record={'url':URL,'actualFinalUrl':final_url,'bytes':received,'sha256':hash256.hexdigest(),'sha512':hash512.hexdigest(),'responseHeaders':{k:v for k,v in headers.items() if k.lower() in ['content-length','content-type','etag','last-modified']},'qualification':'Observed official HTTPS archive bytes and maintained source mapping; no independently published expected browser archive hash is asserted'}
 (archive_root/'archive-receipt.json').write_text(json.dumps(archive_record,indent=2)+'\n')
 destination=cache/('chromium_headless_shell-'+REVISION);destination.mkdir(mode=0o755)
 files={};directories={};members=[];seen=set();expanded=0
 with zipfile.ZipFile(archive) as z:
  if not z.infolist():raise ValueError('Empty ZIP member inventory refused')
  for member in z.infolist():
   name=member.filename;rel=pathlib.PurePosixPath(name.rstrip('/'))
   if not name or name.startswith('/') or '\\' in name or rel.as_posix()!=name.rstrip('/') or '..' in rel.parts or '.' in rel.parts or rel.parts[0]!='chrome-headless-shell-linux64' or name in seen:raise ValueError('Noncanonical/escaping/duplicate official ZIP member refused')
   seen.add(name);mode=member.external_attr>>16;kind=stat.S_IFMT(mode)
   if member.flag_bits&1 or member.create_system!=3:raise ValueError('Encrypted/unknown ZIP source node refused')
   if member.is_dir():
    if kind!=stat.S_IFDIR:raise ValueError('ZIP directory type mismatch')
    if z.read(member)!=b'' or member.file_size!=0:raise ValueError('Nonempty ZIP directory payload refused')
    directory_mode=stat.S_IMODE(mode)
    if directory_mode&0o7000:raise ValueError('Unexpected special directory mode refused')
    if rel.as_posix() in directories and directories[rel.as_posix()]!=directory_mode:raise ValueError('Conflicting archive directory mode')
    directories[rel.as_posix()]=directory_mode;data=None
   else:
    if kind!=stat.S_IFREG or rel.as_posix() in files:raise ValueError('Link/unknown/duplicate archive file refused')
    file_mode=stat.S_IMODE(mode)
    if file_mode&0o7000:raise ValueError('Unexpected special file mode refused')
    expanded+=member.file_size
    if expanded>MAX_EXPANDED:raise ValueError('Explicit diagnostic expanded-byte limit exceeded')
    data=z.read(member);assert len(data)==member.file_size
    files[rel.as_posix()]={'path':rel.as_posix(),'type':'file','mode':file_mode,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
   members.append({'path':name,'type':'directory' if member.is_dir() else 'file','mode':stat.S_IMODE(mode),'bytes':member.file_size,'compressedBytes':member.compress_size,'crc32':f'{member.CRC:08x}','sha256':None if data is None else hashlib.sha256(data).hexdigest()})
  # Complete directory set includes exactly archive directories and implicit parents.
  for name in list(files)+list(directories):
   for parent in pathlib.PurePosixPath(name).parents:
    if parent.as_posix()!='.':directories.setdefault(parent.as_posix(),0o755)
  if set(files)&set(directories):raise ValueError('Archive file/directory collision refused')
  for name,mode in sorted(directories.items(),key=lambda r:len(pathlib.PurePosixPath(r[0]).parts)):
   p=destination/name;p.mkdir(mode=mode,exist_ok=True)
   if p.is_symlink() or p.resolve(strict=True)!=p:raise ValueError('Issued archive directory escaped')
   os.chmod(p,mode)
  for member in z.infolist():
   if member.is_dir():continue
   p=destination/member.filename
   with p.open('xb') as target:target.write(z.read(member))
   os.chmod(p,files[member.filename]['mode'])
 expected=[{'path':name,'type':'directory','mode':mode} for name,mode in directories.items()]+list(files.values());expected=sorted(expected,key=lambda r:r['path'])
 actual=inventory(destination)
 if actual!=expected:raise ValueError('Complete installed archive node/type/mode/byte inventory mismatch')
 executable=destination/'chrome-headless-shell-linux64/chrome-headless-shell'
 if not executable.is_file() or executable.is_symlink() or not os.access(executable,os.X_OK):raise ValueError('Exact inventoried official executable absent')
 if sha(archive)!=archive_record['sha256']:raise ValueError('Original archive changed during installation')
 record={'status':'OFFICIAL_HEADLESS_ARCHIVE_COMPLETE_BEFORE_FIRST_EXECUTION','archive':archive_record,'archivePath':str(archive),'archiveRootIdentity':identity(archive_root),'cachePath':str(cache),'cacheIdentity':identity(cache),'installedRoot':str(destination),'installedRootIdentity':identity(destination),'descriptorSha256':DESCRIPTOR_SHA,'maintainedRegistrySourceSha256':CORE_SHA,'playwrightVersion':'1.63.0','browserVersion':VERSION,'browserRevision':REVISION,'archiveMembers':members,'completeInstalledMap':actual,'executable':str(executable),'executableSha256':sha(executable),'executableIdentity':identity(executable),'limits':{'maximumArchiveBytes':MAX_ARCHIVE,'maximumExpandedBytes':MAX_EXPANDED},'qualification':'Source-bound exact official archive retained separately with full ZIP member CRC/type/mode/bytes and complete extraction inventory before native execution. No browser launch, installed upstream archive expected hash, OS dependency installation or maintained-source change.'}
 receipt.write_text(json.dumps(record,indent=2)+'\n')

if __name__=='__main__':
 a=argparse.ArgumentParser();a.add_argument('--harness',required=True);a.add_argument('--cache',required=True);a.add_argument('--archive-root',required=True);a.add_argument('--receipt',required=True);v=a.parse_args();install(v.harness,v.cache,v.archive_root,v.receipt)
