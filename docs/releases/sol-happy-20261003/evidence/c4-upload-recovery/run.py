"""Usage: python run.py REPO DOTNET ISOLATED_OUTPUT. Never edits production source."""
import hashlib, json, pathlib, subprocess, sys
repo, dotnet, output = sys.argv[1:]
here = pathlib.Path(__file__).resolve().parent
index = json.loads((here / 'index.json').read_text())
out = pathlib.Path(output).resolve()
out.mkdir(parents=True, exist_ok=False)
for pin in index['sourcePins']:
    data = subprocess.check_output(['git', '-C', repo, 'show', index['sourceCommit'] + ':' + pin['path']])
    assert len(data) == pin['bytes'] and hashlib.sha256(data).hexdigest() == pin['sha256']
    path = out / 'canonical' / pin['path'].split('/CUI/')[1]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
import xml.etree.ElementTree as ET
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
properties = ET.SubElement(project, 'PropertyGroup')
for key, value in {'TargetFramework':'net10.0','LangVersion':'14.0','Nullable':'enable','ImplicitUsings':'enable','TreatWarningsAsErrors':'true','EnableDefaultCompileItems':'false','IsTestProject':'true'}.items():
    ET.SubElement(properties,key).text = value
items = ET.SubElement(project,'ItemGroup')
ET.SubElement(items,'Compile',Include='canonical/**/*.cs')
ET.SubElement(items,'Compile',Include=str(here / 'FilesUploadRecoveryTests.cs'))
for name, version in [('Microsoft.NET.Test.Sdk','17.14.1'),('xunit','2.9.3'),('xunit.runner.visualstudio','3.1.5')]:
    ET.SubElement(items,'PackageReference',Include=name,Version=version)
ET.ElementTree(project).write(out/'ActualUpload.Tests.csproj',encoding='unicode')
sys.exit(subprocess.call([dotnet,'test',str(out/'ActualUpload.Tests.csproj'),'--logger','trx;LogFileName=upload.trx']))
