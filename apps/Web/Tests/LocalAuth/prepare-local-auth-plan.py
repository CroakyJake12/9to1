#!/usr/bin/env python3
"""Prepare explicit public pins/TLS and an invocation; never launch or read issuer secrets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import zipfile

HERE = Path(__file__).resolve().parent
REPLAY = HERE.parent / 'ci/sealed-browser-replay'


def credential_safe_environment(environment):
    return {**environment, 'DEBUG': '', 'PWDEBUG': '', 'NODE_DEBUG': '', 'NODE_OPTIONS': ''}


def sha(file):
    digest = hashlib.sha256()
    with Path(file).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def command(argv, cwd=None):
    result = subprocess.run(argv, cwd=cwd, env=credential_safe_environment(os.environ), capture_output=True, timeout=60)
    if result.returncode:
        raise RuntimeError('Bound local preparation command refused')
    return result.stdout


def regular(file):
    if file.is_symlink() or not file.is_file():
        raise RuntimeError('Exact regular public input required')
    return {'path': str(file), 'bytes': file.stat().st_size, 'sha256': sha(file)}


def reviewed_checkout(checkout, expected_commit, rows):
    if not isinstance(expected_commit, str) or not re.fullmatch('[0-9a-f]{40}', expected_commit):
        raise RuntimeError('Explicit separate source identity required')
    actual = command(['git', 'rev-parse', 'HEAD'], checkout).decode().strip()
    if actual != expected_commit:
        raise RuntimeError('Explicit source checkout HEAD differs')
    selected = set()
    pins = []
    for row in rows:
        name = row['path']
        if not isinstance(name, str) or not name or name.startswith('/') or '\\' in name or any(part in ['', '.', '..'] for part in name.split('/')) or name in selected:
            raise RuntimeError('Unique exact reviewed source path required')
        selected.add(name)
        file = checkout / name
        pin = regular(file)
        original = command(['git', 'show', expected_commit + ':' + name], checkout)
        if file.read_bytes() != original or pin['bytes'] != row['bytes'] or pin['sha256'] != row['sha256']:
            raise RuntimeError('Reviewed source body differs from actual Git identity')
        pins.append(pin)
    return pins


def chromium_socket_temp_root(execution):
    # Matching Chromium151 Linux singleton source uses the45-byte suffix below.
    # SetupSockAddr refuses >=108 bytes, reserving the final NUL byte.
    if execution.resolve(strict=False) != execution:
        raise RuntimeError('Canonical owned execution root required')
    temp = execution / 'tmp'
    suffix = '/org.chromium.Chromium.XXXXXX/SingletonSocket'
    size = len(os.fsencode(temp)) + len(os.fsencode(suffix))
    if size >= 108:
        raise RuntimeError('Owned Chromium temporary socket path exceeds Linux bound')
    return {'tempRootBytes': len(os.fsencode(temp)), 'socketPathBytes': size, 'socketPathLimitBytes': 108}


def receive_public_archive(archive_path, manifest, destination):
    # Exact received producer bodies only; no guessed files, patches or extraction shortcuts.
    rows = manifest['publishFiles']
    names = [row['path'] for row in rows]
    if len(set(names)) != len(names) or len(names) != manifest['fileCount']:
        raise RuntimeError('Exact unique original public inventory required')
    if any(not isinstance(row['bytes'], int) or row['bytes'] < 0 or not re.fullmatch('[0-9a-f]{64}', row['sha256']) for row in rows):
        raise RuntimeError('Original public body size/hash required')
    if sum(row['bytes'] for row in rows) > 128 * 1024 * 1024 or archive_path.stat().st_size > 128 * 1024 * 1024:
        raise RuntimeError('Public archive exceeds maintained128MiB bound')
    for name in names:
        if not isinstance(name, str) or not name or name.startswith('/') or '\\' in name or '\0' in name or any(part in ['', '.', '..'] for part in name.split('/')):
            raise RuntimeError('Original public archive path refused')
    with zipfile.ZipFile(archive_path) as archive:
        if archive.namelist() != names:
            raise RuntimeError('Original complete ordered ZIP/CRC receipt differs')
        for row in rows:
            info = archive.getinfo(row['path'])
            if info.is_dir() or info.file_size != row['bytes'] or (info.external_attr >> 16) & 0o170000 not in (0, 0o100000):
                raise RuntimeError('Original regular public archive member required')
        for row in rows:
            data = archive.read(row['path'])  # Full read validates this original member's CRC.
            if len(data) != row['bytes'] or hashlib.sha256(data).hexdigest() != row['sha256']:
                raise RuntimeError('Original public archive body differs')
    destination.mkdir(mode=0o700)
    with zipfile.ZipFile(archive_path) as archive:
        for row in rows:
            target = destination / row['path']
            target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            with target.open('xb') as output:
                output.write(archive.read(row['path']))


def prepare(args):
    if any(not re.fullmatch('[0-9a-f]{40}', value) for value in [args.candidate, args.test_source_commit, args.fixture_source_commit]) or any(not re.fullmatch('[0-9a-f]{64}', value) for value in [args.manifest_sha, args.seal_sha, args.archive_sha]):
        raise RuntimeError('Explicit immutable candidate and manifest hash required')
    execution = Path(args.execution_root).absolute()
    socket_temp_root = chromium_socket_temp_root(execution)
    if execution.exists():
        raise RuntimeError('Fresh private execution root required')
    node = Path(args.node).resolve(strict=True)
    browser = Path(args.chromium).resolve(strict=True)
    playwright = Path(args.playwright_module).resolve(strict=True)
    if json.loads((playwright / 'package.json').read_text()).get('version') != '1.62.0':
        raise RuntimeError('Existing pinned Playwright1.62.0 required; do not install')
    checkout = Path(args.fixture_checkout).resolve(strict=True)
    fixture = checkout / 'cloud/cake-id-auth'
    fixture_head = args.fixture_source_commit
    test_checkout = Path(command(['git', 'rev-parse', '--show-toplevel'], HERE).decode().strip()).resolve(strict=True)
    contract = json.loads((HERE / 'source-contract.json').read_text())
    test_files = ['README.md', 'preflight.test.cjs', 'preparation_test.py', 'prepare-local-auth-plan.py', 'run-local-auth-current.cjs', 'source-contract.json']
    test_rows = [{**regular(HERE / name), 'path': 'apps/Web/Tests/LocalAuth/' + name} for name in test_files] + contract['requiredSource']
    test_pins = reviewed_checkout(test_checkout, args.test_source_commit, test_rows)
    fixture_rows = [{**row, 'path': 'cloud/cake-id-auth/' + row['path']} for row in contract['requiredFixtureSource']]
    reviewed_checkout(checkout, fixture_head, fixture_rows)
    command(['git', 'diff', '--exit-code', 'HEAD', '--', 'cloud/cake-id-auth'], checkout)
    expected_packages = {'wrangler': '4.146.0', 'better-auth': '1.7.7', '@better-auth/oauth-provider': '1.7.7', 'jose': '6.2.12'}
    dependency_pins = []
    dependency_root = (fixture / 'node_modules').resolve(strict=True)
    dependency_links = []
    for package, version in expected_packages.items():
        manifest = (fixture / 'node_modules' / package / 'package.json').resolve(strict=True)
        if json.loads(manifest.read_text()).get('version') != version:
            raise RuntimeError('Existing maintained local issuer dependencies differ; do not install')
        dependency_pins.append(regular(manifest))
    for file in sorted(dependency_root.rglob('*')):
        if file.is_symlink():
            target = file.resolve(strict=True)
            if not target.is_relative_to(dependency_root) or not target.is_file():
                raise RuntimeError('Maintained dependency symlink requires explicit review')
            dependency_links.append({'path': str(file), 'target': str(target)})
        elif file.is_file():
            dependency_pins.append(regular(file))
    files = command(['git', 'ls-files', '-z', '--', 'cloud/cake-id-auth'], checkout).decode().split('\0')
    source_pins = [regular(checkout / name) for name in files if name]
    manifest = Path(args.manifest).resolve(strict=True)
    if sha(manifest) != args.manifest_sha:
        raise RuntimeError('Actual publication receipt hash differs')
    manifest_body = json.loads(manifest.read_text())
    seal = Path(args.seal).resolve(strict=True)
    archive = Path(args.archive).resolve(strict=True)
    if sha(seal) != args.seal_sha or sha(archive) != args.archive_sha:
        raise RuntimeError('Actual original seal/archive hashes differ')
    producer = json.loads(seal.read_text())
    if manifest_body.get('sourceCommit') != args.candidate or producer.get('sourceCommit') != args.candidate or producer.get('receiptSha256') != args.manifest_sha or producer.get('zipSha256') != args.archive_sha or producer.get('zipBytes') != archive.stat().st_size or producer.get('fileCount') != manifest_body.get('fileCount'):
        raise RuntimeError('Actual original producer tuple differs')
    before = Path(args.source_catalog).resolve(strict=True)
    after = Path(args.source_catalog_after).resolve(strict=True)
    # READY path is carried opaquely; preparation never opens the private file or scans its run directory.
    private_manifest = Path(args.fixture_manifest).absolute()
    run_root = private_manifest.parent
    if private_manifest.name != 'browser-fixture-private.json' or run_root.parent != fixture / '.local-run' or not re.fullmatch('[0-9a-f]{16}', run_root.name):
        raise RuntimeError('Exact fresh own READY path required')
    execution.mkdir(mode=0o700)
    assets = execution / 'assets'
    receive_public_archive(archive, manifest_body, assets)
    local_manifest = execution / 'local-publish-manifest.json'
    local_manifest.write_text(json.dumps({**manifest_body, 'publishRoot': str(assets)}, separators=(',', ':')))
    binding = {'sourceCommit': args.candidate, 'manifestPath': str(local_manifest), 'manifestSha256': sha(local_manifest),
               'originalManifestPath': str(manifest), 'originalManifestSha256': args.manifest_sha,
               'originalSealPath': str(seal), 'originalSealSha256': args.seal_sha,
               'originalArchivePath': str(archive), 'originalArchiveSha256': args.archive_sha,
               'fileCount': manifest_body.get('fileCount'), 'sourceCatalogPath': str(before), 'sourceCatalogSha256': sha(before),
               'sourceCatalogAfterPath': str(after), 'sourceCatalogAfterSha256': sha(after),
               'runnerSha256': sha(HERE / 'run-local-auth-current.cjs'), 'hostSha256': sha(REPLAY / 'sealed-https-host-explicit-binding.cjs'),
               'contractSha256': sha(HERE / 'source-contract.json'), 'fixtureSourceCommit': fixture_head,
               'testSourceCommit': args.test_source_commit, 'testCheckout': str(test_checkout), 'testSourcePins': test_rows,
               'fixtureCheckout': str(checkout), 'fixtureSourcePins': source_pins,
               'fixtureDependencyRoot': str(dependency_root), 'fixtureDependencyLinks': dependency_links}
    binding_path = execution / 'public-binding.json'
    binding_path.write_text(json.dumps(binding, indent=2) + '\n')
    command([str(node), '-e', 'require(process.argv[1]).verifyPublicBinding(process.argv[2],process.argv[3]);',
             str(HERE / 'run-local-auth-current.cjs'), str(binding_path), args.candidate])
    tls = execution / 'tls'
    tls.mkdir(mode=0o700)
    cert, key = tls / 'client-cert.pem', tls / 'client-key.pem'
    old_umask = os.umask(0o077)
    try:
        command(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=client.example.test',
                 '-addext', 'subjectAltName=DNS:client.example.test', '-keyout', str(key), '-out', str(cert)])
    finally:
        os.umask(old_umask)
    key.chmod(0o600)
    san = command(['openssl', 'x509', '-in', str(cert), '-noout', '-ext', 'subjectAltName']).decode()
    if san.splitlines()[-1].strip() != 'DNS:client.example.test':
        raise RuntimeError('Exact controlled TLS SAN differs')
    command(['openssl', 'x509', '-in', str(cert), '-noout', '-checkend', '0'])
    public = tls / 'client-public.pem'
    public.write_bytes(command(['openssl', 'x509', '-in', str(cert), '-pubkey', '-noout']))
    der = tls / 'client-public.der'
    der.write_bytes(command(['openssl', 'pkey', '-pubin', '-in', str(public), '-outform', 'DER']))
    import base64
    spki = base64.b64encode(hashlib.sha256(der.read_bytes()).digest()).decode()
    pins = [regular(file) for file in [node, browser, manifest, local_manifest, seal, archive, before, after, binding_path, HERE / 'run-local-auth-current.cjs',
                                     HERE / 'source-contract.json', REPLAY / 'sealed-https-host-explicit-binding.cjs',
                                     REPLAY / 'run-owned-account-replay01.py', REPLAY / 'owned-kernel-child-proof01.py', cert, public, der]]
    # Complete available public browser/PW bodies; do not copy packages or private profiles.
    for folder in [playwright, browser.parent]:
        for file in sorted(folder.rglob('*')):
            if file.is_symlink():
                raise RuntimeError('Public tool symlink inventory requires explicit review')
            if file.is_file():
                pins.append(regular(file))
    pins += source_pins + dependency_pins + test_pins
    pins = list({row['path']: row for row in pins}.values())
    plan = {'output': str(execution / 'results'), 'control': str(execution / 'control'), 'tmpdir': str(execution / 'tmp'),
            'cache': str(execution / 'cache'), 'resourceRoot': str(execution), 'browserExecutable': str(browser),
            'maximumSeconds': 240, 'maximumOutputAndTemporaryBytes': 192000000, 'pins': pins,
            'argv': [str(node), str(HERE / 'run-local-auth-current.cjs'), str(binding_path), str(execution / 'results'),
                     args.candidate, str(private_manifest), str(run_root)]}
    plan_path = execution / 'plan.json'
    plan_path.write_text(json.dumps(plan, indent=2) + '\n')
    stat = key.stat()
    environment = credential_safe_environment({'B5_ACCOUNT_INTEROP_GUI_GRANTED': 'granted', 'PLAYWRIGHT_MODULE': str(playwright),
                   'CHROMIUM_EXECUTABLE': str(browser), 'B5_CLIENT_TLS_ROOT': str(tls), 'B5_CLIENT_TLS_CERT': str(cert),
                   'B5_CLIENT_TLS_KEY': str(key), 'B5_CLIENT_TLS_SPKI': spki})
    invocation = {'scope': 'PREPARED only: fresh root-owned issuer original TTY remains separate; existing browser custodian reused unchanged. Its legacy unconfigured caption is retained and conveys browser-family custody only.',
                  'candidateSourceCommit': args.candidate, 'testSourceCommit': args.test_source_commit, 'fixtureSourceCommit': fixture_head, 'bindingSha256': sha(binding_path),
                  'chromiumSocketTempRoot': socket_temp_root,
                  'planSha256': sha(plan_path), 'planPath': str(plan_path), 'environment': environment,
                  'argv': ['python3', '-B', str(REPLAY / 'run-owned-account-replay01.py'), str(plan_path), sha(plan_path)],
                  'keyMetadata': {'path': str(key), 'mode': stat.st_mode & 0o777, 'bytes': stat.st_size, 'dev': stat.st_dev, 'ino': stat.st_ino, 'mtimeNs': stat.st_mtime_ns},
                  'actualBrowserTests': 'NOT_RUN', 'issuerSecretsReadByPreparation': False, 'privateMaterialPublication': False}
    (execution / 'invocation.json').write_text(json.dumps(invocation, indent=2) + '\n')
    invocation['shellCommand'] = shlex.join(['env'] + [name + '=' + value for name, value in environment.items()] + invocation['argv'])
    print(json.dumps(invocation, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['manifest', 'manifest-sha', 'seal', 'seal-sha', 'archive', 'archive-sha', 'candidate', 'test-source-commit', 'fixture-source-commit', 'source-catalog', 'source-catalog-after', 'fixture-checkout', 'fixture-manifest', 'execution-root', 'node', 'playwright-module', 'chromium']:
        parser.add_argument('--' + name, required=True)
    try:
        prepare(parser.parse_args())
    except Exception as error:
        print(json.dumps({'status': 'REFUSED', 'type': type(error).__name__, 'scope': 'Preparation only; no browser, issuer launch or private fixture read.'}))
        sys.exit(1)
