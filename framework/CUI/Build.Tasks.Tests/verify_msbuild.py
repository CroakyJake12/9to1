#!/usr/bin/env python3
"""Real MSBuild boundary checks. Every SDK invocation uses the owner's build guard."""
import argparse
import hashlib
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
project = fixture / 'BuildBoundary.csproj'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><CuiUsePrebuiltCompiler>true</CuiUsePrebuiltCompiler>
<CuiCompilerTaskAssembly>{xml.escape(str(task))}</CuiCompilerTaskAssembly></PropertyGroup>
<ItemGroup><None Update="Main.cui" CopyToOutputDirectory="PreserveNewest" /><None Remove="Legacy.AXAML" /></ItemGroup>
<Import Project="{xml.escape(str(props))}" />
<Import Project="{xml.escape(str(targets))}" />
</Project>''')
main = fixture / 'Main.cui'
shared = fixture / 'Shared.cui-prefab'
theme = fixture / 'Palette.cui-theme'
main_source = '<Cui>\n<Import Source="Shared.cui-prefab"/>\n<Page ID="main"><Shared/><Text ID="caption" FontSize="18">Hello</Text></Page>\n</Cui>'
main.write_text(main_source)
shared.write_text('<Prefab (Definition) Type="dynamic" ID="Shared"><Text Text="Shared"/></Prefab>')
theme.write_text('<Theme (Definition) Name="Example" Inherits="Professional"><Text (Definition) FontSize="14"/></Theme>')
artifact = fixture / 'obj/Debug/net10.0/Cui/compilation.cuic.json'
rows = []
def run(name, expected_ok, expected_text=None, extra=()):
    command = [str(args.dotnet), 'build', str(project), '-m:1', '--verbosity', 'minimal', *extra]
    process = subprocess.run(command, cwd=fixture, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (evidence / (name + '.log')).write_text(process.stdout)
    (evidence / (name + '.exit')).write_text(str(process.returncode) + '\n')
    passed = (process.returncode == 0) == expected_ok and (expected_text is None or expected_text in process.stdout)
    rows.append(dict(name=name, command=command, exit=process.returncode, expected_success=expected_ok,
                     expected_text=expected_text, passed=passed))
    print(name, 'PASS' if passed else 'FAIL', 'exit', process.returncode, flush=True)
    (evidence / 'results.json').write_text(json.dumps(rows, indent=2))
    if not passed:
        raise RuntimeError(name + ' did not match expected outcome; inspect its complete log')
    return process.stdout

def check(name, predicate):
    rows.append(dict(name=name, passed=bool(predicate)))
    (evidence / 'results.json').write_text(json.dumps(rows, indent=2))
    if not predicate:
        raise AssertionError(name)

run('valid-source-family', True)
original = artifact.read_bytes()
compiled = json.loads(original)
check('all-family-files-compiled', {entry['source'] for entry in compiled['documents']} == {'Main.cui', 'Shared.cui-prefab', 'Palette.cui-theme'})
check('actual-version-metadata', compiled['metadata']['LanguageVersion'] == '1' and compiled['metadata']['RuntimeAbiVersion'] == '1' and compiled['metadata']['ComponentRegistryVersion'] == '2.0')
check('typed-value-payload-preserved', b'CuiLiteralValue' in original and b'18' in original and b'caption' in original)
check('source-maps-present', any(entry['AuthoredId'] == 'caption' for doc in compiled['documents'] for entry in doc['compilation']['SourceMap']))
check('source-hashes-current', all(hashlib.sha256((fixture / entry['source']).read_bytes()).hexdigest() == entry['sha256'] for entry in compiled['documents']))
assembly = fixture / 'bin/Debug/net10.0/BuildBoundary.dll'
check('artifact-embedded-in-real-assembly', b'CUI.Compilation.v1' in assembly.read_bytes())
check('no-generated-axaml', not list(fixture.rglob('*.axaml')))
check('authored-copy-metadata-preserved', (fixture / 'bin/Debug/net10.0/Main.cui').read_text() == main_source)
run('source-toolchain-build-graph', True, extra=('-p:CuiUsePrebuiltCompiler=false',))
# This successful build may legitimately rebuild the compiler (including its
# source revision identity), so it is now the last valid artifact to preserve.
original = artifact.read_bytes()
check('normal-build-does-not-copy-toolchain', not (fixture / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
# Stale positive artifact must not allow a changed source through incremental Build.
main.write_text('<Page>\n<Text Widht="10"/>\n</Page>')
output = run('unknown-property-after-success', False, 'CUIC012')
check('source-line-column-diagnostic', 'Main.cui(2,' in output)
check('failed-compile-preserves-last-good-artifact', artifact.read_bytes() == original)
main.write_text('<Page><Text FontSize="not-a-number"/></Page>')
run('invalid-typed-value', False, 'CUIC')
main.write_text('<Page><Grid/></Page>')
run('forbidden-donor-tag', False, 'CUI')
main.write_text('<Page><Text></Page>')
run('malformed-source', False, 'CUI')
main.write_text(main_source)
shared.unlink()
run('removed-import-not-reused', False, 'CUIC018')
shared.write_text('<Prefab (Definition) Type="dynamic" ID="Shared"><Text Text="Shared"/></Prefab>')
for extension in ('.axaml', '.xaml', '.hui', '.AXAML'):
    legacy = fixture / ('Legacy' + extension)
    legacy.write_text('<Page/>')
    run('reject-' + extension[1:], False, 'CUIB001')
    legacy.unlink()
# A referenced/donor directory outside the declared root is not application markup.
donor = evidence / 'donor'
donor.mkdir()
(donor / 'Private.axaml').write_text('<PrivateDonorTree/>')
main.write_text(main_source.replace('Hello', 'Rebuilt'))
run('valid-after-repair', True)
check('artifact-changed-on-valid-repair', artifact.read_bytes() != original)
# Actual target-only entrypoints may supply backslashes even on Linux.
run('msbuild-path-separators', True, extra=('-p:CuiCompilationArtifact=obj\\path-check\\compilation.cuic.json',))
check('msbuild-path-normalized', (fixture / 'obj/path-check/compilation.cuic.json').exists())

run('missing-toolchain-fails', False, 'CUIB011', ('-p:CuiCompilerTaskAssembly=' + str(fixture / 'missing.dll'),))
check('toolchain-not-shipped-as-app-dependency', not (fixture / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
print(json.dumps({'passed': sum(row['passed'] for row in rows), 'checks': len(rows), 'evidence': str(evidence)}))
