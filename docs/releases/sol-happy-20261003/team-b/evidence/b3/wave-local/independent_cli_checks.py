from pathlib import Path
import subprocess,os,wave,struct,hashlib,json,platform,uuid
base=Path(__file__).resolve().parent
fixture=base/'fixtures-verified'/uuid.uuid4().hex;fixture.mkdir(parents=True)
binary=base/'bin/Release/net10.0/B3.Wave.LocalSourceRegression.dll'
dotnet='/workspace/.tools/dotnet/dotnet'
env=dict(os.environ,DOTNET_CLI_HOME=str(base.parent/'dotnet-home'),NUGET_PACKAGES=str(base.parent/'nuget'),TMPDIR=str(base.parent/'tmp'),DOTNET_NOLOGO='1')
outcomes=[];commands=[]
def invoke(args):
 command=[dotnet,str(binary),*map(str,args)]
 p=subprocess.run(command,env=env,text=True,capture_output=True,timeout=30)
 commands.append(dict(command=command,exit_code=p.returncode,stdout=p.stdout,stderr=p.stderr))
 return p

def check(i,description,condition):
 outcomes.append(dict(test_id=i,description=description,outcome='PASS' if condition else 'FAIL'))
 (base/'incremental-cli-results.json').write_text(json.dumps(dict(outcomes=outcomes,commands=commands),indent=2)+'\n')
 if not condition: raise AssertionError(i+': '+description)
try:
 # Independent stereo PCM fixture, no mocked domain/provider and no browser claim.
 audio=fixture/'source.wav'
 frames=b''.join(struct.pack('<hh',i%2000-1000,1000-i%2000) for i in range(48000))
 with wave.open(str(audio),'wb') as w:
  w.setnchannels(2);w.setsampwidth(2);w.setframerate(48000);w.writeframes(frames)
 source_hash=hashlib.sha256(audio.read_bytes()).hexdigest()
 project=fixture/'project.waveproject.json'
 created=invoke(['--project-create',project,'Independent track'])
 p0=json.loads(project.read_text())
 check('B3-WAVE-CLI-01','Create persists nonempty project/track identities at revision 0',created.returncode==0 and p0['ProjectId'] and p0['Tracks'][0]['TrackId'] and p0['Revision']==0)
 imported=invoke(['--project-import',project,audio,'0.5'])
 p1=json.loads(project.read_text());clip=p1['Tracks'][0]['Clips'][0]
 check('B3-WAVE-CLI-02','Import retains project/track identity, increments revision and uses exact 24000-frame offset/48000 source range',imported.returncode==0 and p1['ProjectId']==p0['ProjectId'] and p1['Tracks'][0]['TrackId']==p0['Tracks'][0]['TrackId'] and p1['Revision']==1 and clip['TimelineStartFrame']==24000 and clip['FrameCount']==48000 and len(clip['SourceSha256'])==64 and bytes.fromhex(clip['SourceSha256'])==bytes.fromhex(source_hash))
 project_bytes=project.read_bytes();mix=fixture/'mix.wav'
 exported=invoke(['--project-export',project,mix])
 with wave.open(str(mix),'rb') as w:
  actual=w.readframes(w.getnframes());size=w.getnframes();fmt=(w.getnchannels(),w.getsampwidth(),w.getframerate())
 check('B3-WAVE-CLI-03','Export PCM exactly preserves stereo source after half-second silence and never mutates project/source',exported.returncode==0 and size==72000 and fmt==(2,2,48000) and actual[:24000*4]==b'\0'*(24000*4) and actual[24000*4:]==frames and project.read_bytes()==project_bytes and hashlib.sha256(audio.read_bytes()).hexdigest()==source_hash)
 original_output=mix.read_bytes();again=invoke(['--project-export',project,mix])
 check('B3-WAVE-CLI-04','Export refuses existing output and preserves bytes',again.returncode!=0 and mix.read_bytes()==original_output)
 trimmed=fixture/'trim.wav';trim=invoke(['--trim',audio,'0.25','0.75',trimmed])
 with wave.open(str(trimmed),'rb') as w:trim_frames=w.readframes(w.getnframes());trim_count=w.getnframes()
 check('B3-WAVE-CLI-05','Trim outputs exact half-open 12000..36000 PCM frames',trim.returncode==0 and trim_count==24000 and trim_frames==frames[12000*4:36000*4])
 source_bytes=audio.read_bytes();inplace=invoke(['--trim',audio,'0','0.5',audio])
 check('B3-WAVE-CLI-06','In-place trim is denied and preserves original source',inplace.returncode!=0 and audio.read_bytes()==source_bytes)
 corrupted=bytearray(source_bytes);corrupted[-1]^=0x7f;audio.write_bytes(corrupted)
 changed=fixture/'changed-export.wav';result=invoke(['--project-export',project,changed])
 check('B3-WAVE-CLI-07','Altered isolated source negative control is rejected with no partial output; project unchanged',result.returncode!=0 and 'SourceChanged' in result.stderr and not changed.exists() and project.read_bytes()==project_bytes)
 audio.unlink();missing=fixture/'missing-export.wav';result=invoke(['--project-export',project,missing])
 check('B3-WAVE-CLI-08','Missing isolated source is rejected explicitly, no fabricated audio, project unchanged',result.returncode!=0 and 'SourceUnavailable' in result.stderr and not missing.exists() and project.read_bytes()==project_bytes)
 future=fixture/'future.waveproject.json';future.write_text('{"SchemaVersion":99}')
 result=invoke(['--project-export',future,fixture/'future.wav'])
 check('B3-WAVE-CLI-09','Unknown project schema is refused and never rendered/rewritten',result.returncode!=0 and not (fixture/'future.wav').exists() and future.read_text()=='{"SchemaVersion":99}')
finally:
 all_outcomes=outcomes+[{"test_id":f"B3-WAVE-CLI-{i:02d}","outcome":"NOT-RUN","description":"Earlier failure prevented execution"} for i in range(len(outcomes)+1,10)]
 report=dict(commit=json.loads((base/'source-manifest.json').read_text())['commit'],binary=str(binary),binary_sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),platform=platform.platform(),runtime=subprocess.check_output([dotnet,'--version'],env=env,text=True).strip(),fixture=str(fixture),fixture_definition='48000 Hz stereo PCM16; frame i: (i % 2000 - 1000, 1000 - i % 2000)',source_fixture_sha256=globals().get('source_hash'),tests_discovered=9,tests_executed=len(outcomes),outcomes=all_outcomes,commands=commands,browser_acceptance=False,production_project_build=False)
 (base/'independent-cli-results.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps({k:v for k,v in report.items() if k!='commands'},indent=2))
