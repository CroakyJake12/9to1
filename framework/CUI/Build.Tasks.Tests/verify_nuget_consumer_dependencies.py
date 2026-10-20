#!/usr/bin/env python3
"""Real installed-package dependency gate for the CUI compiler toolchain.

Restores and builds an ORDINARY fresh consumer that references the built CUI
package (CakeOS.Cui) via a normal PackageReference and standard SDK imports, from a
local NuGet feed only (offline). It proves:

  - the exact package id/version resolves; the generated nuspec dependency groups are
    clean (no leaked Core/Language/Runtime/desktop-runtime package dependencies) —
    the toolchain is self-contained under tools/;
  - the compiler task and its runtime dependencies resolve entirely from the package/
    feed (no source-project path, no direct Import escape, no source-checkout
    fallback);
  - the emitted artifact's compiler identity matches the compiler DLL actually packed
    in tools/compiler;
  - tools are build-tool assets, not application runtime controls (no lib/ package
    content, none of the task assemblies leak into the consumer's app output);
  - native/minimal net10 consumer builds cleanly; a browser/WASM-targeted
    dependency-inspection consumer is exercised only to the actual supported boundary
    (precise unsupported/missing-dependency diagnostic, never an invented PASS).

Every SDK invocation uses the provided guarded dotnet (--dotnet), -m:1, and an
isolated package cache under the evidence directory.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

STATELESS = '<Page ID="stateless"><Text Text="PackageReference real source"/></Page>'
STATEFUL = ('<Cui><Variable Type="bool" ID="Open">False</Variable>\n'
            '<Actions><Action name="toggle"><Toggle Target="Open"/></Action></Actions>\n'
            '<Page><Button Action="toggle" Content="Aa"/></Page></Cui>')

parser = argparse.ArgumentParser()
parser.add_argument('--package', type=Path, required=True)
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--dotnet', type=Path, default=Path('/home/jacob/.local/bin/dotnet'))
parser.add_argument('--probe-dotnet', type=Path, default=None)
parser.add_argument('--expected-package-id', default='CakeOS.Cui')
args = parser.parse_args()

e = args.evidence.resolve()
e.mkdir(parents=True, exist_ok=False)
package = args.package.resolve()
dotnet = args.dotnet.resolve()
probe_dotnet = (args.probe_dotnet or dotnet).resolve()
expected_id = args.expected_package_id

feed = e / 'local-feed'
feed.mkdir()
installed = e / 'installed-package'
cache = e / 'packages'          # isolated NUGET_PACKAGES cache
rows = []


def record(name, status, **detail):
    rows.append(dict(name=name, status=status, **detail))
    (e / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
    print(name, status.upper(), flush=True)
    return status == 'pass'


def check(name, passed, **detail):
    return record(name, 'pass' if passed else 'fail', **detail)


def get(obj, *keys):
    for key in keys:
        if isinstance(obj, dict) and key in obj:
            return obj[key]
    return None


def run_dotnet(command, cwd, label, expect_success=True):
    env = dict(os.environ)
    env['NUGET_PACKAGES'] = str(cache)
    proc = subprocess.run(command, cwd=cwd, text=True, env=env,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (label + '.log')).write_text(proc.stdout)
    (e / (label + '.exit')).write_text(str(proc.returncode) + '\n')
    ok = (proc.returncode == 0) == expect_success
    return proc, ok


# --- 1. extract package identity from the bundled nuspec, set up local feed ----
with zipfile.ZipFile(package) as z:
    nuspec_entry = next(n for n in z.namelist() if n.lower().endswith('.nuspec'))
    nuspec_text = z.read(nuspec_entry).decode('utf-8')
    z.extractall(installed)

shutil.copy(package, feed / package.name)

m_id = re.search(r'<id>\s*(.*?)\s*</id>', nuspec_text, re.S | re.I)
m_ver = re.search(r'<version>\s*(.*?)\s*</version>', nuspec_text, re.S | re.I)
pkg_id = m_id.group(1) if m_id else None
pkg_ver = m_ver.group(1) if m_ver else None
check('package-id-matches-expected', pkg_id == expected_id, id=pkg_id, expected=expected_id)
record('package-version-resolved', 'pass' if pkg_ver else 'fail', version=pkg_ver,
       required=f'{expected_id}@{pkg_ver}')
if not (pkg_id and pkg_ver):
    raise RuntimeError('could not determine package id/version from nuspec')

# --- 2. nuspec dependency groups + packed assets inspection ----------------------
deps_section = re.search(r'<dependencies\b.*?</dependencies>', nuspec_text, re.S | re.I)
dep_entries = []
if deps_section:
    dep_entries = re.findall(r'<dependency\b[^>]*?id="([^"]+)"[^>]*?>', deps_section.group(0), re.I)
check('nuspec-dependency-groups-clean', len(dep_entries) == 0, dependencies=dep_entries)
dev_dep = re.search(r'<developmentDependency>\s*(true)\s*</developmentDependency>', nuspec_text, re.I)
check('nuspec-development-dependency', bool(dev_dep))

archive_names = set()
with zipfile.ZipFile(package) as z:
    archive_names = set(z.namelist())
required_assets = {
    'tools/compiler/CakeOS.Cui.Build.Compiler.dll',
    'tools/compiler/CakeOS.Cui.Compiler.dll',
    'tools/legacy/CakeOS.Cui.Build.Tasks.dll',
    'buildTransitive/CakeOS.Cui.props',
    'buildTransitive/CakeOS.Cui.targets',
}
check('packed-toolchain-assets-present', required_assets <= archive_names,
      missing=sorted(required_assets - archive_names))
lib_entries = [n for n in archive_names if n.startswith('lib/')]
check('package-has-no-lib-runtime-assets', len(lib_entries) == 0, lib=lib_entries)
# Honesty: the package must not claim/force a browser/WASM runtime dependency.
web_deps = [d for d in dep_entries if re.search(r'wasm|browser|web|blazor', d, re.I)]
check('no-web-wasm-browser-dependency-claimed', not web_deps, suspicious=web_deps)
wasm_assets = [n for n in archive_names if re.search(r'wasm|browser|blazor', n, re.I)]
check('no-wasm-browser-assets-shipped', not wasm_assets, suspicious=wasm_assets)


def compiler_dll_path():
    return installed / 'tools/compiler/CakeOS.Cui.Compiler.dll'


def discover_artifacts(cui_dir):
    found = {}
    for name in ('compilation.v2.cuic.json', 'compilation.v1.cuic.json', 'compilation.cuic.json'):
        p = cui_dir / name
        if p.exists():
            found[name] = p
    return found


def read_artifact(path):
    data = json.loads(path.read_text(encoding='utf-8'))
    version = get(data, 'FormatVersion', 'formatVersion')
    if version == 2:
        return 'v2', data
    if version == 1:
        return 'v1', data
    return 'legacy', data


def run_probe(dll, label):
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


def consumer_csproj(project, name, extra_props=''):
    project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework>{extra_props}</PropertyGroup>
<ItemGroup>
<PackageReference Include="{escape(pkg_id)}" Version="{escape(pkg_ver)}" />
<EmbeddedResource Include="Main.cui" LogicalName="App.Main"/>
</ItemGroup>
<Target Name="RecordCuiToolchain" BeforeTargets="CompileCuiSources">
<WriteLinesToFile File="$(MSBuildProjectDirectory)/toolchain.txt" Lines="$(CuiUsePrebuiltCompiler);$(CuiCompilerTaskAssembly)" Overwrite="true"/>
</Target>
</Project>''')


def write_nuget_config(consumer_dir):
    # Offline: only the local feed; no registries, no audit traffic.
    (consumer_dir / 'NuGet.Config').write_text(f'''<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="{escape(str(feed))}" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local-feed">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <auditSources>
    <clear />
  </auditSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
</configuration>
''')


# --- 3. native/minimal net10 consumer -------------------------------------------
consumer = e / 'consumer'
consumer.mkdir()
write_nuget_config(consumer)
(consumer / 'Main.cui').write_text(STATELESS)
consumer_csproj(consumer / 'Consumer.csproj', consumer)
proj = consumer / 'Consumer.csproj'

_, ok = run_dotnet([str(dotnet), 'restore', str(proj), '-m:1'], consumer, 'restore-native')
check('native-restore-succeeds', ok)

assets_path = consumer / 'obj/project.assets.json'
if assets_path.exists():
    assets = json.loads(assets_path.read_text(encoding='utf-8'))
    frame = get(assets, 'project.frameworks.net10.0') or {}
    deps = get(frame, 'dependencies') or {}
    check('native-resolved-required-package',
          any(k.lower() == pkg_id.lower() and get(v, 'target') == pkg_ver for k, v in deps.items()),
          dependencies={k: get(v, 'target') for k, v in deps.items()})
    libraries = get(assets, 'libraries') or {}
    lib_keys = [k for k in libraries if not k.startswith('.NETCoreApp')]
    # Clean closure: the only NuGet library pulled must be the CUI package itself.
    extra_libs = [k for k in lib_keys if k.lower() != f'{pkg_id.lower()}/{pkg_ver.lower()}']
    check('native-clean-closure-only-cui-package', len(extra_libs) == 0, libraries=lib_keys)
    pkg_lib = get(libraries, f'{pkg_id}/{pkg_ver}')
    check('native-package-path-resolves-from-cache',
          bool(pkg_lib) and 'framework' not in str(get(pkg_lib, 'path') or '').lower(),
          path=get(pkg_lib, 'path'))
    cache_pkg_tool = cache / f'{pkg_id.lower()}/{pkg_ver.lower()}/tools/compiler/CakeOS.Cui.Compiler.dll'
    check('native-restore-populated-isolated-cache-from-feed', cache_pkg_tool.exists(),
          path=str(cache_pkg_tool))
else:
    record('native-assets-not-produced', 'fail',
           reason='restore did not produce project.assets.json')

_, ok = run_dotnet([str(dotnet), 'build', str(proj), '-m:1', '--verbosity', 'minimal'],
                   consumer, 'build-native')
check('native-build-succeeds', ok)

choice_path = consumer / 'toolchain.txt'
if choice_path.exists():
    choice = choice_path.read_text().splitlines()
    choice0 = choice[0].strip() if choice else ''
    choice1 = choice[1].strip() if len(choice) > 1 else ''
    check('native-prebuilt-toolchain-selected', choice0 == 'true')
    check('native-compiler-from-installed-package',
          'tools/compiler/CakeOS.Cui.Build.Compiler.dll' in choice1
          and '/framework/' not in choice1.replace('\\', '/'), compiler_assembly=choice1)
else:
    record('native-toolchain-choice-not-recorded', 'fail',
           reason='toolchain.txt was not produced')

cui_dir = consumer / 'obj/Debug/net10.0/Cui'
artifacts = discover_artifacts(cui_dir)
check('native-artifact-produced', len(artifacts) > 0, found=sorted(artifacts))
for aname, apath in artifacts.items():
    version, artifact = read_artifact(apath)
    expected_hash = get(artifact, 'CompilerSha256', 'compilerSha256')
    actual_hash = hashlib.sha256(compiler_dll_path().read_bytes()).hexdigest()
    check(f'native-{aname}-compiler-identity-matches-packaged-dll',
          bool(expected_hash) and expected_hash == actual_hash)

dll = consumer / 'bin/Debug/net10.0/Consumer.dll'
if not dll.exists():
    record('native-missing-consumer-assembly', 'fail',
           reason='native build did not produce the consumer assembly')
else:
    names = run_probe(dll, 'native')
    check('native-genuine-v1-resource-embedded', 'CUI.Compilation.v1' in names)

# Tools must not become app runtime controls.
check('native-task-assembly-not-in-app-output',
      not (consumer / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
check('native-compiler-assembly-not-in-app-output',
      not (consumer / 'bin/Debug/net10.0/CakeOS.Cui.Compiler.dll').exists())

# Stateless -> stateful transition on the same consumer.
(consumer / 'Main.cui').write_text(STATEFUL)
trans, trans_ok = run_dotnet(
    [str(dotnet), 'build', str(proj), '-m:1', '--verbosity', 'minimal'],
    consumer, 'build-native-transition', expect_success=False)
if not trans_ok:
    record('native-stateful-transition', 'blocked',
           reason='compiler did not accept the stateful input at the detected transport contract')
else:
    artifacts_after = discover_artifacts(cui_dir)
    v2_after = get(artifacts_after, 'compilation.v2.cuic.json')
    v1_after = get(artifacts_after, 'compilation.v1.cuic.json')
    if v2_after is None:
        record('native-stateful-not-raw-shipped-under-v1', 'blocked',
               reason='typed V2 transport not detected; cannot prove the transition')
    else:
        _, v2 = read_artifact(v2_after)
        v2_docs = {get(d, 'Source') for d in get(v2, 'Documents') or []}
        v1_stateful = False
        if v1_after is not None:
            _, v1 = read_artifact(v1_after)
            v1_docs = {get(d, 'source') for d in get(v1, 'documents') or []}
            v1_stateful = 'Main.cui' in v1_docs
        names_after = run_probe(dll, 'native-transition') if dll.exists() else set()
        check('native-stateful-promoted-to-v2', 'Main.cui' in v2_docs)
        check('native-stateful-absent-from-v1', not v1_stateful)
        check('native-v2-resource-embedded', 'CUI.Compilation.v2' in names_after)
        abis = {}
        for doc in get(v2, 'Documents') or []:
            abis[get(doc, 'Source')] = get(get(doc, 'Metadata') or {}, 'RuntimeAbiVersion')
        check('native-stateful-v2-abi2', abis.get('Main.cui') == '2', abis=abis)

# --- 4. browser/WASM-targeted dependency-inspection/consumer, supported boundary --
wasm = e / 'wasm-consumer'
wasm.mkdir()
write_nuget_config(wasm)
(wasm / 'Main.cui').write_text(STATELESS)
consumer_csproj(wasm / 'Consumer.csproj', wasm,
                extra_props='<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>')
wasm_proj = wasm / 'Consumer.csproj'
wasm_proc, wasm_ok = run_dotnet(
    [str(dotnet), 'restore', str(wasm_proj), '-m:1'], wasm, 'wasm-restore', expect_success=False)
wasm_build_proc = None
wasm_build_ok = False
if wasm_proc.returncode == 0:
    wasm_build_proc, wasm_build_ok = run_dotnet(
        [str(dotnet), 'build', str(wasm_proj), '-m:1', '--verbosity', 'minimal'],
        wasm, 'wasm-build', expect_success=False)

wasm_log = (wasm_build_proc.stdout if wasm_build_proc is not None else wasm_proc.stdout)
if wasm_build_ok:
    wasm_choice = (wasm / 'toolchain.txt').read_text().splitlines() if (wasm / 'toolchain.txt').exists() else []
    wasm_names = run_probe(wasm / 'bin/Debug/net10.0/Consumer.dll', 'wasm')
    check('wasm-compiler-from-installed-package',
          bool(wasm_choice) and wasm_choice[0].strip() == 'true'
          and 'tools/compiler/CakeOS.Cui.Build.Compiler.dll' in wasm_choice[1]
          and '/framework/' not in wasm_choice[1].replace('\\', '/'))
    check('wasm-genuine-v1-resource-embedded', 'CUI.Compilation.v1' in wasm_names)
    check('wasm-no-browser-runtime-injected',
          not (wasm / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
    record('wasm-targeted-consumer-boundary', 'pass',
           detail='compiler toolchain is RID-agnostic and compiled at the supported boundary; '
                  'no web/WASM runtime was claimed or injected')
else:
    # Precise unsupported/missing-dependency diagnostic; never an invented PASS.
    feature = ''
    for token in ('browser-wasm', 'wasm-tools', 'WebAssembly', 'workload', 'RID', 'publish'):
        if token.lower() in wasm_log.lower():
            feature = token
            break
    record('wasm-targeted-consumer-boundary', 'blocked',
           reason='WASM-targeted build did not succeed; unsupported/missing dependency at the '
                  'actual boundary',
           diagnostic=feature or wasm_log.strip().splitlines()[-1][:300] if wasm_log.strip() else '(empty log)')

summary = {
    'required_package': f'{pkg_id}@{pkg_ver}',
    'checks': len(rows),
    'passed': sum(r['status'] == 'pass' for r in rows),
    'blocked': sum(r['status'] == 'blocked' for r in rows),
    'failed': sum(r['status'] == 'fail' for r in rows),
    'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
}
( e / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary))
raise SystemExit(0 if all(r['status'] == 'pass' for r in rows) else 2)
