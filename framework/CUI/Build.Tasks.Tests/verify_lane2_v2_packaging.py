#!/usr/bin/env python3
"""Lane 2 real MSBuild packaging boundary checks.

Proves the distinct typed V2 artifact is embedded, stateful raw is excluded from every shipped raw
identity, stateless raw retains its genuine V1/Link/metadata shape, and the incremental stateless ->
stateful transition removes only stale build-owned raw outputs.

The fixture uses DISTINCT sources with unambiguous item types: Stateful.cui is an embedded resource,
Stateless.cui is a Content item linked to Raw/Stateless.cui and copied to the output directory. This
avoids the ambiguous None+Content-on-the-same-file combination.

Every SDK invocation uses the owner's build guard. Run only after the coordinator allocates the
serialized .NET slot.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import xml.sax.saxutils as xml

parser = argparse.ArgumentParser()
parser.add_argument('--task-assembly', type=Path, required=True)
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--dotnet', type=Path, default=Path('/home/jacob/.local/bin/dotnet'))
args = parser.parse_args()
task = args.task_assembly.resolve()
evidence = args.evidence.resolve()
evidence.mkdir(parents=True, exist_ok=True)
fixture = evidence / 'fixture'
fixture.mkdir(exist_ok=False)
root = Path(__file__).resolve().parents[1]
props = root / 'Compiler/buildTransitive/CakeOS.Cui.props'
targets = root / 'Compiler/buildTransitive/CakeOS.Cui.targets'

project = fixture / 'Lane2Packaging.csproj'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><CuiUsePrebuiltCompiler>true</CuiUsePrebuiltCompiler>
<CuiCompilerTaskAssembly>{xml.escape(str(task))}</CuiCompilerTaskAssembly></PropertyGroup>
<ItemGroup>
  <EmbeddedResource Include="Stateful.cui" LogicalName="Lane2.Stateful.cui" />
  <Content Include="Stateless.cui" Link="Raw/Stateless.cui" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
<Import Project="{xml.escape(str(props))}" />
<Import Project="{xml.escape(str(targets))}" />
</Project>''')

stateless_source = '<Cui><Page ID="stateless"><Text Text="ok"/></Page></Cui>'
stateful_source = '''<Cui><Variable Type="bool" ID="Open">False</Variable>
<Actions><Action name="toggle"><Toggle Target="Open"/></Action></Actions>
<Page><Button Action="toggle" Content="Aa"/></Page></Cui>'''
(project.parent / 'Stateless.cui').write_text(stateless_source)
(project.parent / 'Stateful.cui').write_text(stateful_source)

assembly = fixture / 'bin/Debug/net10.0/Lane2Packaging.dll'
output_dir = fixture / 'bin/Debug/net10.0'
obj_artifact = fixture / 'obj/Debug/net10.0/Cui/compilation.cuic.json'


def run(name, expected_ok, expected_text=None, extra=()):
    command = [str(args.dotnet), 'build', str(project), '-m:1', '--verbosity', 'minimal', *extra]
    process = subprocess.run(command, cwd=fixture, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (evidence / (name + '.log')).write_text(process.stdout)
    passed = (process.returncode == 0) == expected_ok and (expected_text is None or expected_text in process.stdout)
    print(name, 'PASS' if passed else 'FAIL', 'exit', process.returncode, flush=True)
    if not passed:
        raise RuntimeError(name + ' did not match expected outcome; inspect its complete log')
    return process.stdout


def check(name, predicate):
    if not predicate:
        raise AssertionError(name)
    print(name, 'PASS', flush=True)


def resource_names():
    # List embedded resource names from the built assembly (strings > 4 chars).
    blob = assembly.read_bytes()
    names = set()
    for token in blob.split(b'\x00'):
        try:
            s = token.decode('utf-8')
        except Exception:
            continue
        if len(s) > 4 and s.count('.'):
            names.add(s)
    return names, blob


def artifact_payload():
    return json.loads(obj_artifact.read_text(encoding='utf-8'))


run('lane2-mixed-build', True)

names, blob = resource_names()
obj = artifact_payload()
check('v2-resource-embedded', b'CUI.Compilation.v2' in blob)
check('v2-artifact-format', obj.get('Format') == 'cui-build-compilation' and obj.get('FormatVersion') == 2)
check('v2-artifact-documents', len(obj.get('Documents') or []) == 2)
check('stateful-raw-not-embedded', 'Lane2.Stateful.cui' not in names)
check('stateful-raw-not-copied', not (output_dir / 'Stateful.cui').exists())
check('stateless-raw-copied', (output_dir / 'Raw' / 'Stateless.cui').exists())

# Incremental stateless -> stateful transition on the SAME output without Clean.
(project.parent / 'Stateless.cui').write_text(stateful_source)
unrelated = output_dir / 'unrelated.txt'
unrelated.write_text('keep me\n')
run('lane2-incremental-stateless-to-stateful', True)
names, blob = resource_names()
check('transitioned-stateless-raw-removed-from-output', not (output_dir / 'Raw' / 'Stateless.cui').exists())
check('transitioned-stateless-raw-not-embedded', 'Raw/Stateless.cui' not in names)
check('unrelated-file-untouched', unrelated.read_text() == 'keep me\n')
check('v2-still-embedded', b'CUI.Compilation.v2' in blob)
# Source files must never be deleted by the incremental transition.
check('source-not-deleted', (project.parent / 'Stateless.cui').exists() and (project.parent / 'Stateful.cui').exists())

# Failed compile must not publish a new successful artifact or destroy recoverable evidence.
(project.parent / 'Stateless.cui').write_text('<Cui><Page><Text Widht="10"/></Page></Cui>')
run('lane2-failed-compile', False, 'CUIC012')
check('failed-compile-preserves-artifact', assembly.exists())
