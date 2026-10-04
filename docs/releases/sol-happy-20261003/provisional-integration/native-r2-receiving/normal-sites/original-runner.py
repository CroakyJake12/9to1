#!/usr/bin/env python3
"""Build and execute the unchanged normal owning Sites project on exact proposal source."""
import importlib.util
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path('/workspace/team-c-c4-native-r2-receiving')
HEAD = 'ce7f7ce15cc75b5fac40bae2bce20f232a3d6f38'
OUTPUT = Path('/workspace/team-c-resume-evidence/c4-native-r2-receiving/normal-sites')
PROJECT = '9to1 Workspace/Sites/Tests/HavenOS.Sites.Tests.csproj'


def main():
    OUTPUT.mkdir(exist_ok=False)
    diagnostics = OUTPUT / 'diagnostics'
    diagnostics.mkdir()
    spec = importlib.util.spec_from_file_location('ordinary_native', ROOT / 'apps/Web/Tests/ci/run-ordinary-native.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    env = os.environ.copy()
    for name, directory in {
        'DOTNET_CLI_HOME': 'cli', 'NUGET_PACKAGES': 'nuget', 'NUGET_HTTP_CACHE_PATH': 'http',
        'NUGET_PLUGINS_CACHE_PATH': 'plugins', 'XDG_CACHE_HOME': 'font-cache',
        'TMPDIR': 'tmp', 'TMP': 'tmp', 'TEMP': 'tmp', 'HAVEN_DATA_DIR': 'fixture-data',
    }.items():
        path = OUTPUT / directory
        path.mkdir(exist_ok=True)
        env[name] = str(path)
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1')
    commands = module.Commands(diagnostics, env, ROOT)
    result = {'sourceCommit': HEAD, 'project': PROJECT, 'status': 'NOT_RUN', 'testsStarted': False,
              'scope': 'Actual normal whole receiving owning Sites project; local fake HTTP reader fixtures, no real provider/current-session/import/scanner/publication acceptance'}
    try:
        assert commands.run('git-head', ['git', 'rev-parse', 'HEAD'], 15).strip() == HEAD
        commands.run('git-clean-before', ['git', 'diff', '--exit-code', 'HEAD', '--'], 15)
        commands.run('source-tree-pins', ['git', 'ls-tree', '-r', 'HEAD'], 15)
        assert commands.run('dotnet-version', ['dotnet', '--version'], 30).strip() == '10.0.401'
        commands.run('dotnet-info', ['dotnet', '--info'], 30)
        artifacts = OUTPUT / 'artifacts'
        props = ['-p:SelfContained=false', '-p:UseSharedCompilation=false',
                 '-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false',
                 '-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false',
                 '-p:CreateHardLinksForCopyLocalIfPossible=false',
                 '-p:CreateHardLinksForPublishFilesIfPossible=false']
        commands.run('restore', ['dotnet', 'restore', PROJECT, '--artifacts-path', str(artifacts),
                     '-r', 'linux-x64', '--configfile', str(ROOT / 'NuGet.Config'),
                     '--disable-build-servers', '-p:Configuration=Release', '-m:1',
                     '-nodeReuse:false'] + props, 600)
        commands.run('build', ['dotnet', 'build', PROJECT, '--no-restore', '-c', 'Release',
                     '--artifacts-path', str(artifacts), '-r', 'linux-x64',
                     '--disable-build-servers', '-m:1', '-nodeReuse:false'] + props, 1200)
        evaluated = module.parse_sdk(commands.run('target-properties', ['dotnet', 'msbuild', PROJECT,
                     '-p:Configuration=Release', '-p:RuntimeIdentifier=linux-x64',
                     '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(artifacts),
                     '-nodeReuse:false', '-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,IsTestProject'] + props, 30))
        module.write_json(diagnostics / 'target-properties.json', evaluated)
        target = Path(evaluated['TargetPath'])
        assert target.is_file() and evaluated['TargetFramework'] == 'net10.0'
        module.write_json(diagnostics / 'artifact-identities.json', {
            'entryDLL': {'path': str(target), 'sha256': module.digest(target), 'bytes': target.stat().st_size},
            'actualProductionSites': {'path': str(target.parent / 'HavenOS.Sites.dll'),
                                      'sha256': module.digest(target.parent / 'HavenOS.Sites.dll')},
        })
        result['testsStarted'] = True
        commands.run('normal-owner-tests', ['dotnet', 'test', PROJECT, '--no-build', '--no-restore',
                     '-c', 'Release', '--artifacts-path', str(artifacts), '-r', 'linux-x64',
                     '--disable-build-servers', '-m:1', '-nodeReuse:false',
                     '--logger', 'trx;LogFileName=normal-sites.trx',
                     '--results-directory', str(diagnostics / 'test-results')] + props, 180)
        trx = diagnostics / 'test-results/normal-sites.trx'
        tree = ET.fromstring(trx.read_bytes())
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        counters = tree.find('.//t:Counters', ns).attrib
        assert int(counters['total']) > 21 and counters['executed'] == counters['total']
        assert counters['passed'] == counters['total'] and counters['failed'] == '0'
        definitions = tree.findall('.//t:UnitTest/t:TestMethod', ns)
        native_r2 = [x for x in definitions if 'R2NativeSiteArtifactReaderTests' in x.attrib['className']]
        assert len(native_r2) == 21
        result.update(status='PASS', counts=counters, nativeR2OriginalCases=21, trxSha256=module.digest(trx))
    except Exception as error:
        result.update(status='FAIL', error=repr(error))
    finally:
        try:
            commands.run('git-clean-after', ['git', 'diff', '--exit-code', 'HEAD', '--'], 15)
        except Exception as error:
            result.update(status='FAIL', sourceCustodyError=repr(error))
        module.write_json(diagnostics / 'result.json', result)
    print(json.dumps(result, indent=2))
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
