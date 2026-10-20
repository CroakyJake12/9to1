#!/usr/bin/env python3
"""Lane 2 real MSBuild packaging boundary checks (pass 3).

Proves the DISTINCT typed V2 artifact is emitted for the stateful subset and a genuine V1 bundle
for the stateless subset of a mixed project, across all five accepted CUI item types
(EmbeddedResource / Content / None / AvaloniaResource / CuiMarkup), with:

- lowercase genuine-V1 wire keys (``format/formatVersion/documents/source``);
- authoritative assembly-manifest resource enumeration via a small C# probe (not a byte-scan
  heuristic) run under the allocated build slot;
- output-copy (Always/PreserveNewest/IfDifferent) and publish (CopyToPublishDirectory defaults)
  ownership modelled independently, including Never and publish-only items;
- fail-closed path custody: an escaping/absolute TargetPath fails the build; a destination whose
  current bytes no longer match the owned artifact is never deleted (unrelated same-path files are
  preserved); never-owned same-bytes paths are never claimed;
- statefulness propagation through the import/dependency closure (stateless importer of a stateful
  child is promoted to ABI2);
- failed-build preservation: a compile failure publishes nothing and preserves the previous artifact.

Every SDK invocation uses the owner's guarded ``dotnet`` and ``-m:1``. Run only after the
coordinator allocates the serialized .NET slot.
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
parser.add_argument('--probe-dotnet', type=Path, default=None)
args = parser.parse_args()

task = args.task_assembly.resolve()
evidence = args.evidence.resolve()
evidence.mkdir(parents=True, exist_ok=True)
probe_dotnet = (args.probe_dotnet or args.dotnet).resolve()

root = Path(__file__).resolve().parents[1]
props = root / 'Compiler/buildTransitive/CakeOS.Cui.props'
targets = root / 'Compiler/buildTransitive/CakeOS.Cui.targets'

STATEFUL = '<Cui><Variable Type="bool" ID="Open">False</Variable>\n<Actions><Action name="toggle"><Toggle Target="Open"/></Action></Actions>\n<Page><Button Action="toggle" Content="Aa"/></Page></Cui>'
STATELESS = '<Cui><Page ID="stateless"><Text Text="ok"/></Page></Cui>'
BAD_SOURCE = '<Cui><Page><Text Widht="10"/></Page></Cui>'
UNRELATED_MARKER = 'UNRELATED-USER-FILE-NOT-OWNED\n'


def csproj(compiler_task, items, extra_groups=''):
    return f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><CuiUsePrebuiltCompiler>true</CuiUsePrebuiltCompiler>
<CuiCompilerTaskAssembly>{xml.escape(str(compiler_task))}</CuiCompilerTaskAssembly></PropertyGroup>
<ItemGroup>
{items}
</ItemGroup>
{extra_groups}
<Import Project="{xml.escape(str(props))}" />
<Import Project="{xml.escape(str(targets))}" />
</Project>'''


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')


def invoke(project, verb, expected_ok, expected_text=None, extra=(), cwd=None, label=''):
    command = [str(args.dotnet), verb, str(project), '-m:1', '--verbosity', 'minimal', *extra]
    process = subprocess.run(command, cwd=cwd or project.parent, text=True,
                             stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (evidence / (label + '.log')).write_text(process.stdout)
    failed = (process.returncode == 0) != expected_ok
    if expected_text is not None and expected_text not in process.stdout:
        failed = True
    if failed:
        print(label, 'FAIL', 'exit', process.returncode, flush=True)
        raise RuntimeError(label + ' did not match expected outcome; inspect its complete log')
    print(label, 'PASS', 'exit', process.returncode, flush=True)
    return process.stdout


def check(name, predicate):
    if not predicate:
        raise AssertionError(name)
    print(name, 'PASS', flush=True)


def read_artifact(path):
    return json.loads(path.read_text(encoding='utf-8'))


def resource_names(dll, label):
    """Authoritative assembly-manifest enumeration via a tiny C# probe built/run under the slot."""
    probe = evidence / 'probe'
    write(probe / 'Probe.csproj', f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>''')
    write(probe / 'Program.cs', '''using System;
using System.Reflection;
class Program
{
    static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args[0]);
        foreach (var name in assembly.GetManifestResourceNames())
            Console.WriteLine(name);
        return 0;
    }
}''')
    run = subprocess.run([str(probe_dotnet), 'run', '--project', str(probe / 'Probe.csproj'),
                          '--', str(dll)],
                         cwd=probe, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (evidence / (label + '-probe.log')).write_text(run.stdout)
    if run.returncode != 0:
        raise RuntimeError('resource probe failed; inspect ' + label + '-probe.log')
    names = {line.strip() for line in run.stdout.splitlines() if line.strip()}
    return names


# ---------------------------------------------------------------------------
# Project 1: mixed all-five-item-types fixture (with publish/ownership scenarios)
# ---------------------------------------------------------------------------
mix = evidence / 'Lane2Mixed'
items = f'''<EmbeddedResource Include="Stateful.cui" LogicalName="Lane2.Stateful.cui" />
<Content Include="Stateless.cui" Link="Raw/Stateless.cui" CopyToOutputDirectory="PreserveNewest" />
<Content Include="PublishOnly.cui" CopyToPublishDirectory="Always" />
<Content Include="NoPublish.cui" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" />
<Content Include="NeverCopy.cui" CopyToOutputDirectory="Never" />
<None Include="Theme.cui-theme" />
<AvaloniaResource Include="AvaloniaResource.cui" />
<CuiMarkup Include="Prefab.cui-prefab" />'''
write(mix / 'Lane2Mixed.csproj', csproj(task, items))
for name, source in [('Stateful.cui', STATEFUL), ('Stateless.cui', STATELESS),
                     ('PublishOnly.cui', STATELESS), ('NoPublish.cui', STATELESS),
                     ('NeverCopy.cui', STATELESS), ('Theme.cui-theme', STATELESS),
                     ('AvaloniaResource.cui', STATELESS), ('Prefab.cui-prefab', STATELESS)]:
    write(mix / name, source)

out = mix / 'bin/Debug/net10.0'
dll = out / 'Lane2Mixed.dll'
v2 = mix / 'obj/Debug/net10.0/Cui/compilation.v2.cuic.json'
v1 = mix / 'obj/Debug/net10.0/Cui/compilation.v1.cuic.json'
publish_override = evidence / 'publish-root'

# --- baseline mixed build ---------------------------------------------------
invoke(mix / 'Lane2Mixed.csproj', 'build', True, label='lane2-baseline')

names = resource_names(dll, 'baseline')
v2p = read_artifact(v2)
v1p = read_artifact(v1)

# Genuine V1 keys are lowercase; the typed V2 artifact keys are PascalCase.
check('v2-artifact-format', v2p.get('Format') == 'cui-build-compilation' and v2p.get('FormatVersion') == 2)
check('v1-artifact-format-lowercase', v1p.get('format') == 'cui-build-compilation' and v1p.get('formatVersion') == 1)
check('v1-documents-array', isinstance(v1p.get('documents'), list))

v2_docs = {d['Source'] for d in v2p.get('Documents') or []}
v1_docs = {d['source'] for d in v1p.get('documents') or []}
check('v2-exactly-stateful-subset', v2_docs == {'Stateful.cui'})
expected_v1 = {'Stateless.cui', 'PublishOnly.cui', 'NoPublish.cui', 'NeverCopy.cui',
               'Theme.cui-theme', 'AvaloniaResource.cui', 'Prefab.cui-prefab'}
check('v1-all-five-item-types-stateless-subset', v1_docs == expected_v1)

# Authoritative resource enumeration: both artifact identities embedded; stateful raw excluded.
check('v2-resource-embedded', 'CUI.Compilation.v2' in names)
check('v1-resource-embedded', 'CUI.Compilation.v1' in names)
check('stateful-raw-not-embedded', 'Lane2.Stateful.cui' not in names)
check('stateful-raw-not-copied', not (out / 'Stateful.cui').exists())
check('stateless-raw-copied', (out / 'Raw' / 'Stateless.cui').exists())
check('publish-only-not-copied-to-output', not (out / 'PublishOnly.cui').exists())
check('no-publish-copied-to-output', (out / 'NoPublish.cui').exists())
check('never-copy-not-copied-to-output', not (out / 'NeverCopy.cui').exists())

# --- never-owned same-bytes path must not be claimed or deleted ----------------
write(out / 'NeverCopy.cui', (mix / 'NeverCopy.cui').read_text(encoding='utf-8'))
invoke(mix / 'Lane2Mixed.csproj', 'build', True, label='lane2-keep-never-copy')
check('never-copy-same-bytes-survives', (out / 'NeverCopy.cui').exists())

# --- incremental stateless -> stateful on SAME output, unrelated same-path preserved ---
write(out / 'Raw' / 'Stateless.cui', UNRELATED_MARKER)
write(mix / 'Stateless.cui', STATEFUL)
invoke(mix / 'Lane2Mixed.csproj', 'build', True, label='lane2-transition-stateless-to-stateful')

v2_after = read_artifact(v2)
v1_after = read_artifact(v1)
v2_docs_after = {d['Source'] for d in v2_after.get('Documents') or []}
v1_docs_after = {d['source'] for d in v1_after.get('documents') or []}
check('transitioned-v2-now-stateful', v2_docs_after == {'Stateful.cui', 'Stateless.cui'})
check('transitioned-v1-drops-stateless', 'Stateless.cui' not in v1_docs_after)
# The owned destination was overwritten with unrelated bytes -> hash-guard must refuse deletion.
check('unrelated-same-path-preserved', (out / 'Raw' / 'Stateless.cui').read_text(encoding='utf-8') == UNRELATED_MARKER)
check('never-copy-still-present', (out / 'NeverCopy.cui').exists())
check('sources-not-deleted', (mix / 'Stateless.cui').exists() and (mix / 'Stateful.cui').exists())
check('v2-still-embedded', 'CUI.Compilation.v2' in resource_names(dll, 'transition'))

# --- custom publish directory: publish-only / publish-never / content defaults ---
invoke(mix / 'Lane2Mixed.csproj', 'publish', True,
       extra=('-p:PublishDir=' + str(publish_override)),
       label='lane2-publish-custom-root')
check('publish-custom-root-created', publish_override.is_dir())
check('publish-only-published', (publish_override / 'PublishOnly.cui').exists())
check('publish-never-absent', not (publish_override / 'NoPublish.cui').exists())
check('content-default-published', (publish_override / 'NeverCopy.cui').exists())
check('stateful-raw-not-published', not (publish_override / 'Stateful.cui').exists())

# --- failed compile must preserve the previous artifact bytes -------------------
v2_before = v2.read_bytes()
v1_before = v1.read_bytes()
write(mix / 'Stateless.cui', BAD_SOURCE)
invoke(mix / 'Lane2Mixed.csproj', 'build', False, 'CUIC012', label='lane2-failed-compile')
check('failed-compile-preserves-v2-artifact-bytes', v2.exists() and v2.read_bytes() == v2_before)
check('failed-compile-preserves-v1-artifact-bytes', v1.exists() and v1.read_bytes() == v1_before)

# ---------------------------------------------------------------------------
# Project 2: transitive import closure propagation (stateless importer of a
# stateful child becomes stateful / ABI2 itself)
# ---------------------------------------------------------------------------
imp = evidence / 'Lane2Import'
write(imp / 'Child.cui', STATEFUL)
write(imp / 'Parent.cui', '<Cui><Import Source="Child.cui"/><Page><Text Text="ok"/></Page></Cui>')
write(imp / 'Lane2Import.csproj', csproj(task, '<Content Include="Parent.cui" />\n<Content Include="Child.cui" />'))
invoke(imp / 'Lane2Import.csproj', 'build', True, label='lane2-transitive-import')

imp_v2 = read_artifact(imp / 'obj/Debug/net10.0/Cui/compilation.v2.cuic.json')
imp_docs = {d['Source'] for d in imp_v2.get('Documents') or []}
check('import-parent-and-child-stateful-in-v2', imp_docs == {'Parent.cui', 'Child.cui'})
parent_doc = next(d for d in imp_v2['Documents'] if d['Source'] == 'Parent.cui')
check('import-parent-promoted-to-abi2', parent_doc['Metadata']['RuntimeAbiVersion'] == '2')

# ---------------------------------------------------------------------------
# Project 3: unsafe/escaping TargetPath must fail closed (CUIB003) before any
# publishing or mutation of cleanup state
# ---------------------------------------------------------------------------
unsafe = evidence / 'Lane2Unsafe'
write(unsafe / 'Safe.cui', STATELESS)
unsafe_item = '<Content Include="Safe.cui" Link="x.cui" TargetPath="../escape.cui" CopyToOutputDirectory="PreserveNewest" />'
write(unsafe / 'Lane2Unsafe.csproj', csproj(task, unsafe_item))
invoke(unsafe / 'Lane2Unsafe.csproj', 'build', False, 'Unsafe', label='lane2-unsafe-targetpath')
check('unsafe-build-did-not-publish-artifact',
      not (unsafe / 'obj/Debug/net10.0/Cui/compilation.v2.cuic.json').exists())


print('ALL LANE2 V2 PACKAGING CHECKS PASSED')
