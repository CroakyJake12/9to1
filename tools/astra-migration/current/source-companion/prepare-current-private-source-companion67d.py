#!/usr/bin/env python3
"""Root-only source-chain receiving. No SDK, test, model, credential or upload calls.

Reuse the existing full C29 source and C45/C48/C49 private transports. Verify
incremental pack coverage rather than letting canonical alternates fill missing
new source. Emit only a small current build-source companion and exact receipt.
Distribution license conformance and runtime acceptance remain separate.
"""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile

ROOT = Path('/workspace/astra-consolidated')
BASE = Path('/workspace/astra-source/friday-write-present-sol61u62')
HEAD29 = '563104e8f1feeba0663a10a721af4dc585033f2c'
HEAD45 = '1f2f1778fda757a30b1e29c449c61978614f5875'
HEAD48 = '7d41e42a7fb89a7f0097fe2d37923989dccbbd25'
HEAD49 = 'cff9281d0bc6640c3305f9a54daf1c1141877497'
FULL_BYTES = 120026060
FULL_SHA = 'fe63bde21df359598340ebaa1fd729914e55e801c7abc1ebbbe59761e53ebf4e'
WORK_LIMIT = 16777216
BUILD_LIMIT = 4194304
SPARE = 134217728


def descriptor(path, size, digest):
    return {'path': path, 'bytes': size, 'sha256': digest}


PARTS = [descriptor('/tmp/astra-root-c29-full-source-export01/astra-C29-source.bundle.part001', 83886080, 'bfd8bdb6e85a33b377bc516d841a81ac71d8b3fde031f217c0524c53f1cede8c'),
         descriptor('/tmp/astra-root-c29-full-source-export01/astra-C29-source.bundle.part002', 36139980, '65fbafd18e5bfb19cbb7c57f7741a6da022b8fb659bb7142944af2bb7e31e4f4')]
SOURCE_DESCRIPTION = descriptor('/workspace/astra-source/root-c31-source-restart-private03/SOURCE-AND-DEPENDENCIES.json', 2944, '2ddd5fbab293f08e0845554bf54a459f1a51364c6f70f5f56c0f0a2436fc770b')
FRESH_BASE_PROOF = descriptor('/workspace/astra-source/root-c31-source-restart-private03/FULL-FRESH-GIT-RECEIVING-RECEIPT.json.gz', 1009389, '1db78e49983825dc0c9fb85644f488234fb3d2c5b3b165bdca7f1c737100ef56')
SHALLOW = descriptor('/tmp/astra-root-c29-full-source-export01/SHALLOW-BOUNDARIES.txt', 41, '35062b7261558478fb8ced8229927ee846aa9acb1cbd7de69ac7857d42b0cd6f')
DEPENDENCIES = [descriptor('/workspace/astra-source/root-c31-source-restart-private03/XamlX.bundle', 192903, '01550ba0392bc1829ca0230eccd4df4ad7879a8e58373415efdbd30dec2a28e7'),
                descriptor('/workspace/astra-source/root-c31-source-restart-private03/Avalonia.DBus.bundle', 410318, 'fc5d8dd54e2e28a7dd34284600d004c92095193b857c9b030d36b2510b04b0a7'),
                descriptor('/workspace/astra-source/root-c31-source-restart-private03/XamlX.SHALLOW-BOUNDARIES.txt', 82, '57d0cbaf6a84c0eebdc459c7e1a7636099cffafc9a2deef2c1bd34cf772aa631'),
                descriptor('/workspace/astra-source/root-c31-source-restart-private03/Avalonia.DBus.SHALLOW-BOUNDARIES.txt', 82, '55244a947d41f4c8335f56b3ae66b78273e558e3a2e7051c4f30435147accebb')]
TRANSPORTS = [descriptor('/workspace/astra-source/root-c45-private-source-checkpoint58/ASTRA-C45-EXACT-SOURCE-CHECKPOINT-NOT-READY58.zip', 375535, '6450e5697ecef8a21e3d90ffb59063248ad401e68e7a69f8aca393d5fc698a08'),
              descriptor('/workspace/astra-private-friday-checkpoint48b/ASTRA-FRIDAY-C48-PRIVATE-SOURCE-RECOVERY.zip', 493929, 'c61aa3ca684d89fa28894c124eccfc43a09a599a158df0298a77ab1b17291817'),
              descriptor('/workspace/astra-private-friday-checkpoint49/ASTRA-FRIDAY-C49-PRIVATE-SOURCE-FAILURE-RECOVERY.zip', 4891346, '48b19eaeccd0e91bfffa9116a6fcd8f7b2f54bbe96cfaae483981ae91c7cfd9f')]
METADATA = [descriptor('/workspace/astra-source/root-c45-private-source-checkpoint58/C45-SOURCE-PRIVATE-UPLOAD-METADATA-READBACK58.json', 1293, '06fe521c0d5d28b1add720a15c08a2a4fbeb508ace82ba8f566f71f194d49325'),
            descriptor('/workspace/astra-source/root-private-friday-C48-remote-metadata64.json', 898, '44675461372a32580a1a7be6e82c10cb6540c4e8ea456083a7198730c25b028b'),
            descriptor('/workspace/astra-source/root-private-friday-C49-remote-metadata64.json', 907, '923d96bbbaa79faec41cfab689f3bf38ef8cb9dbaa677de5fa3e8e31321d4570')]


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


def pin(path):
    path = Path(path)
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), 'Regular absolute source input required')
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1048576), b''):
            value.update(block)
    return descriptor(str(path), path.stat().st_size, value.hexdigest())


def bound(row):
    require(set(row) == {'path', 'bytes', 'sha256'} and pin(row['path']) == row, 'Whole exact source transport differs')
    return Path(row['path'])


def record(row, compressed=False):
    raw = bound(row).read_bytes()
    return json.loads(gzip.decompress(raw) if compressed else raw)


def member_name(name):
    require(isinstance(name, str) and name and '\\' not in name and not PurePosixPath(name).is_absolute()
            and '\0' not in name and all(part not in ('', '.', '..') for part in name.split('/'))
            and not re.match('[A-Za-z]:', name), 'Safe relative source member required')
    return name


def whole_zip(row, manifest_name, row_key_sets):
    path = bound(row)
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist(); names = [member_name(x.filename) for x in entries]
        require(len(names) == len(set(names)) == len({n.casefold() for n in names}) and all(not x.is_dir() for x in entries), 'Source ZIP aliases or incomplete members rejected')
        manifest_raw = archive.read(manifest_name); manifest = json.loads(manifest_raw); declared = {}
        for array, key in row_key_sets:
            for item in manifest[array]:
                name = member_name(item[key]); require(name not in declared, 'Duplicate source manifest member')
                declared[name] = item
        require(set(names) == {manifest_name, *declared}, 'Whole source ZIP membership differs')
        proofs = []
        for name, item in declared.items():
            value = hashlib.sha256(); count = 0
            with archive.open(name) as stream:
                for block in iter(lambda: stream.read(1048576), b''):
                    count += len(block); value.update(block)
            require(count == item['bytes'] and value.hexdigest() == item['sha256'], 'Source ZIP whole member differs')
            proofs.append({'member': name, 'bytes': count, 'sha256': value.hexdigest()})
        return manifest, proofs


def allocation(root):
    return sum(p.stat().st_blocks * 512 for p in [root, *root.rglob('*')] if not p.is_symlink())


class Receiving:
    def __init__(self, output):
        self.output = output; self.git_stages = []

    def git(self, args, label, env=None, stdin=None):
        log = self.output / (label + '.log'); child = None; sink = None
        primary = None; faults = []; natural = None; log_pin = None
        try:
            sink = log.open('xb')
            child = subprocess.Popen(['git', *args], stdin=stdin, stdout=sink, stderr=subprocess.STDOUT, env=env)
        except BaseException as cause:
            primary = cause
        finally:
            if child is not None:
                while natural is None:
                    try:
                        natural = child.wait()
                    except BaseException as cause:
                        faults.append({'operation': 'naturalWait', 'cause': repr(cause)})
                        if primary is None:
                            primary = cause
            if sink is not None:
                try:
                    sink.flush(); os.fsync(sink.fileno())
                except BaseException as cause:
                    faults.append({'operation': 'flushAndFsync', 'cause': repr(cause)})
                    if primary is None:
                        primary = cause
                try:
                    sink.close()
                except BaseException as cause:
                    faults.append({'operation': 'close', 'cause': repr(cause)})
                    if primary is None:
                        primary = cause
            if log.is_file():
                try:
                    log_pin = pin(log)
                except BaseException as cause:
                    faults.append({'operation': 'wholeLogPin', 'cause': repr(cause)})
                    if primary is None:
                        primary = cause
        self.git_stages.append({'argv': ['git', *args], 'pid': child.pid if child else None, 'naturalExitCode': natural,
                                'wholeLog': log_pin, 'primaryFailure': repr(primary) if primary else None,
                                'independentWaitOrPublicationFaults': faults, 'forcedStop': False})
        if primary is not None:
            raise primary
        require(natural == 0, 'Source Git receiving failed; original process and whole log retained')
        require(log.stat().st_size <= BUILD_LIMIT and allocation(self.output) <= WORK_LIMIT, 'Bounded source companion evidence exceeded')
        return log.read_text()

    def objects(self, head, label, excluded_head=None):
        args = ['-C', str(ROOT), 'rev-list', '--objects', head]
        if excluded_head is not None:
            args.append('^' + excluded_head)
        return {line.split()[0] for line in self.git(args, label).splitlines()}

    def proved_base_objects(self, proof):
        gitdir = self.output / 'original-C29-shallow-object-scope.git'
        self.git(['init', '--bare', str(gitdir)], 'original-base-scope-init')
        boundary = bound(SHALLOW).read_bytes()
        require(boundary.decode().strip() == proof['requiresExactShallowBoundary'], 'Exact original shallow boundary required')
        with (gitdir / 'shallow').open('xb') as stream:
            stream.write(boundary)
        common = self.git(['-C', str(ROOT), 'rev-parse', '--path-format=absolute', '--git-common-dir'], 'original-base-canonical-objects').strip()
        env = os.environ.copy(); env['GIT_ALTERNATE_OBJECT_DIRECTORIES'] = str(Path(common) / 'objects')
        raw = self.git(['--git-dir=' + str(gitdir), 'rev-list', '--objects', HEAD29], 'original-base-shallow-reachable', env=env)
        known = {line.split()[0] for line in raw.splitlines()}
        require(HEAD29 in known and proof['requiresExactShallowBoundary'] in known
                and all(re.fullmatch('[a-f0-9]{40}', oid) for oid in known), 'Original bounded source graph differs')
        query = self.output / 'original-base-object-types.input'
        with query.open('xb') as stream:
            stream.write((''.join(oid + '\n' for oid in sorted(known))).encode())
        with query.open('rb') as stream:
            census = self.git(['--git-dir=' + str(gitdir), 'cat-file', '--batch-check=%(objectname) %(objecttype) %(objectsize)'],
                              'original-base-object-types', env=env, stdin=stream)
        types = {}; blobs = {}
        for line in census.splitlines():
            fields = line.split()
            require(len(fields) == 3 and fields[0] in known and fields[0] not in types
                    and fields[1] in ('commit', 'tree', 'blob') and fields[2].isdecimal(), 'Original source object census differs')
            types[fields[0]] = fields[1]
            if fields[1] == 'blob':
                blobs[fields[0]] = int(fields[2])
        original_blobs = {row['gitBlob']: row['bytes'] for row in proof['wholeUniqueBlobsDecodedAndHashed']}
        require(set(types) == known and len(original_blobs) == len(proof['wholeUniqueBlobsDecodedAndHashed']) == 15071
                and blobs == original_blobs, 'Canonical objects beyond the original shallow/base blob proof cannot be credited')
        return known, {'originalFullFreshFetchAndStrictFsck': FRESH_BASE_PROOF, 'exactShallowBoundary': SHALLOW,
                       'boundedReachableObjectCount': len(known), 'exactOriginallyWholeDecodedBlobCount': len(blobs),
                       'wholeOriginalBlobObjectIdsAndSizesEqual': True, 'unprovedOlderCanonicalHistoryNotCredited': True}

    def dependency_source_objects(self, dependency, transport, boundary):
        raw = bound(transport).read_bytes(); header, pack = raw.split(b'\n\n', 1)
        require(header.decode() == '# v2 git bundle\n' + dependency['head'] + ' HEAD'
                and pack.startswith(b'PACK'), 'Exact complete retained dependency bundle/head required')
        shallow = bound(boundary).read_bytes()
        require(shallow.decode().splitlines() == dependency['shallowBoundaries'], 'Exact retained dependency shallow boundaries required')
        label = 'retained-dependency-' + dependency['name']
        gitdir = self.output / (label + '.git')
        self.git(['init', '--bare', str(gitdir)], label + '-init')
        with (gitdir / 'shallow').open('xb') as stream:
            stream.write(shallow)
        pack_file = self.output / (label + '.pack-input')
        with pack_file.open('xb') as stream:
            stream.write(pack)
        env = os.environ.copy()
        for key in ('GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_OBJECT_DIRECTORY'):
            env.pop(key, None)
        with pack_file.open('rb') as stream:
            self.git(['--git-dir=' + str(gitdir), 'index-pack', '--strict', '--stdin'], label + '-index', env=env, stdin=stream)
        indexes = list((gitdir / 'objects/pack').glob('*.idx'))
        require(len(indexes) == 1, 'One complete retained dependency source pack required')
        details = self.git(['--git-dir=' + str(gitdir), 'verify-pack', '-v', str(indexes[0])], label + '-verify', env=env)
        objects = {line.split()[0] for line in details.splitlines() if line.split() and re.fullmatch('[a-f0-9]{40}', line.split()[0])}
        require(dependency['head'] in objects and objects, 'Retained dependency source objects/head missing')
        return objects, {'relativeRoot': dependency['path'], 'head': dependency['head'], 'wholeSourceBundle': transport,
                         'exactShallowBoundary': boundary, 'wholeContainedObjectCount': len(objects),
                         'allSourceObjectsActuallyContainedInTransport': True, 'canonicalAlternatesCredited': False}

    def incremental_pack(self, row, archive_member, old_head, new_head, known, index):
        with zipfile.ZipFile(bound(row)) as archive:
            raw = archive.read(archive_member)
        header, pack = raw.split(b'\n\n', 1)
        require(header.startswith(b'# v2 git bundle\n') and pack.startswith(b'PACK'), 'Exact v2 source bundle required')
        lines = header.decode().splitlines()[1:]
        require(len(lines) == 2 and lines[0].startswith('-' + old_head + ' ') and lines[1] == new_head + ' HEAD', 'Source bundle prerequisite or head differs')
        original_pack_end = len(pack) - 20
        require(original_pack_end >= 12, 'Complete original pack trailer required')
        pack_file = self.output / ('source-pack-' + str(index) + '.input')
        with pack_file.open('xb') as stream:
            stream.write(pack)
        gitdir = self.output / ('source-pack-' + str(index) + '.git')
        self.git(['init', '--bare', str(gitdir)], 'source-pack-' + str(index) + '-init')
        common = self.git(['-C', str(ROOT), 'rev-parse', '--path-format=absolute', '--git-common-dir'], 'source-pack-' + str(index) + '-canonical-objects').strip()
        env = os.environ.copy(); env['GIT_ALTERNATE_OBJECT_DIRECTORIES'] = str(Path(common) / 'objects')
        with pack_file.open('rb') as stream:
            self.git(['--git-dir=' + str(gitdir), 'index-pack', '--strict', '--stdin', '--fix-thin'], 'source-pack-' + str(index) + '-index', env=env, stdin=stream)
        indexes = list((gitdir / 'objects/pack').glob('*.idx')); require(len(indexes) == 1, 'Exactly one indexed incremental pack required')
        details = self.git(['--git-dir=' + str(gitdir), 'verify-pack', '-v', str(indexes[0])], 'source-pack-' + str(index) + '-verify', env=env)
        original_ids = set(); appended_ids = set()
        for line in details.splitlines():
            fields = line.split()
            if fields and re.fullmatch('[a-f0-9]{40}', fields[0]):
                require(len(fields) in (5, 7) and fields[4].isdecimal(), 'Whole indexed source object metadata differs')
                (original_ids if int(fields[4]) < original_pack_end else appended_ids).add(fields[0])
        require(original_ids and appended_ids <= known, 'Thin pack resolved an unproved newer source object from canonical alternates')
        require(old_head in known, 'Incremental prerequisite is outside the proved older transport graph')
        reachable = self.objects(new_head, 'source-pack-' + str(index) + '-reachable', old_head)
        require(reachable - known <= original_ids and original_ids <= reachable | known,
                'Incremental source pack omits required new objects or contains unreferenced extras')
        return known | original_ids, {'originalTransport': row, 'bundleMember': archive_member, 'requiredBase': old_head,
                                      'head': new_head, 'originalPackedObjects': len(original_ids), 'provedOlderThinDeltaBases': len(appended_ids),
                                      'allRequiredNewObjectsActuallyPresentInTransport': True, 'canonicalAlternatesNotCreditedForMissingNewObjects': True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--actual-sdk-terminal', type=Path, required=True)
    parser.add_argument('--actual-sdk-terminal-sha256', required=True)
    parser.add_argument('--build-source-bind', type=Path, required=True)
    parser.add_argument('--build-source-bind-sha256', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    for digest in (args.actual_sdk_terminal_sha256, args.build_source_bind_sha256):
        require(re.fullmatch('[a-f0-9]{64}', digest), 'Independent whole input SHA required')
    terminal_pin = pin(args.actual_sdk_terminal.absolute()); source_bind_pin = pin(args.build_source_bind.absolute())
    require(terminal_pin['sha256'] == args.actual_sdk_terminal_sha256 and source_bind_pin['sha256'] == args.build_source_bind_sha256, 'Actual source input binding differs')
    terminal = record(terminal_pin); source_bind = record(source_bind_pin)
    require(terminal['status'] == 'ACTUAL_CURRENT_WRITE_PRESENT_WINDOWS_SDK_PUBLICATIONS_COMPLETE_SEAL_AND_PLATFORM_RECEIVING_PENDING'
            and terminal['sourceCommit'] == HEAD49 and terminal['sourceInputsBefore'] == terminal['sourceInputsAfter'], 'Genuine current completed app source rows required')
    require(source_bind['status'] == 'ROOT_CURRENT_WRITE_PRESENT_SOURCE_BUILD_COMPANION_ONLY'
            and source_bind['sourceCommit'] == HEAD49 and source_bind['distributionLicenseReviewClaimed'] is False
            and source_bind['modelWeightsOrCredentialsIncluded'] is False, 'Exact Root build-source inventory without unsupported licensing/model claims required')
    output = args.output.absolute()
    require(output.is_relative_to(BASE) and output.resolve(strict=False) == output and not output.exists(), 'Fresh direct isolated source companion directory required')
    stat = os.statvfs(BASE); require(stat.f_bavail * stat.f_frsize >= SPARE + WORK_LIMIT, 'Bounded source companion disk/spare admission unavailable')
    output.mkdir(); receiving = Receiving(output)
    try:
        require(receiving.git(['-C', str(ROOT), 'rev-parse', 'HEAD'], 'current-source-head').strip() == HEAD49
                and not receiving.git(['-C', str(ROOT), 'diff', '--name-only'], 'current-worktree').strip()
                and not receiving.git(['-C', str(ROOT), 'diff', '--cached', '--name-only'], 'current-index').strip(), 'Current publication source changed')
        description = record(SOURCE_DESCRIPTION); base_proof = record(FRESH_BASE_PROOF, True)
        require(description['sourceHead'] == base_proof['head'] == HEAD29 and base_proof['bundleBytes'] == FULL_BYTES
                and base_proof['bundleSha256'] == FULL_SHA and base_proof['fullTreeExact'] and base_proof['processesNaturalExit']
                and len(base_proof['wholeUniqueBlobsDecodedAndHashed']) == 15071
                and all(stage['exitCode'] == 0 for stage in base_proof['actualFreshFetchAndStrictFsck']), 'Original complete C29 fresh fetch/fsck/blob proof differs')
        value = hashlib.sha256(); count = 0
        for row in PARTS:
            with bound(row).open('rb') as stream:
                for block in iter(lambda: stream.read(1048576), b''):
                    count += len(block); value.update(block)
        require(count == FULL_BYTES and value.hexdigest() == FULL_SHA, 'Whole multipart full source stream differs')
        for row in [SHALLOW, *DEPENDENCIES]:
            bound(row)
        require(bound(SHALLOW).read_text().strip() == base_proof['requiresExactShallowBoundary'], 'Original source shallow-boundary prerequisite differs')
        manifests = []; zip_proofs = []
        for row, manifest, arrays in [(TRANSPORTS[0], 'PACKAGE-MANIFEST.json', [('members', 'relative')]),
                                      (TRANSPORTS[1], 'MANIFEST.json', [('sourceFiles', 'member'), ('metadata', 'member')]),
                                      (TRANSPORTS[2], 'MANIFEST.json', [('rows', 'zipEntry')])]:
            data, proof = whole_zip(row, manifest, arrays); manifests.append(data); zip_proofs.append({'transport': row, 'wholeMembers': proof})
        require(manifests[0]['head'] == HEAD45 and manifests[0]['requiredBase'] == HEAD29
                and manifests[1]['head'] == HEAD48 and manifests[1]['base'] == HEAD45
                and manifests[2]['sourceHead'] == HEAD49 and manifests[2]['modelsPasswordsCookiesSecretsIncluded'] is False, 'Actual private source head chain differs')
        private_metadata = [record(row) for row in METADATA]
        remote = [private_metadata[0]['remote'], private_metadata[1]['metadata'], private_metadata[2]['metadata']]
        owners = []
        for index, item in enumerate(remote):
            permissions = item['permissions']; require(len(permissions) == 1 and permissions[0]['type'] == 'user' and permissions[0]['role'] == 'owner', 'Sole-owner private source permission proof required')
            owners.append(permissions[0]['emailAddress'])
            require(int(item['size']) == TRANSPORTS[index]['bytes'], 'Actual private uploaded source size differs')
            if index:
                require(private_metadata[index]['status'] == 'ACTUAL_PRIVATE_UPLOAD_OWNER_ONLY_METADATA_READBACK_PASS_REMOTE_CONTENT_SHA_NOT_CLAIMED'
                        and item['shared'] is False, 'Actual current private metadata/visibility evidence required')
        require(len(set(owners)) == 1 and remote[1]['parent_ids'] == remote[2]['parent_ids']
                and manifests[2]['requiresParentC48RecoveryDriveFile'] == remote[1]['id'], 'Private source owner/parent chain differs')
        known, base_object_proof = receiving.proved_base_objects(base_proof); pack_proofs = []
        dependency_gitlinks = []
        for dependency in description['dependencies']:
            oid = receiving.git(['-C', str(ROOT), 'rev-parse', HEAD49 + ':' + dependency['path']], 'dependency-gitlink-' + dependency['name']).strip()
            require(oid == dependency['head'], 'Current dependency Git link differs from the exact retained source bundle')
            dependency_gitlinks.append({'path': dependency['path'], 'gitlink': oid, 'wholeRetainedBundle': next(row for row in DEPENDENCIES if Path(row['path']).name == dependency['bundle'])})
        for index, (row, member, old_head, new_head) in enumerate([(TRANSPORTS[0], 'latest-source-C45.bundle', HEAD29, HEAD45),
                                                                (TRANSPORTS[1], 'C45-TO-C48.gitbundle', HEAD45, HEAD48),
                                                                (TRANSPORTS[2], '00-C49-FILES-ASSERTION-SOURCE.gitbundle', HEAD48, HEAD49)]):
            known, proof = receiving.incremental_pack(row, member, old_head, new_head, known, index); pack_proofs.append(proof)
        dependency_objects = {}; dependency_transport_proofs = []
        for dependency in description['dependencies']:
            transport = next(row for row in DEPENDENCIES if Path(row['path']).name == dependency['bundle'])
            boundary = next(row for row in DEPENDENCIES if Path(row['path']).name == dependency['name'] + '.SHALLOW-BOUNDARIES.txt')
            objects, proof = receiving.dependency_source_objects(dependency, transport, boundary)
            dependency_objects[dependency['path']] = (dependency, transport, objects)
            dependency_transport_proofs.append(proof)
        covered = terminal['sourceInputsBefore']
        for row in covered:
            relative = member_name(row['path']); actual = pin(ROOT / relative)
            require(all(actual[key] == row[key] for key in ('bytes', 'sha256')), 'Current corresponding source content differs')
            owner = next((root for root in dependency_objects if relative.startswith(root + '/')), None)
            if owner is None:
                require('gitOwner' not in row and row['gitBlob'] in known, 'Unproved first-party Git owner or transported object')
                object_id = receiving.git(['-C', str(ROOT), 'rev-parse', HEAD49 + ':' + relative], 'covered-' + str(len(receiving.git_stages))).strip()
            else:
                dependency, transport, objects = dependency_objects[owner]; within = member_name(relative[len(owner) + 1:])
                require(row['gitOwner'] == {'kind': 'EXACT_RETAINED_DEPENDENCY_GIT_OWNER', 'relativeRoot': owner,
                                           'parentGitlink': dependency['head'], 'commit': dependency['head'], 'pathWithinOwner': within,
                                           'wholeSourceBundle': transport, 'sourceDependencyDescription': SOURCE_DESCRIPTION}
                        and row['gitBlob'] in objects, 'Exact same dependency-owner/path/blob/transport proof as publisher required')
                dependency_head = receiving.git(['-C', str(ROOT / owner), 'rev-parse', 'HEAD'], 'covered-owner-' + str(len(receiving.git_stages))).strip()
                require(dependency_head == dependency['head'], 'Dependency owner source HEAD changed')
                object_id = receiving.git(['-C', str(ROOT / owner), 'rev-parse', dependency['head'] + ':' + within], 'covered-' + str(len(receiving.git_stages))).strip()
            require(object_id == row['gitBlob'], 'Current corresponding source actual owning Git path differs')
        input_binding = record(terminal['inputBinding']); parent = record(input_binding['currentCompilerReceipt'], True)
        source_peer = record(input_binding['sourcePeer'])
        production_basis = input_binding['qualifiedDependencyCompilerBasis']
        require(terminal['qualifiedDependencyCompilerBasis'] == production_basis, 'Actual publication component basis changed')
        controlling = [terminal['driver'], input_binding['currentCompilerSource'], input_binding['sealer'],
                       parent['exactResourceCustodyHelper'], parent['exactFixtureExecAuditHelper'],
                       parent['exactDesktopRuntimeClosureRestorer'], parent['exactOwningTestRuntimeClosureRestorer'],
                       *source_peer['qualifiedFiles'], *production_basis['protectedBasisPins']]
        mandatory = {str(Path(__file__).absolute()), *[row['path'] for row in controlling if Path(row['path']).suffix == '.py']}
        for row in controlling:
            bound(row)
        supplied = {item['file']['path'] for item in source_bind['files']}
        require(mandatory <= supplied and sum(item['file']['bytes'] for item in source_bind['files']) <= BUILD_LIMIT,
                'Complete actual controlling compiler/driver/restorer/helper source inventory required')
        source_archive = output / 'CURRENT-C49-WRITE-PRESENT-BUILD-SOURCE-COMPANION.zip'; names = set(); rows = []
        with zipfile.ZipFile(source_archive, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
            for item in source_bind['files']:
                name = member_name(item['archiveMember']); path = bound(item['file'])
                historical_restorers = {
                    '/tmp/astra-a4-coherent-owning-compiler-preparation11-20261006/restore-exact-desktop-runtime-closure12.py':
                        {'path': '/tmp/astra-a4-coherent-owning-compiler-preparation11-20261006/restore-exact-desktop-runtime-closure12.py', 'bytes': 3179, 'sha256': '5c711acd6ac1a0f11b0dacccf5258a44d057a4b9d7fef7f1a6711426242af29c'},
                    '/tmp/astra-a4-coherent-owning-compiler-preparation14-20261006/restore-exact-owning-test-runtime-closure14.py':
                        {'path': '/tmp/astra-a4-coherent-owning-compiler-preparation14-20261006/restore-exact-owning-test-runtime-closure14.py', 'bytes': 4339, 'sha256': '33f4fbe3cd15fd5fc6c77e0b547e87095007793d2474fbdf7588c3fb91906fb3'}}
                exact_retained_restorer = str(path) in historical_restorers and item['file'] == historical_restorers[str(path)] and item['file'] in controlling
                require(name.casefold() not in names and (path.is_relative_to(Path('/workspace/astra-source')) or exact_retained_restorer)
                        and path.suffix in ('.py', '.json', '.md', '.props', '.targets', '.csproj', '.config'), 'Only exact isolated source or the two whole pinned retained restorers accepted')
                names.add(name.casefold()); archive.write(path, name)
                rows.append({'path': name, 'bytes': item['file']['bytes'], 'sha256': item['file']['sha256']})
        with zipfile.ZipFile(source_archive) as archive:
            require(sorted(archive.namelist()) == sorted(row['path'] for row in rows), 'Whole build-source companion membership differs')
            for row in rows:
                body = archive.read(row['path']); require(len(body) == row['bytes'] and hashlib.sha256(body).hexdigest() == row['sha256'], 'Whole build-source ZIP roundtrip differs')
        protected = [terminal_pin, source_bind_pin, terminal['inputBinding'], input_binding['currentCompilerReceipt'], input_binding['sourcePeer'],
                     SOURCE_DESCRIPTION, FRESH_BASE_PROOF, SHALLOW, *PARTS, *DEPENDENCIES, *TRANSPORTS, *METADATA,
                     *controlling, *[item['file'] for item in source_bind['files']]]
        for row in protected:
            bound(row)
        require(receiving.git(['-C', str(ROOT), 'rev-parse', 'HEAD'], 'final-source-head').strip() == HEAD49
                and not receiving.git(['-C', str(ROOT), 'diff', '--name-only'], 'final-worktree').strip()
                and not receiving.git(['-C', str(ROOT), 'diff', '--cached', '--name-only'], 'final-index').strip(), 'Source head/worktree/index changed during companion receiving')
        for row in covered:
            actual = pin(ROOT / member_name(row['path']))
            require(all(actual[key] == row[key] for key in ('bytes', 'sha256')), 'Corresponding current source changed before publication')
        result = {'status': 'EXACT_PRIVATE_CURRENT_CORRESPONDING_SOURCE_HANDOFF', 'sourceCommit': HEAD49,
                  'actualSdkTerminalReceipt': terminal_pin, 'coveredSourceInputs': covered, 'completeCurrentSourceBuildAndLicenseFilesIncluded': True,
                  'modelWeightsOrCredentialsIncluded': False, 'wholeTransportPins': [*PARTS, SHALLOW, *DEPENDENCIES, *TRANSPORTS, pin(source_archive)],
                  'originalFullSourceFreshReceivingProof': FRESH_BASE_PROOF, 'originalSourceDependencyDescription': SOURCE_DESCRIPTION,
                  'originalFullBaseBoundedObjectGraphReadback': base_object_proof, 'actualCurrentRetainedDependencyGitlinks': dependency_gitlinks,
                  'wholeSourceTransportZipMemberProofs': zip_proofs, 'actualIncrementalSourcePackCoverage': pack_proofs,
                  'actualContainedDependencySourceObjectCoverage': dependency_transport_proofs,
                  'smallCurrentBuildSourceCompanion': {'archive': pin(source_archive), 'files': rows, 'inputBinding': source_bind_pin},
                  'privateDeliveryMetadataEvidence': METADATA, 'currentPrivateDriveIds': [item['id'] for item in remote],
                  'historicalFullBasePartDriveIdsFromOriginalReceipt': [row['driveId'] for row in description['parts']],
                  'remoteContentSha256IndependentlyObserved': False, 'originalFullBasePrivatePermissionsNotReobservedHere': True,
                  'gitReceivingStages': receiving.git_stages, 'sourceGitPrivateInputsBeforeAfterWholeEqual': True,
                  'distributionLicenseReviewClaimed': False, 'binaryLicenseMappingsRemainSeparatelyRequired': True,
                  'compilerSdkTestsAppsModelsUploadsExecuted': False, 'runtimeAcceptanceClaimed': False}
        path = output / 'CURRENT-C49-PRIVATE-CORRESPONDING-SOURCE-RECEIPT.json'
        with path.open('x') as stream:
            json.dump(result, stream, indent=2); stream.write('\n')
        require(all(pin(row['path']) == row for row in protected), 'Source input changed during final receipt publication')
        for row in covered:
            actual = pin(ROOT / member_name(row['path']))
            require(all(actual[key] == row[key] for key in ('bytes', 'sha256')), 'Current source changed during final receipt publication')
        require(receiving.git(['-C', str(ROOT), 'rev-parse', 'HEAD'], 'postpublication-source-head').strip() == HEAD49
                and not receiving.git(['-C', str(ROOT), 'diff', '--name-only'], 'postpublication-worktree').strip()
                and not receiving.git(['-C', str(ROOT), 'diff', '--cached', '--name-only'], 'postpublication-index').strip(), 'Current Git source changed during final publication')
        require(allocation(output) <= WORK_LIMIT and os.statvfs(BASE).f_bavail * os.statvfs(BASE).f_frsize >= SPARE, 'Actual complete source companion disk/spare budget exceeded')
        print(json.dumps(pin(path)), flush=True)
    except BaseException as primary:
        try:
            with (output / 'SOURCE-COMPANION-ACTUAL-FAILURE-PRESERVED.json').open('x') as stream:
                json.dump({'status': 'PRIVATE_SOURCE_COMPANION_FAILURE_PRESERVED_NO_DELIVERY_PROMOTION', 'originalException': repr(primary),
                           'actualGitStages': receiving.git_stages, 'sdkExecuted': False, 'runtimeAcceptanceClaimed': False}, stream, indent=2)
        except BaseException as publication_cause:
            primary.add_note('Independent failure-receipt publication cause: ' + repr(publication_cause))
        raise


if __name__ == '__main__':
    main()
