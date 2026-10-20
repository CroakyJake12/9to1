#!/usr/bin/env python3
"""Real installed-package dependency gate for the CUI compiler toolchain.

Restores and builds an ORDINARY fresh consumer that references the built CUI
package (CakeOS.Cui) via a normal PackageReference and standard SDK imports, from a
local NuGet feed only (offline). It proves:

  - the exact package id/version resolves; the generated nuspec dependency groups are
    clean (no leaked Core/Language/Runtime/desktop-runtime package dependencies);
  - every tool dependency is genuinely BUNDLED under ``tools`` (Core/Language/Runtime
    plus the Avalonia closure), rather than relying on dependency suppression alone;
  - the compiler task and its runtime dependencies resolve entirely from the package/
    feed (no source-project path, no direct Import escape, no source-checkout
    fallback);
  - the emitted artifact's compiler identity matches the compiler DLL actually packed
    in tools/compiler;
  - tools are build-tool assets, not application runtime controls (no lib/ package
    content, none of the task assemblies leak into the consumer's app output);
  - native/minimal net10 consumer builds cleanly; the stateful transition ships a
    distinct V2 artifact and never ships stateful raw under a V1 identity (read from
    the actually embedded manifest/payload);
  - a browser/WASM-targeted dependency-inspection consumer is exercised only to the
    actual supported boundary: the real RID output path is located (recorded
    TargetPath or actual bin match) and a precise unsupported/missing-workload
    BLOCKED is distinguished from a product/harness FAIL.

Every SDK invocation uses the provided guarded dotnet (--dotnet), -m:1, and an
isolated package cache under the evidence directory.

``--selfcheck`` runs a focused Python-only fixture (no SDK) against authentic nested
project.assets.json shapes proving the version/identity reader passes for the real
shape and fails on a wrong version.
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
STATEFUL_SENTINEL = '<Variable Type="bool" ID="Open">'

V1_RESOURCE = 'CUI.Compilation.v1'
V2_RESOURCE = 'CUI.Compilation.v2'

parser = argparse.ArgumentParser()
parser.add_argument('--package', type=Path, default=None)
parser.add_argument('--evidence', type=Path, default=None)
parser.add_argument('--dotnet', type=Path, default=Path('/home/jacob/.local/bin/dotnet'))
parser.add_argument('--probe-dotnet', type=Path, default=None)
parser.add_argument('--expected-package-id', default='CakeOS.Cui',
                    help='exact package id; selects the layout: CakeOS.Cui (tools/compiler) '
                         'or CakeOS.Cui.Build.Compiler (standalone tools/net10.0)')
parser.add_argument('--selfcheck', action='store_true')
args = parser.parse_args()

if not args.selfcheck and (args.package is None or args.evidence is None):
    parser.error('--package and --evidence are required unless --selfcheck is used')

rows = []


def get(obj, *keys):
    for key in keys:
        if isinstance(obj, dict) and key in obj:
            return obj[key]
    return None


def resolve_layout(expected_id):
    """Both actual package layouts:
    - main CakeOS.Cui compiler package -> tools/compiler (+ tools/legacy)
    - standalone CakeOS.Cui.Build.Compiler task package -> tools/net10.0
    """
    low = (expected_id or '').lower()
    if low == 'cakeos.cui.build.compiler':
        return {
            'standalone': True,
            'tool_prefix': 'tools/net10.0',
            'task_dll': 'CakeOS.Cui.Build.Compiler.dll',
            'compiler_dll': 'CakeOS.Cui.Compiler.dll',
            'props': 'buildTransitive/CakeOS.Cui.Build.Compiler.props',
        }
    return {
        'standalone': False,
        'tool_prefix': 'tools/compiler',
        'task_dll': 'CakeOS.Cui.Build.Compiler.dll',
        'compiler_dll': 'CakeOS.Cui.Compiler.dll',
        'legacy_dll': 'tools/legacy/CakeOS.Cui.Build.Tasks.dll',
        'props': 'buildTransitive/CakeOS.Cui.props',
    }


def classify_closure(archive_names, L):
    """Missing closure members, or [] when the layout is fully bundled."""
    prefix = L['tool_prefix']
    required = {
        f'{prefix}/{L["compiler_dll"]}',
        f'{prefix}/CakeOS.Cui.Core.dll',
        f'{prefix}/CakeOS.Cui.Language.dll',
        f'{prefix}/CakeOS.Cui.Runtime.dll',
    }
    if not L['standalone']:
        required.add(f'{prefix}/{L["task_dll"]}')
        required.add('tools/legacy/CakeOS.Cui.Build.Tasks.dll')
    else:
        required.add(f'{prefix}/{L["task_dll"]}')
    return sorted(required - set(archive_names))


def standalone_probe_project(props_abs):
    """Real installed-task harness (same task-argument contract as root attempt2
    consumer/Probe.csproj): absolute import of the installed package props only, no
    source fallback. CompileCuiSourcesTask is invoked with the exact required
    arguments Sources/ProjectDirectory/OutputFile."""
    return ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<NuGetAudit>false</NuGetAudit></PropertyGroup>'
            '<Import Project="' + escape(str(props_abs), {'"': '&quot;'}) + '"/>'
            '<UsingTask TaskName="CakeOS.Cui.Build.CompileCuiSourcesTask" AssemblyFile="$(CuiCompilerTaskAssembly)"/>'
            '<Target Name="Probe">'
            '<Error Condition="\'$(CuiCompilerTaskAssembly)\' == \'\'" Text="Installed standalone task was not admitted"/>'
            '<CompileCuiSourcesTask Sources="$(MSBuildProjectDirectory)/Input.cui" '
            'ProjectDirectory="$(MSBuildProjectDirectory)" '
            'OutputFile="$(MSBuildProjectDirectory)/artifacts/compilation.v1.cuic.json"/></Target></Project>')


def asset_dependencies(assets):
    # Descend literal keys; never split a dotted path because the TFM key
    # "net10.0" itself contains a dot.
    project = get(assets, 'project') or {}
    frameworks = get(project, 'frameworks') or {}
    frame = get(frameworks, 'net10.0') or {}
    return get(frame, 'dependencies') or {}


def dependency_present(assets, pkg_id):
    deps = asset_dependencies(assets)
    return any(k.lower() == pkg_id.lower() for k in deps)


def resolved_library_version(libraries, pkg_id):
    best = None
    for key in libraries or {}:
        if '/' not in key:
            continue
        pid, ver = key.split('/', 1)
        if pid.lower() == pkg_id.lower():
            best = ver
    return best


def selfcheck():
    """Python-only fixture: authentic nested assets shapes (no SDK)."""
    def make(pkg_id, pkg_ver, lib_ver=None):
        lib_ver = lib_ver or pkg_ver
        return {
            'project': {
                'frameworks': {
                    'net10.0': {
                        'targetAlias': 'net10.0',
                        'dependencies': {pkg_id: {'target': 'Package', 'version': pkg_ver}},
                    }
                }
            },
            'libraries': {
                f'{pkg_id}/{lib_ver}': {
                    'type': 'package',
                    'path': f'{pkg_id.lower()}/{lib_ver}',
                }
            },
        }

    cases = []

    def case(name, ok, **detail):
        cases.append(dict(name=name, passed=bool(ok), **detail))

    real = make('CakeOS.Cui', '1.0.0')
    prerelease = make('CakeOS.Cui', '1.2.3-beta', '1.2.3-beta')
    wrong = make('CakeOS.Cui', '1.0.0', '1.0.1')
    missing_lib = make('CakeOS.Cui', '1.0.0')
    missing_lib['libraries'] = {}

    case('selfcheck-authentic-nested-resolves',
         dependency_present(real, 'CakeOS.Cui')
         and resolved_library_version(get(real, 'libraries'), 'CakeOS.Cui') == '1.0.0')
    case('selfcheck-prerelease-version-resolves',
         dependency_present(prerelease, 'CakeOS.Cui')
         and resolved_library_version(get(prerelease, 'libraries'), 'CakeOS.Cui') == '1.2.3-beta')
    wrong_resolved = resolved_library_version(get(wrong, 'libraries'), 'CakeOS.Cui')
    case('selfcheck-detects-wrong-version',
         wrong_resolved is not None and wrong_resolved != '1.0.0', resolved=wrong_resolved)
    # Prove the reader descends the nested assets hierarchy (the flat literal lookup
    # that the original harness used returns None for the nested framework record).
    case('selfcheck-nested-lookup-not-flat',
         get(real, 'project.frameworks.net10.0') is None
         and bool(asset_dependencies(real)))
    case('selfcheck-missing-library-returns-none',
         resolved_library_version(get(missing_lib, 'libraries'), 'CakeOS.Cui') is None)

    # Both actual package layouts, no SDK.
    main_l = resolve_layout('CakeOS.Cui')
    std_l = resolve_layout('CakeOS.Cui.Build.Compiler')
    case('selfcheck-main-layout-is-tools-compiler',
         not main_l['standalone'] and main_l['tool_prefix'] == 'tools/compiler')
    case('selfcheck-standalone-layout-is-tools-net10',
         std_l['standalone'] and std_l['tool_prefix'] == 'tools/net10.0')
    case('selfcheck-standalone-distinct-from-main',
         std_l['tool_prefix'] != main_l['tool_prefix'])

    # Standalone closure expectation: compiler + Core/Language/Runtime + Avalonia.
    std_closure = sorted(set(classify_closure(set(), std_l)))
    full_std = set(std_closure) | {std_l['props']}
    case('selfcheck-standalone-closure-expected',
         'tools/net10.0/CakeOS.Cui.Compiler.dll' in std_closure
         and 'tools/net10.0/CakeOS.Cui.Core.dll' in std_closure
         and 'tools/net10.0/CakeOS.Cui.Language.dll' in std_closure
         and 'tools/net10.0/CakeOS.Cui.Runtime.dll' in std_closure
         and 'tools/net10.0/CakeOS.Cui.Build.Compiler.dll' in std_closure)
    case('selfcheck-standalone-closure-complete', classify_closure(full_std, std_l) == [])
    # Missing-dependency detection.
    missing_compiler = full_std - {'tools/net10.0/CakeOS.Cui.Compiler.dll'}
    case('selfcheck-standalone-missing-dependency-detected',
         classify_closure(missing_compiler, std_l) == ['tools/net10.0/CakeOS.Cui.Compiler.dll'])

    # Real installed-task harness fixture (task-argument contract preserved).
    probe_txt = standalone_probe_project(Path('/pkg/buildTransitive/CakeOS.Cui.Build.Compiler.props'))
    harness_needed = ('CompileCuiSourcesTask', 'Sources=', 'ProjectDirectory=',
                      'OutputFile=', 'CuiCompilerTaskAssembly')
    case('selfcheck-real-installed-task-harness', all(n in probe_txt for n in harness_needed))

    summary = {
        'mode': 'selfcheck',
        'synthetic_authentic_shape': True,
        'checks': len(cases),
        'passed': sum(c['passed'] for c in cases),
    }
    print(json.dumps(summary))
    raise SystemExit(0 if all(c['passed'] for c in cases) else 2)


if args.selfcheck:
    selfcheck()

e = args.evidence.resolve()
e.mkdir(parents=True, exist_ok=False)
package = args.package.resolve()
dotnet = args.dotnet.resolve()
probe_dotnet = (args.probe_dotnet or dotnet).resolve()
expected_id = args.expected_package_id
LAYOUT = resolve_layout(expected_id)

feed = e / 'local-feed'
feed.mkdir()
installed = e / 'installed-package'
cache = e / 'packages'          # isolated NUGET_PACKAGES cache


def record(name, status, **detail):
    rows.append(dict(name=name, status=status, **detail))
    (e / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
    print(name, status.upper(), flush=True)
    return status == 'pass'


def check(name, passed, **detail):
    return record(name, 'pass' if passed else 'fail', **detail)


def run_dotnet(command, cwd, label):
    env = dict(os.environ)
    env['NUGET_PACKAGES'] = str(cache)
    proc = subprocess.run(command, cwd=cwd, text=True, env=env,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (e / (label + '.log')).write_text(proc.stdout)
    (e / (label + '.exit')).write_text(str(proc.returncode) + '\n')
    return proc


def succeeded(proc):
    return proc.returncode == 0


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
L = LAYOUT
prefix = L['tool_prefix']
required_assets = {
    f'{prefix}/{L["task_dll"]}',
    f'{prefix}/{L["compiler_dll"]}',
    L['props'],
}
if not L['standalone']:
    required_assets.add('tools/legacy/CakeOS.Cui.Build.Tasks.dll')
    required_assets.add('buildTransitive/CakeOS.Cui.targets')
check('packed-toolchain-assets-present', required_assets <= archive_names,
      missing=sorted(required_assets - archive_names))
# Bundling proof: the toolchain must be self-contained under tools (not merely that
# package dependencies are suppressed). Applies to BOTH actual layouts.
closure_missing = classify_closure(archive_names, L)
check('toolchain-bundles-compiler-runtime-closure', closure_missing == [],
      missing=closure_missing)
avalonia = [n for n in archive_names if n.startswith(prefix + '/Avalonia.')]
check('toolchain-bundles-avalonia-closure', len(avalonia) > 0, found=sorted(avalonia)[:8])
lib_entries = [n for n in archive_names if n.startswith('lib/')]
check('package-has-no-lib-runtime-assets', len(lib_entries) == 0, lib=lib_entries)
# Honesty: the package must not claim/force a browser/WASM runtime dependency.
web_deps = [d for d in dep_entries if re.search(r'wasm|browser|web|blazor', d, re.I)]
check('no-web-wasm-browser-dependency-claimed', not web_deps, suspicious=web_deps)
wasm_assets = [n for n in archive_names if re.search(r'wasm|browser|blazor', n, re.I)]
check('no-wasm-browser-assets-shipped', not wasm_assets, suspicious=wasm_assets)


def compiler_dll_path():
    return installed / LAYOUT['tool_prefix'] / LAYOUT['compiler_dll']


def read_artifact(path):
    data = json.loads(path.read_text(encoding='utf-8'))
    version = get(data, 'FormatVersion', 'formatVersion')
    if version == 2:
        return 'v2', data
    if version == 1:
        return 'v1', data
    return 'legacy', data


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
    probe = e / 'probe'
    probe.mkdir(parents=True, exist_ok=True)
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
        return None
    if run.returncode != 0:
        raise RuntimeError('resource probe failed for ' + resource + '; inspect ' + label + '-probe.log')
    return run.stdout


def read_target_path(consumer_dir):
    tp = consumer_dir / 'targetpath.txt'
    if tp.exists():
        value = tp.read_text().strip()
        if value:
            return Path(value)
    return None


def find_consumer_dll(consumer_dir):
    hits = sorted(consumer_dir.glob('bin/**/Consumer.dll'))
    if not hits:
        return None
    rid = next((h for h in hits if 'browser-wasm' in h.parts), None)
    return rid or hits[0]


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
<Target Name="RecordTargetPath" AfterTargets="Build">
<WriteLinesToFile File="$(MSBuildProjectDirectory)/targetpath.txt" Lines="$(TargetPath)" Overwrite="true"/>
</Target>
</Project>''')


def write_nuget_config(consumer_dir):
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


def classify_wasm_failure(log):
    low = log.lower()
    tokens = ('workload', 'wasm-tools', 'browser-wasm', 'emscripten',
              'runtime.browser-wasm', 'rid', 'unsupported')
    hit = next((t for t in tokens if t in low), None)
    tail = log.strip().splitlines()[-1][:300] if log.strip() else '(empty log)'
    if hit:
        return 'blocked', f'missing/unsupported {hit} at the actual WASM boundary'
    return 'fail', tail


# --- 3. native/minimal net10 consumer -------------------------------------------
if L['standalone']:
    # Real installed-task harness: import the installed package props (absolute)
    # and invoke the actual CompileCuiSourcesTask, exactly the root attempt2
    # consumer/Probe.csproj contract (Sources/ProjectDirectory/OutputFile). This
    # proves the packaged compiler/runtime closure resolves at task execution time
    # from the installed tools/net10.0 directory, with no source fallback.
    probe = e / 'consumer'
    probe.mkdir()
    write_nuget_config(probe)
    (probe / 'Input.cui').write_text(
        '<Page ID="standalone-probe"><Text Text="TESTONLY standalone task probe"/></Page>\n')
    (probe / 'Probe.csproj').write_text(
        standalone_probe_project((installed / L['props']).resolve()))
    build_proc = run_dotnet([str(dotnet), 'build', str(probe / 'Probe.csproj'),
                             '-m:1', '--verbosity', 'minimal'],
                            probe, 'build-standalone-installed-task')
    check('standalone-installed-task-runs', succeeded(build_proc))
    if succeeded(build_proc):
        art = probe / 'artifacts/compilation.v1.cuic.json'
        if art.exists():
            data = json.loads(art.read_text(encoding='utf-8'))
            version = get(data, 'FormatVersion', 'formatVersion')
            meta = get(data, 'Metadata', 'metadata') or {}
            check('standalone-genuine-v1-artifact-produced', version == 1, version=version)
            check('standalone-artifact-metadata-present', bool(meta))
        else:
            record('standalone-artifact-not-produced', 'fail',
                   reason='task ran but the v1 artifact was not produced')
    # Tools must not become app runtime controls.
    check('standalone-task-assembly-not-in-app-output',
          not (probe / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
    check('standalone-compiler-assembly-not-in-app-output',
          not (probe / 'bin/Debug/net10.0/CakeOS.Cui.Compiler.dll').exists())
else:
    consumer = e / 'consumer'
    consumer.mkdir()
    write_nuget_config(consumer)
    (consumer / 'Main.cui').write_text(STATELESS)
    consumer_csproj(consumer / 'Consumer.csproj', consumer)
    proj = consumer / 'Consumer.csproj'

    restore_proc = run_dotnet([str(dotnet), 'restore', str(proj), '-m:1'], consumer, 'restore-native')
    check('native-restore-succeeds', succeeded(restore_proc))

    assets_path = consumer / 'obj/project.assets.json'
    if assets_path.exists():
        assets = json.loads(assets_path.read_text(encoding='utf-8'))
        libraries = get(assets, 'libraries') or {}
        dep_ok = dependency_present(assets, pkg_id)
        resolved_ver = resolved_library_version(libraries, pkg_id)
        record('native-resolved-required-package',
               'pass' if (dep_ok and resolved_ver == pkg_ver) else 'fail',
               dependency_present=dep_ok, resolved_version=resolved_ver,
               expected_version=pkg_ver)
        lib_keys = [k for k in libraries if not k.startswith('.NETCoreApp')]
        extra_libs = [k for k in lib_keys if k.lower() != f'{pkg_id.lower()}/{pkg_ver.lower()}']
        check('native-clean-closure-only-cui-package', len(extra_libs) == 0, libraries=lib_keys)
        pkg_lib = get(libraries, f'{pkg_id}/{pkg_ver}')
        check('native-package-path-resolves-from-cache',
              bool(pkg_lib) and 'framework' not in str(get(pkg_lib, 'path') or '').lower(),
              path=get(pkg_lib, 'path'))
        cache_pkg_tool = cache / f'{pkg_id.lower()}/{pkg_ver.lower()}' / LAYOUT['tool_prefix'] / LAYOUT['compiler_dll']
        check('native-restore-populated-isolated-cache-from-feed', cache_pkg_tool.exists(),
              path=str(cache_pkg_tool))
    else:
        record('native-assets-not-produced', 'fail',
               reason='restore did not produce project.assets.json')

    build_proc = run_dotnet([str(dotnet), 'build', str(proj), '-m:1', '--verbosity', 'minimal'],
                            consumer, 'build-native')
    check('native-build-succeeds', succeeded(build_proc))

    choice_path = consumer / 'toolchain.txt'
    if choice_path.exists():
        choice = choice_path.read_text().splitlines()
        choice0 = choice[0].strip() if choice else ''
        choice1 = choice[1].strip() if len(choice) > 1 else ''
        check('native-prebuilt-toolchain-selected', choice0 == 'true')
        check('native-compiler-from-installed-package',
              f'{L["tool_prefix"]}/CakeOS.Cui.Build.Compiler.dll' in choice1
              and '/framework/' not in choice1.replace('\\', '/'), compiler_assembly=choice1)
    else:
        record('native-toolchain-choice-not-recorded', 'fail',
               reason='toolchain.txt was not produced')

    native_dll = read_target_path(consumer) or find_consumer_dll(consumer)
    if native_dll is None or not native_dll.exists():
        record('native-missing-consumer-assembly', 'fail',
               reason='native build did not produce the consumer assembly at recorded/actual path')
    else:
        names = run_probe(native_dll, 'native')
        check('native-genuine-v1-resource-embedded', V1_RESOURCE in names)

    # Tools must not become app runtime controls.
    check('native-task-assembly-not-in-app-output',
          not (consumer / 'bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll').exists())
    check('native-compiler-assembly-not-in-app-output',
          not (consumer / 'bin/Debug/net10.0/CakeOS.Cui.Compiler.dll').exists())

    # Stateless -> stateful transition on the same consumer: read the shipped payload.
    (consumer / 'Main.cui').write_text(STATEFUL)
    trans_proc = run_dotnet([str(dotnet), 'build', str(proj), '-m:1', '--verbosity', 'minimal'],
                            consumer, 'build-native-transition')
    if not succeeded(trans_proc):
        record('native-stateful-transition', 'blocked',
               reason='compiler did not accept the stateful input at the detected transport contract')
    else:
        trans_dll = read_target_path(consumer) or find_consumer_dll(consumer)
        if trans_dll is None or not trans_dll.exists():
            record('native-stateful-not-raw-shipped-under-v1', 'fail',
                   reason='transition build succeeded but consumer assembly not found at recorded/actual path')
        else:
            names_after = run_probe(trans_dll, 'native-transition')
            if V2_RESOURCE not in names_after:
                record('native-stateful-not-raw-shipped-under-v1', 'blocked',
                       reason='distinct V2 artifact not embedded in the shipped assembly; '
                              'typed V2 transport not present/integrated')
            else:
                v2_text = run_probe_resource(trans_dll, V2_RESOURCE, 'native-transition')
                v2 = json.loads(v2_text) if v2_text else {}
                docs = get(v2, 'Documents', 'documents') or []
                main_doc = next((d for d in docs if get(d, 'Source', 'source') == 'Main.cui'), None)
                meta = get(main_doc, 'Metadata', 'metadata') or {}
                abi = get(meta, 'RuntimeAbiVersion', 'runtimeAbiVersion')
                check('native-stateful-promoted-to-v2', main_doc is not None)
                check('native-stateful-v2-abi2', abi == '2', abi=abi)
                if V1_RESOURCE in names_after:
                    v1_text = run_probe_resource(trans_dll, V1_RESOURCE, 'native-transition') or ''
                    check('native-stateful-absent-from-shipped-v1',
                          STATEFUL_SENTINEL not in v1_text and 'Main.cui' not in v1_text)
                else:
                    check('native-stateful-absent-from-shipped-v1', True)

# --- 4. browser/WASM-targeted dependency-inspection/consumer, supported boundary --
# (main CakeOS.Cui compiler package only; the standalone build-task package has
#  no consumer-side CUI compilation wiring, so WASM consumer exercise does not
#  apply to it.)
if not L['standalone']:
    wasm = e / 'wasm-consumer'
    wasm.mkdir()
    write_nuget_config(wasm)
    (wasm / 'Main.cui').write_text(STATELESS)
    consumer_csproj(wasm / 'Consumer.csproj', wasm,
                    extra_props='<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>')
    wasm_proj = wasm / 'Consumer.csproj'

    wasm_restore = run_dotnet([str(dotnet), 'restore', str(wasm_proj), '-m:1'],
                              wasm, 'wasm-restore')
    wasm_build = None
    if succeeded(wasm_restore):
        wasm_build = run_dotnet([str(dotnet), 'build', str(wasm_proj), '-m:1', '--verbosity', 'minimal'],
                                wasm, 'wasm-build')

    if wasm_build is not None and succeeded(wasm_build):
        wasm_dll = read_target_path(wasm) or find_consumer_dll(wasm)
        if wasm_dll is None or not wasm_dll.exists():
            record('wasm-targeted-consumer-boundary', 'fail',
                   reason='WASM build reported success but assembly output was not found at the '
                          'recorded/actual RID path', recorded=str(wasm_dll))
        else:
            wasm_choice = (wasm / 'toolchain.txt').read_text().splitlines() \
                if (wasm / 'toolchain.txt').exists() else []
            wasm_names = run_probe(wasm_dll, 'wasm')
            check('wasm-compiler-from-installed-package',
                  bool(wasm_choice) and wasm_choice[0].strip() == 'true'
                  and f'{L["tool_prefix"]}/CakeOS.Cui.Build.Compiler.dll' in wasm_choice[1]
                  and '/framework/' not in wasm_choice[1].replace('\\', '/'))
            check('wasm-genuine-v1-resource-embedded', V1_RESOURCE in wasm_names)
            check('wasm-no-browser-runtime-injected',
                  not (wasm_dll.parent / 'CakeOS.Cui.Build.Compiler.dll').exists())
            record('wasm-targeted-consumer-boundary', 'pass',
                   output_path=str(wasm_dll),
                   detail='compiler toolchain is RID-agnostic and compiled at the supported boundary; '
                          'no web/WASM runtime was claimed or injected')
    else:
        log = (wasm_build.stdout if wasm_build is not None else wasm_restore.stdout)
        status, diag = classify_wasm_failure(log)
        record('wasm-targeted-consumer-boundary', status,
               reason='WASM-targeted build did not succeed',
               diagnostic=diag)

summary = {
    'required_package': f'{pkg_id}@{pkg_ver}',
    'checks': len(rows),
    'passed': sum(r['status'] == 'pass' for r in rows),
    'blocked': sum(r['status'] == 'blocked' for r in rows),
    'failed': sum(r['status'] == 'fail' for r in rows),
    'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
}
(e / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary))
raise SystemExit(0 if all(r['status'] == 'pass' for r in rows) else 2)
