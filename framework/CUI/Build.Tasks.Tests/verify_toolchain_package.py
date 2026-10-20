#!/usr/bin/env python3
"""Installed-package and source-checkout CUI toolchain bootstrap checks.

Builds a real consumer against (a) the exact packaged tool files extracted from a
nupkg and (b) the source-checkout toolchain, proving the compiler task/runtime
toolchain resolves from the installed package layout in one path and from the
source projects in the other. This exercises installed package assets directly;
the separate ``verify_nuget_consumer_dependencies.py`` gate proves a real
PackageReference restore/build against a local feed (no source path escape).

The verifier consumes the typed transport's returned contract: distinct genuine V1
(lowercase keys) and V2 (PascalCase keys) artifacts, plus the legacy single-file
V1 layout, whichever the actual build emitted. Manifest resources are enumerated
authoritatively through a tiny compiled C# probe rather than raw byte scans. A
stateless -> stateful input transition proves stateful source is never
raw-shipped under a V1 identity (it must move to a distinct V2 artifact).

Every SDK invocation uses the provided guarded dotnet (--dotnet) with -m:1.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile
from xml.sax.saxutils import escape

STATELESS = '<Page ID="stateless"><Text Text="Packaged real source"/></Page>'
STATEFUL = ('<Cui><Variable Type="bool" ID="Open">False</Variable>\n'
            '<Actions><Action name="toggle"><Toggle Target="Open"/></Action></Actions>\n'
            '<Page><Button Action="toggle" Content="Aa"/></Page></Cui>')

p = argparse.ArgumentParser()
p.add_argument('--package', type=Path, required=True)
p.add_argument('--evidence', type=Path, required=True)
p.add_argument('--dotnet', type=Path, default=Path('/home/jacob/.local/bin/dotnet'))
p.add_argument('--probe-dotnet', type=Path, default=None)
a = p.parse_args()

e = a.evidence.resolve()
e.mkdir(parents=True, exist_ok=False)
root = Path(__file__).resolve().parents[1]
package = a.package.resolve()

installed = e / 'installed-package'
with zipfile.ZipFile(package) as z:
    z.extractall(installed)

probe_dotnet = (a.probe_dotnet or a.dotnet).resolve()

rows = []


def get(obj, *keys):
    for key in keys:
        if isinstance(obj, dict) and key in obj:
            return obj[key]
    return None


def record(name, status, **detail):
    rows.append(dict(name=name, status=status, **detail))
    (e / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
    print(name, status.upper(), flush=True)
    return status == 'pass'


def check(name, passed, **detail):
    return record(name, 'pass' if passed else 'fail', **detail)


def discover_artifacts(cui_dir):
    found = {}
    for name in ('compilation.v2.cuic.json', 'compilation.v1.cuic.json', 'compilation.cuic.json'):
        path = cui_dir / name
        if path.exists():
            found[name] = path
    return found


def read_artifact(path):
    data = json.loads(path.read_text(encoding='utf-8'))
    version = get(data, 'FormatVersion', 'formatVersion')
    if version == 2:
        return 'v2', data
    if version == 1:
        return 'v1', data
    return 'legacy', data


def compiler_hash(artifact):
    return get(artifact, 'CompilerSha256', 'compilerSha256')


def doc_sources(version, artifact):
    docs = get(artifact, 'Documents', 'documents') or []
    sources = set()
    for doc in docs:
        source = get(doc, 'Source', 'source')
        if source is not None:
            sources.add(source)
    return sources


def doc_runtime_abis(version, artifact):
    docs = get(artifact, 'Documents', 'documents') or []
    abis = {}
    for doc in docs:
        source = get(doc, 'Source', 'source')
        if version == 'v2':
            meta = get(doc, 'Metadata', 'metadata') or {}
        else:
            comp = get(doc, 'Compilation', 'compilation') or {}
            meta = get(comp, 'Metadata', 'metadata') or {}
        abis[source] = get(meta, 'RuntimeAbiVersion', 'runtimeAbiVersion')
    return abis


def build(name, project, expected, diagnostic=None, extra=()):
    cmd = [str(a.dotnet), 'build', str(project), '-m:1', '--verbosity', 'minimal', *extra]
    result = subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (name + '.log')).write_text(result.stdout)
    (e / (name + '.exit')).write_text(str(result.returncode) + '\n')
    ok = (result.returncode == 0) == expected and (not diagnostic or diagnostic in result.stdout)
    check(name, ok, command=cmd, exit=result.returncode, diagnostic=diagnostic)
    return result.stdout


def run_probe(dll, label):
    """Authoritative assembly-manifest enumeration via a tiny compiled C# probe."""
    probe = e / 'probe'
    (probe / 'Probe.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    (probe / 'Program.cs').write_text(
        'using System;\nusing System.Reflection;\nclass Program\n{\n'
        '    static int Main(string[] args)\n    {\n'
        '        var a = Assembly.LoadFrom(args[0]);\n'
        '        foreach (var n in a.GetManifestResourceNames()) Console.WriteLine(n);\n'
        '        return 0;\n    }\n}\n')
    run = subprocess.run([str(probe_dotnet), 'run', '--project', str(probe / 'Probe.csproj'),
                          '--', str(dll)],
                         cwd=probe, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (label + '-probe.log')).write_text(run.stdout)
    if run.returncode != 0:
        raise RuntimeError('resource probe failed; inspect ' + label + '-probe.log')
    return {line.strip() for line in run.stdout.splitlines() if line.strip()}


def compiler_dll_candidates(mode):
    if mode == 'package':
        return [installed / 'tools/compiler/CakeOS.Cui.Compiler.dll']
    return [root / 'Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Compiler.dll',
            root / 'Compiler/bin/Debug/net10.0/CakeOS.Cui.Compiler.dll']


V1_RESOURCE = 'CUI.Compilation.v1'
V2_RESOURCE = 'CUI.Compilation.v2'

for mode, directory in [('package', installed), ('source', root / 'Compiler')]:
    f = e / (mode + '-consumer')
    f.mkdir()
    source = f / 'Main.cui'

    def write_source(text):
        source.write_text(text)

    write_source(STATELESS)
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
    try:
        choice = (f / 'toolchain.txt').read_text()
    except FileNotFoundError:
        choice = ''
    if mode == 'package':
        check(mode + '-packaged-tools-selected',
              str(installed) in choice and choice.splitlines()[0] == 'true')
        check(mode + '-package-has-no-source-project-dependency', '.csproj' not in choice)
    else:
        check(mode + '-source-tool-project-selected',
              'CakeOS.Cui.Build.Compiler.csproj' in choice and choice.splitlines()[0] != 'true')

    cui_dir = f / 'obj/Debug/net10.0/Cui'
    artifacts = discover_artifacts(cui_dir)
    check(mode + '-artifact-produced', len(artifacts) > 0, found=sorted(artifacts))

    # Compiler identity must match the compiler assembly actually used by the task.
    for aname, apath in artifacts.items():
        version, artifact = read_artifact(apath)
        expected_hash = compiler_hash(artifact)
        matched = False
        for candidate in compiler_dll_candidates(mode):
            if candidate.exists() and hashlib.sha256(candidate.read_bytes()).hexdigest() == expected_hash:
                matched = True
                break
        check(f'{mode}-{aname}-compiler-identity-matches', bool(expected_hash) and matched)

    assembly = f / 'bin/Debug/net10.0/Consumer.dll'
    if not assembly.exists():
        record(mode + '-baseline-missing-consumer-assembly', 'fail',
               reason='baseline build did not produce the consumer assembly')
        continue
    names = run_probe(assembly, mode + '-baseline')
    check(mode + '-genuine-v1-resource-embedded', V1_RESOURCE in names)
    if any('v2' in n for n in artifacts):
        check(mode + '-distinct-v2-resource-embedded', V2_RESOURCE in names)
    else:
        record(mode + '-distinct-v2-resource-embedded', 'blocked',
               reason='no V2 artifact emitted by actual transport contract')

    check(mode + '-build-tools-not-app-runtime',
          not (f / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())

    # Stateless -> stateful transition: stateful raw source must not be raw-shipped
    # under a V1 identity; it must move to a distinct V2 artifact.
    write_source(STATEFUL)
    tproc = subprocess.run([str(a.dotnet), 'build', str(project), '-m:1',
                            '--verbosity', 'minimal'],
                           text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (mode + '-transition-stateful.log')).write_text(tproc.stdout)
    if tproc.returncode != 0:
        record(mode + '-stateful-transition', 'blocked',
               reason='compiler did not accept the stateful input at the detected transport contract')
    else:
        artifacts_after = discover_artifacts(cui_dir)
        v2_after = get(artifacts_after, 'compilation.v2.cuic.json')
        v1_after = get(artifacts_after, 'compilation.v1.cuic.json')
        if v2_after is None:
            record(mode + '-stateful-not-raw-shipped-under-v1', 'blocked',
                   reason='typed V2 transport not detected; cannot prove the transition')
        else:
            _, v2 = read_artifact(v2_after)
            v2_docs = doc_sources('v2', v2)
            stateful_in_v2 = 'Main.cui' in v2_docs
            if v1_after is not None:
                _, v1 = read_artifact(v1_after)
                v1_docs = doc_sources('v1', v1)
                stateful_in_v1 = 'Main.cui' in v1_docs
            else:
                v1_docs = set()
                stateful_in_v1 = False
            names_after = run_probe(assembly, mode + '-transition')
            check(mode + '-stateful-promoted-to-v2', stateful_in_v2)
            check(mode + '-stateful-absent-from-v1', not stateful_in_v1)
            check(mode + '-v2-resource-embedded-after-transition', V2_RESOURCE in names_after)
            if stateful_in_v2 and not stateful_in_v1 and V2_RESOURCE in names_after:
                # Validate the stateful V2 document is ABI2 (not raw source under V1).
                abis = doc_runtime_abis('v2', v2)
                check(mode + '-stateful-v2-abi2', abis.get('Main.cui') == '2')
            elif not stateful_in_v2 and not stateful_in_v1:
                record(mode + '-stateful-v2-abi2', 'blocked',
                       reason='build did not compile stateful source as V2 (compiler/task mismatch)')

# Legacy single-file V1 compatibility: still consumed when present (sparse base).
# (No explicit check here; discover_artifacts already folds compilation.cuic.json into
#  the per-mode artifact loop above if the actual build emits it.)

print(json.dumps({
    'checks': len(rows),
    'passed': sum(r['status'] == 'pass' for r in rows),
    'blocked': sum(r['status'] == 'blocked' for r in rows),
    'failed': sum(r['status'] == 'fail' for r in rows),
    'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
}))
raise SystemExit(0 if all(r['status'] == 'pass' for r in rows) else 2)
