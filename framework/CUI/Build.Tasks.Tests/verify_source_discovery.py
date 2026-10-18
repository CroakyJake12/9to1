#!/usr/bin/env python3
"""Exercise real SDK/CUI source discovery without hiding explicitly authored inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser()
parser.add_argument('--task-assembly', type=Path, required=True)
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--dotnet', type=Path, default=Path('/home/jacob/.local/bin/dotnet'))
args = parser.parse_args()
evidence = args.evidence.resolve()
evidence.mkdir(parents=True, exist_ok=False)
root = Path(__file__).resolve().parents[1]
fixture = evidence / 'consumer'
fixture.mkdir()
project = fixture / 'Discovery.csproj'
main = fixture / 'Main.cui'
main.write_text('<Page><Text Text="Real source"/></Page>')
rows = []


def project_source(items=''):
    project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><CuiUsePrebuiltCompiler>true</CuiUsePrebuiltCompiler>
<CuiCompilerTaskAssembly>{escape(str(args.task_assembly.resolve()))}</CuiCompilerTaskAssembly></PropertyGroup>
<Import Project="{escape(str(root / 'Compiler/buildTransitive/CakeOS.Cui.props'))}"/>
<ItemGroup>{items}</ItemGroup>
<Import Project="{escape(str(root / 'Compiler/buildTransitive/CakeOS.Cui.targets'))}"/>
</Project>''')


def record(name, passed, **metadata):
    rows.append(dict(name=name, passed=bool(passed), **metadata))
    (evidence / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
    if not passed:
        raise AssertionError(name)


def run(name, good, diagnostic=None, full_build=False):
    command = [str(args.dotnet), 'build' if full_build else 'msbuild', str(project), '-m:1', '-verbosity:minimal']
    if not full_build:
        command.append('-t:CompileCuiSources')
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (evidence / (name + '.log')).write_text(result.stdout)
    (evidence / (name + '.exit')).write_text(str(result.returncode) + '\n')
    record(name, (result.returncode == 0) == good and (not diagnostic or diagnostic in result.stdout),
           command=command, exit=result.returncode, expected_success=good, diagnostic=diagnostic)
    print(name, 'PASS', result.returncode, flush=True)


project_source()
# These represent retained copies from a nested test project's earlier build.
# They stay on disk throughout the test; deletion cannot make discovery pass.
stale_paths = []
for name in ['Tests/bin/Debug/net10.0/UI/Chat/ChatSpace.cui',
             'Tests/bin/Release/net10.0/UI/Chat/ChatSpace.cui',
             'Tests/obj/Debug/net10.0/Legacy.axaml']:
    path = fixture / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('<Page><ForbiddenLegacyControl/></Page>')
    stale_paths.append(path)
run('nested-output-copies-are-not-source', True)
artifact = fixture / 'obj/Debug/net10.0/Cui/compilation.cuic.json'
record('only-authored-source-compiled', [d['source'] for d in json.loads(artifact.read_text())['documents']] == ['Main.cui'])
record('all-stale-copies-preserved', all(p.exists() for p in stale_paths))

# Paths with a similar prefix are normal authored content; no prefix blacklist.
for directory in ['binary-assets', 'objects']:
    path = fixture / directory / 'Invalid.cui'
    path.parent.mkdir()
    path.write_text('<Page><Text Widht="10"/></Page>')
    project_source(f'<EmbeddedResource Include="{escape(str(path))}"/>')
    run('authored-' + directory + '-validated', False, 'CUIC012')
    path.unlink()

# Even exact output-directory names must not exempt an explicitly supplied source.
# Cover local, nested and external locations; both legacy and CUI validation gates.
for label, parent in [('local-bin', fixture / 'bin'),
                      ('nested-bin', fixture / 'Tests/bin'),
                      ('external-bin', evidence / 'outside/bin')]:
    parent.mkdir(parents=True, exist_ok=True)
    for extension, text, diagnostic in [('.axaml', '<Page/>', 'CUIB001'),
                                        ('.cui', '<Page><Text Widht="10"/></Page>', 'CUIC012')]:
        path = parent / ('Authored' + extension)
        path.write_text(text)
        project_source(f'<EmbeddedResource Include="{escape(str(path))}" LogicalName="Explicit.Authored"/>')
        run('explicit-' + label + extension + '-validated', False, diagnostic)
        path.unlink()

# Normal explicit data in such a directory is compiled and embedded, not erased.
valid = evidence / 'outside/bin/Authored.cui'
valid_bytes = b'<Page><Text Text="Explicit resource retained"/></Page>'
valid.write_bytes(valid_bytes)
project_source(f'<EmbeddedResource Include="{escape(str(valid))}" LogicalName="Explicit.Authored"/>')
run('valid-explicit-bin-resource-is-packaged', True, full_build=True)
compiled = json.loads(artifact.read_text())
record('explicit-resource-compiled-hash', any(d['sha256'] == hashlib.sha256(valid_bytes).hexdigest() for d in compiled['documents']))
assembly = (fixture / 'bin/Debug/net10.0/Discovery.dll').read_bytes()
record('explicit-resource-identity-and-bytes-preserved', b'Explicit.Authored' in assembly and valid_bytes in assembly)
record('stale-copies-still-present-after-build', all(p.exists() for p in stale_paths))
print(json.dumps({'checks': len(rows), 'passed': sum(r['passed'] for r in rows)}))
