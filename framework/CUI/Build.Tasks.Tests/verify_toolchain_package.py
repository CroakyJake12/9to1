#!/usr/bin/env python3
"""Build a real consumer against the exact packaged tool files and source imports.

This exercises installed package assets directly; it does not claim that every
runtime dependency is published or that a full NuGet feed is release-ready.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile
from xml.sax.saxutils import escape

p = argparse.ArgumentParser()
p.add_argument('--package', type=Path, required=True)
p.add_argument('--evidence', type=Path, required=True)
a = p.parse_args()
e = a.evidence.resolve()
e.mkdir(parents=True, exist_ok=False)
root = Path(__file__).resolve().parents[1]
package = a.package.resolve()
installed = e / 'installed-package'
with zipfile.ZipFile(package) as z:
    z.extractall(installed)
rows = []


def check(name, passed, **detail):
    rows.append(dict(name=name, passed=bool(passed), **detail))
    (e / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
    if not passed:
        raise AssertionError(name)


def build(name, project, expected, diagnostic=None):
    cmd = ['/home/jacob/.local/bin/dotnet', 'build', str(project), '-m:1', '--verbosity', 'minimal']
    result = subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (name + '.log')).write_text(result.stdout)
    (e / (name + '.exit')).write_text(str(result.returncode) + '\n')
    check(name, (result.returncode == 0) == expected and (not diagnostic or diagnostic in result.stdout),
          command=cmd, exit=result.returncode, diagnostic=diagnostic)
    print(name, 'PASS', result.returncode, flush=True)


for mode, directory in [('package', installed), ('source', root / 'Compiler')]:
    f = e / (mode + '-consumer')
    f.mkdir()
    source = f / 'Main.cui'
    source.write_text('<Page><Text Text="Packaged real source"/></Page>')
    project = f / 'Consumer.csproj'
    props = directory / 'buildTransitive/CakeOS.Cui.props'
    targets = directory / 'buildTransitive/CakeOS.Cui.targets'
    project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<Import Project="{escape(str(props))}"/>
<ItemGroup><EmbeddedResource Include="Main.cui" LogicalName="App.Main"/></ItemGroup>
<Import Project="{escape(str(targets))}"/>
<Target Name="RecordCompilerChoice" BeforeTargets="CompileCuiSources">
<WriteLinesToFile File="$(MSBuildProjectDirectory)/toolchain.txt" Lines="$(CuiUsePrebuiltCompiler);$(CuiCompilerTaskAssembly);@(ProjectReference)" Overwrite="true"/>
</Target>
</Project>''')
    build(mode + '-bootstrap-without-overrides', project, True)
    choice = (f / 'toolchain.txt').read_text()
    if mode == 'package':
        check('packaged-tools-selected', str(installed) in choice and choice.splitlines()[0] == 'true')
        check('package-has-no-source-project-dependency', '.csproj' not in choice)
    else:
        check('source-tool-project-selected', 'CakeOS.Cui.Build.Compiler.csproj' in choice and choice.splitlines()[0] != 'true')
    artifact = json.loads((f / 'obj/Debug/net10.0/Cui/compilation.cuic.json').read_text())
    compiler = directory / ('tools/compiler/CakeOS.Cui.Compiler.dll' if mode == 'package' else 'bin/Debug/net10.0/CakeOS.Cui.Compiler.dll')
    check(mode + '-compiler-identity-matches', artifact['compilerSha256'] == hashlib.sha256(compiler.read_bytes()).hexdigest())
    assembly = (f / 'bin/Debug/net10.0/Consumer.dll').read_bytes()
    check(mode + '-source-and-artifact-embedded', b'App.Main' in assembly and b'CUI.Compilation.v1' in assembly and source.read_bytes() in assembly)
    check(mode + '-build-tools-not-app-runtime', not (f / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
    source.write_text('<Page><Text Widht="10"/></Page>')
    build(mode + '-reject-invalid-after-success', project, False, 'CUIC012')
print(json.dumps({'checks': len(rows), 'passed': sum(r['passed'] for r in rows), 'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest()}))
