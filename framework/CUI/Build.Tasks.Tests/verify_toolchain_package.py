#!/usr/bin/env python3
"""Installed-package and source-checkout CUI toolchain bootstrap checks.

Builds a real consumer against (a) the exact packaged tool files extracted from a
nupkg and (b) the source-checkout toolchain, proving the compiler task/runtime
toolchain resolves from the installed package layout in one path and from the
source projects in the other. This exercises installed package assets directly;
the separate ``verify_nuget_consumer_dependencies.py`` gate proves a real
PackageReference restore/build against a local feed (no source path escape).

The verifier consumes the typed transport's returned contract: genuine V1
(stateless) and distinct V2 (stateful) artifacts, plus the legacy single-file V1
layout. A stateless baseline MUST emit V1; V2 is required only after the stateful
transition. Manifest resources are enumerated authoritatively through a tiny
compiled C# probe (no raw byte scans); the actually shipped payload is inspected by
loading the embedded resource text, so stale leftover ``obj`` files under the
compile output are never mistaken for what the shipped assembly embeds.

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
# Distinctive marker of stateful source that must never appear in a raw/V1 payload.
STATEFUL_SENTINEL = '<Variable Type="bool" ID="Open">'

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


def build(name, project, expected, diagnostic=None, extra=()):
    cmd = [str(a.dotnet), 'build', str(project), '-m:1', '--verbosity', 'minimal', *extra]
    result = subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (name + '.log')).write_text(result.stdout)
    (e / (name + '.exit')).write_text(str(result.returncode) + '\n')
    ok = (result.returncode == 0) == expected and (not diagnostic or diagnostic in result.stdout)
    check(name, ok, command=cmd, exit=result.returncode, diagnostic=diagnostic)
    return result.stdout


def probe_program():
    return ('using System;\nusing System.IO;\nusing System.Reflection;\nclass Program\n{\n'
            '    static int Main(string[] args)\n    {\n'
            '        var a = Assembly.LoadFrom(args[0]);\n'
            '        if (args.Length > 2 && args[1] == "--resource")\n'
            '        {\n'
            '            var stream = a.GetManifestResourceStream(args[2]);\n'
            '            if (stream == null) { Console.Error.WriteLine("MISSING"); return 2; }\n'
            '            using var reader = new StreamReader(stream);\n'
            '            Console.Write(reader.ReadToEnd());\n'
            '            return 0;\n'
            '        }\n'
            '        foreach (var n in a.GetManifestResourceNames()) Console.WriteLine(n);\n'
            '        return 0;\n    }\n}\n')


def run_probe(dll, label):
    """Authoritative assembly-manifest enumeration via a tiny compiled C# probe."""
    probe = e / 'probe'
    probe.mkdir(parents=True, exist_ok=True)   # owned probe dir must exist before writes
    (probe / 'Probe.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    (probe / 'Program.cs').write_text(probe_program())
    run = subprocess.run([str(probe_dotnet), 'run', '--project', str(probe / 'Probe.csproj'),
                          '--', str(dll)],
                         cwd=probe, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (label + '-probe.log')).write_text(run.stdout)
    if run.returncode != 0:
        raise RuntimeError('resource probe failed; inspect ' + label + '-probe.log')
    return {line.strip() for line in run.stdout.splitlines() if line.strip()}


def run_probe_resource(dll, resource, label):
    """Read the actually embedded text payload of one named manifest resource."""
    probe = e / 'probe'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'Probe.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    (probe / 'Program.cs').write_text(probe_program())
    run = subprocess.run([str(probe_dotnet), 'run', '--project', str(probe / 'Probe.csproj'),
                          '--', str(dll), '--resource', resource],
                         cwd=probe, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (label + '-probe-resource-' + resource + '.log')).write_text(run.stdout)
    if run.returncode == 2:
        return None  # resource not embedded (MISSING)
    if run.returncode != 0:
        raise RuntimeError('resource probe failed for ' + resource + '; inspect ' + label + '-probe.log')
    return run.stdout


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
    # A stateless baseline is genuine V1 output; V2 is required only after the
    # stateful transition, so its absence here is expected, not a defect.
    check(mode + '-genuine-v1-resource-embedded', V1_RESOURCE in names)
    if V2_RESOURCE in names:
        check(mode + '-distinct-v2-resource-embedded-at-stateless', True)
    else:
        check(mode + '-stateless-baseline-is-v1-only',
              'compilation.v2.cuic.json' not in artifacts)

    check(mode + '-build-tools-not-app-runtime',
          not (f / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())

    # Stateless -> stateful transition: stateful raw source must not be shipped
    # under a V1 identity; it must move to a distinct embedded V2 artifact. The
    # shipped state is read from the assembly manifest/payload, not stale obj files.
    write_source(STATEFUL)
    tproc = subprocess.run([str(a.dotnet), 'build', str(project), '-m:1',
                            '--verbosity', 'minimal'],
                           text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (mode + '-transition-stateful.log')).write_text(tproc.stdout)
    if tproc.returncode != 0:
        record(mode + '-stateful-transition', 'blocked',
               reason='compiler did not accept the stateful input at the detected transport contract')
        continue

    names_after = run_probe(assembly, mode + '-transition')
    if V2_RESOURCE not in names_after:
        record(mode + '-stateful-not-raw-shipped-under-v1', 'blocked',
               reason='distinct V2 artifact not embedded in the shipped assembly; '
                      'typed V2 transport not present/integrated')
        continue

    v2_text = run_probe_resource(assembly, V2_RESOURCE, mode + '-transition')
    v2 = json.loads(v2_text) if v2_text else {}
    docs = get(v2, 'Documents', 'documents') or []
    main_doc = next((d for d in docs if get(d, 'Source', 'source') == 'Main.cui'), None)
    meta = get(main_doc, 'Metadata', 'metadata') or {}
    abi = get(meta, 'RuntimeAbiVersion', 'runtimeAbiVersion')
    check(mode + '-stateful-promoted-to-v2', main_doc is not None)
    check(mode + '-stateful-v2-abi2', abi == '2', abi=abi)

    # Stateful composer must be absent from the actually shipped raw/V1 payload.
    if V1_RESOURCE in names_after:
        v1_text = run_probe_resource(assembly, V1_RESOURCE, mode + '-transition') or ''
        check(mode + '-stateful-absent-from-shipped-v1',
              STATEFUL_SENTINEL not in v1_text and 'Main.cui' not in v1_text)
    else:
        # No V1 resource is shipped at all, so stateful raw is certainly absent.
        check(mode + '-stateful-absent-from-shipped-v1', True)

print(json.dumps({
    'checks': len(rows),
    'passed': sum(r['status'] == 'pass' for r in rows),
    'blocked': sum(r['status'] == 'blocked' for r in rows),
    'failed': sum(r['status'] == 'fail' for r in rows),
    'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
}))
raise SystemExit(0 if all(r['status'] == 'pass' for r in rows) else 2)
