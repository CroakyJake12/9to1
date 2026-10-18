#!/usr/bin/env python3
"""Focused real-build probes for explicit resources and mid-build source edits."""
import argparse, hashlib, json, subprocess
from pathlib import Path
from xml.sax.saxutils import escape
p=argparse.ArgumentParser(); p.add_argument('--evidence',type=Path,required=True); args=p.parse_args()
e=args.evidence.resolve(); e.mkdir(parents=True,exist_ok=False)
r=Path(__file__).resolve().parents[1]; task=r/'Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll'; legacy=r/'Compiler/CakeOS.Cui.Build.Tasks/bin/Debug/netstandard2.0/CakeOS.Cui.Build.Tasks.dll'
rows=[]
def check(name,value):
 rows.append({'name':name,'passed':bool(value)}); (e/'results.json').write_text(json.dumps(rows,indent=2))
 if not value: raise AssertionError(name)
def build(name,project,good,extra=()):
 cmd=['/home/jacob/.local/bin/dotnet','build',str(project),'-m:1','--verbosity','minimal',*extra]
 x=subprocess.run(cmd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 (e/(name+'.log')).write_text(x.stdout); (e/(name+'.exit')).write_text(str(x.returncode)+'\n')
 check(name,(x.returncode==0)==good)
 if not good: check(name+'-compiler-diagnostic','CUIC012' in x.stdout)
 print(name,'PASS',x.returncode,flush=True)
 return x.stdout
shared=e/'outside'; shared.mkdir()
good={}; paths={}
for kind in ['EmbeddedResource','Content','None','AvaloniaResource']:
 path=shared/(kind+'.cui'); paths[kind]=path; good[kind]=f'<Page><Text Text="Frozen{kind}"/></Page>'.encode(); path.write_bytes(good[kind])
# Identical source bytes must still preserve two distinct copy identities.
good['None']=good['Content']
paths['None'].write_bytes(good['None'])
bad=b'<Page><Text Widht="ChangedAfterCompilation"/></Page>'
for vendor in [False,True]:
 name='vendor' if vendor else 'sdk'; f=e/name; f.mkdir(); project=f/'Custody.csproj'
 kinds=['EmbeddedResource','Content','None']+(['AvaloniaResource'] if vendor else [])
 for kind in paths: paths[kind].write_bytes(good[kind])
 items='\n'.join(f'<{kind} Include="{escape(str(paths[kind]))}" Link="Linked/{kind}.cui" LogicalName="App.{kind}" CopyToOutputDirectory="PreserveNewest" />' for kind in kinds)
 mutation='\n'.join(f'<WriteLinesToFile File="{escape(str(paths[kind]))}" Lines="{escape(bad.decode()).replace(chr(34),"&quot;")}" Overwrite="true" />' for kind in kinds)
 props=r/('vendor/Avalonia/packages/Avalonia/AvaloniaBuildTasks.props' if vendor else 'Compiler/buildTransitive/CakeOS.Cui.props')
 targets=r/('vendor/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets' if vendor else 'Compiler/buildTransitive/CakeOS.Cui.targets')
 project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><CuiUsePrebuiltCompiler>true</CuiUsePrebuiltCompiler><CuiCompilerTaskAssembly>{escape(str(task))}</CuiCompilerTaskAssembly><CuiBuildTasksLocation>{escape(str(legacy))}</CuiBuildTasksLocation></PropertyGroup><Import Project="{props}"/><ItemGroup>{items}</ItemGroup><Import Project="{targets}"/><Target Name="EditOriginalAfterCompile" AfterTargets="CompileCuiSources" Condition="'$(MutateCuiSources)' == 'true'">{mutation}</Target></Project>''')
 (f/'Main.cui').write_text('<Page><Text Text="RootSource"/></Page>')
 # Explicit items outside discovery roots must each participate in compilation.
 for kind in kinds:
  paths[kind].write_bytes(bad)
  build(name+'-reject-outside-'+kind,project,False)
  paths[kind].write_bytes(good[kind])
 build(name+'-mid-build-edit',project,True,('-p:MutateCuiSources=true',))
 artifact=json.loads((f/'obj/Debug/net10.0/Cui/compilation.cuic.json').read_text())
 hashes={item['source']:item['sha256'] for item in artifact['documents']}
 check(name+'-all-explicit-sources-indexed',len(artifact['documents'])==len(kinds)+1)
 check(name+'-originals-really-changed',all(paths[kind].read_bytes().strip()==bad for kind in kinds))
 for kind in kinds:
  check(name+'-snapshot-hash-'+kind,hashes['../outside/'+kind+'.cui']==hashlib.sha256(good[kind]).hexdigest())
 assembly=(f/'bin/Debug/net10.0/Custody.dll').read_bytes()
 check(name+'-original-embedded-name-and-bytes',b'App.EmbeddedResource' in assembly and good['EmbeddedResource'] in assembly and bad not in assembly)
 for kind in ['Content','None']:
  check(name+'-copy-name-and-bytes-'+kind,(f/('bin/Debug/net10.0/Linked/'+kind+'.cui')).read_bytes()==good[kind])
 if vendor:
  # Legacy bundle is an uncompressed length-prefixed byte stream; keep original
  # identities and bytes, never hash-generated snapshot paths as public identities.
  bundle=(f/'obj/Debug/net10.0/Cui/resources').read_bytes()
  logical=str(paths['AvaloniaResource']).replace('/','.').encode()
  check('vendor-bundle-preserves-identity-and-snapshot',logical in bundle and good['AvaloniaResource'] in bundle and bad not in bundle and b'.sources.' not in bundle)
 build(name+'-next-build-rejects-edited-originals',project,False)
print(json.dumps({'passed':len(rows),'checks':len(rows),'evidence':str(e)}))
