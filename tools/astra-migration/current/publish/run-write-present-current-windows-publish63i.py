#!/usr/bin/env python3
"""Root-only ordinary SDK receiving driver. Preparation of this file executes no SDK.

Reuse is of byte-qualified current dependency products, never of a new application
compiler. Root binds the actual successful current compiler and Desktop closure.
The new Write and existing Present owners each restore, evaluate, resolve, build
and publish through the genuine SDK. No tests, models or applications are run.
"""
import argparse
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path('/workspace/astra-consolidated')
BASE = Path('/workspace/astra-source/friday-write-present-sol61u62')
SCOPE = Path('/dev/shm/astra-framework-owning-metadata-20261006-03')
SHADOW = SCOPE / 'source-root'
SDK = Path('/workspace/astra-tools/dotnet-10.0.401/dotnet')
CACHE = Path('/workspace/astra-tools/nuget')
SDK_PIN = {'path': str(SDK), 'bytes': 71808,
           'sha256': '01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269'}
PACKET_PIN = {'path': str(BASE / 'WRITE-PRESENT62C-EXACT-SOURCE-CANDIDATE-AND-PROGRESSIVE-PACKAGE-HANDOFF01.json'),
              'bytes': 13484, 'sha256': '2c76db9a1dadc06047578f67b01c7a625e65e5a219e5fd586de0b6b9d7748c86'}
SEALER_PIN = {'path': str(BASE / 'package/seal-write-present-win62.py'),
              'bytes': 13501, 'sha256': 'df45dbdcf8f7c9a91bab1628e21374c61e655a344129c51f03ab76ee1c6bec26'}
CUSTODY_SHA = '0d650d3f4be643adbaa60e1ee748389be7c869deb67feb959700f3c80fd732b2'
REGISTRY_SHA = 'd7b117d1d919bcdd9dbc95cefd9e241f02ab4194a3b2bbf7c4793c32ccc2bfa4'
RECONSTRUCTION_LIMIT = 182374123
RESIDENT_ADMISSION = 534695659 + 67108864
DISK_ADMISSION = 1218122064
DISK_SPARE = 134217728
EVIDENCE_LIMIT = 35559951 + 67108864
OWNER_RESIDENT_LIMIT = 67108864
HEAP_LIMIT = '0x50000000'
LOG_LIMIT = 8388608
APPS = ('Write', 'Present')
PROTECTED_KINDS = ('output', 'target', 'pdb', 'reference')


def require(value, message):
    if not value:
        raise RuntimeError(message)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def sha_file(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1048576), b''):
            value.update(block)
    return value.hexdigest()


def pin(path):
    path = Path(path)
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), 'Regular absolute whole file required: ' + str(path))
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha_file(path)}


def bound(row):
    require(set(row) == {'path', 'bytes', 'sha256'} and pin(row['path']) == row, 'Whole exact file binding differs: ' + str(row.get('path')))
    return Path(row['path'])


def read_bound(row, zipped=False):
    raw = bound(row).read_bytes()
    return json.loads(gzip.decompress(raw) if zipped else raw)


def git(*arguments):
    return subprocess.check_output(['git', '-C', str(ROOT), *arguments])


def tracked_changes():
    return sorted(set(git('diff', '--name-only').decode().splitlines() + git('diff', '--cached', '--name-only').decode().splitlines()))


def exclusive_json(path, value):
    raw = json.dumps(value, indent=2, ensure_ascii=False).encode() + b'\n'
    with Path(path).open('xb') as stream:
        stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    require(Path(path).read_bytes() == raw, 'Whole receipt publication differs')
    return pin(path)


def physical_tree(root):
    files = []; blocks = 0; directories = 0; seen = set()
    if not root.exists():
        return {'files': [], 'logicalBytes': 0, 'uniqueFileAllocatedBytes': 0, 'directoryAllocatedBytes': 0, 'totalAllocatedBytes': 0}
    require(root.is_dir() and root.resolve() == root and not root.is_symlink(), 'Direct task-owned directory required')
    for path in [root, *sorted(root.rglob('*'))]:
        require(not path.is_symlink() and path.resolve() == path, 'Task-owned publication/evidence links rejected')
        stat = path.stat()
        if path.is_dir():
            directories += stat.st_blocks * 512
        else:
            require(path.is_file(), 'Unexpected task-owned file type')
            files.append({'path': str(path), 'bytes': stat.st_size, 'sha256': sha_file(path), 'device': stat.st_dev, 'inode': stat.st_ino, 'nlink': stat.st_nlink})
            if (stat.st_dev, stat.st_ino) not in seen:
                seen.add((stat.st_dev, stat.st_ino)); blocks += stat.st_blocks * 512
    return {'files': files, 'logicalBytes': sum(x['bytes'] for x in files), 'uniqueFileAllocatedBytes': blocks,
            'directoryAllocatedBytes': directories, 'totalAllocatedBytes': blocks + directories}


def free(path):
    stat = os.statvfs(path)
    return stat.f_bavail * stat.f_frsize


def process_admission():
    active = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdecimal() or int(entry.name) == os.getpid():
            continue
        try:
            argv = [part.decode('utf-8', 'replace') for part in (entry / 'cmdline').read_bytes().split(b'\0') if part]
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        if not argv:
            continue
        name = Path(argv[0]).name
        sdk = name in ('dotnet', 'csc', 'csc.dll') and any('astra-tools/dotnet' in arg or 'Roslyn/bincore' in arg for arg in argv)
        model = name in ('llama-server', 'llama-cli', 'strata-worker', 'strata_worker')
        console = name == 'dotnet' and any(arg.endswith('Haven.dll') for arg in argv)
        orchestrator = name.startswith('python') and len(argv) > 1 and Path(argv[1]).name.startswith(('run-current-root-selective-component-compiler', 'run-current-root-normal-closure-recovery', 'run-c45-selective-component-compiler', 'run-write-present-current-windows-publish'))
        if sdk or model or console or orchestrator:
            active.append({'pid': int(entry.name), 'executable': name, 'kind': 'sdk' if sdk or orchestrator else 'model' if model else 'console'})
    require(not active, 'Root must schedule an exclusive SDK interval; existing work remains running: ' + repr(active))
    return active


def admission(output):
    require(not tracked_changes(), 'Tracked source must be clean before any SDK process')
    active = process_admission()
    mount = [line for line in Path('/proc/self/mountinfo').read_text().splitlines() if line.split()[4] == '/dev']
    require(mount and ' - tmpfs ' in mount[-1] and mount[-1].split()[5].split(',')[0] == 'rw', 'Default private writable /dev tmpfs required; never overlay host /dev/shm')
    require(Path('/dev').stat().st_dev == SCOPE.parent.stat().st_dev and list(SCOPE.parent.iterdir()) == [], 'Default tool-private /dev/shm must be empty')
    require(not output.exists() and not output.is_symlink() and output.is_relative_to(BASE) and output.resolve(strict=False) == output, 'Fresh direct isolated output below this lane required')
    require(free(ROOT) >= DISK_ADMISSION, 'Full real-disk normal/evidence/spare admission unavailable')
    memory_max = Path('/sys/fs/cgroup/memory.max').read_text().strip()
    memory_current = int(Path('/sys/fs/cgroup/memory.current').read_text())
    require(memory_max.isdecimal() and int(memory_max) - memory_current >= RESIDENT_ADMISSION, 'Actual cgroup resident headroom unavailable')
    require(free(SCOPE.parent) >= RESIDENT_ADMISSION, 'Whole private reconstruction/owner metadata tmpfs headroom unavailable')
    output.mkdir(mode=0o700); SCOPE.mkdir(mode=0o700)
    namespace_guard = SCOPE.parent / ('astra-write-present63-' + str(os.getpid()) + '-' + str(time.time_ns()))
    namespace_guard.mkdir(mode=0o700)
    require(output.stat().st_dev == ROOT.stat().st_dev, 'Publication must occupy the real workspace filesystem')
    return {'actualMountNamespace': os.readlink('/proc/self/ns/mnt'), 'privateDevMount': mount,
            'scopeDevice': SCOPE.stat().st_dev, 'scopeInode': SCOPE.stat().st_ino, 'uniqueGuard': str(namespace_guard),
            'actualProcessScan': active, 'workspaceFreeBeforeCreation': free(ROOT),
            'memoryMax': int(memory_max), 'memoryCurrent': memory_current, 'requiredResidentBytes': RESIDENT_ADMISSION,
            'conservedCompilerResidentBytes': 534695659, 'additionalNewOwnerResidentBytes': OWNER_RESIDENT_LIMIT,
            'declaredDiskEnvelopeBytes': DISK_ADMISSION, 'diskFinalSpareBytes': DISK_SPARE,
            'newPrivateLifetimeHistoricalInodesNotClaimed': True, 'hostPrivateScopeNeverExposed': True, 'noCacheCredit': True}


def import_pure(path, name):
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def qualify_current_production_basis(receipt, head, read_record, verify_record, desktop_component_pin=None):
    failed66_component = receipt['status'] == 'ACTUAL_NORMAL_CLOSURE_RECOVERY66_FAILURE_PRESERVED'
    require(receipt['headAtCapture'] == receipt['headAtCompletion'] == head and receipt['inputsUnchangedAtCompletion']
            and not receipt['changedInputs'] and not receipt['unavailableInputsAtCompletion'],
            'Genuine current whole unchanged production inputs required')
    require(receipt['exitCode'] == (1 if failed66_component else 0), 'Original current compiler/recovery natural status differs')
    component = None
    if failed66_component:
        failed66_pin = {'path': '/workspace/astra-source/a4-current-targeted-owning-compiler66/output/CURRENT-TARGETED-OWNING-COMPILER66-RECEIPT.json.gz',
                        'bytes': 3255894, 'sha256': 'd7f0d94403dbd4efc96973a0471475ab9b1bc811f90715720e12f0e0d3d53d23'}
        verify_record(failed66_pin)
        require(read_record(failed66_pin, True) == receipt, 'Only the exact preserved failed66 recovery observation is admitted')
        cause = receipt['actualPrimaryException']
        require(cause['type'] == 'AssertionError' and cause['message'] == ''
                and "assert compiled_now and all(q['compilerDecisionComplete'] for q in runtime_queue)" in cause['actualTraceback']
                and receipt['actualOwningTestRuntimeClosureManifests'] == []
                and receipt['releasedNewTaskOwnedRuntimeCopies'] == [] and receipt['wholeReleasedCopyRestorationRevalidation'] == [],
                'Failed66 cleanup cause or unaccepted normal collection differs')
        require(desktop_component_pin is not None, 'Separate genuinely qualified failed66 Desktop normal component custody required')
        component = read_record(desktop_component_pin)
        require(component['status'] == 'ACTUAL_CURRENT66_FAILED_INTERVAL_DESKTOP_NORMAL_COMPONENT_CUSTODY67_PASS_NO_FULL_INTERVAL_ACCEPTANCE'
                and component['originalReceipt'] == component['currentCompilerReceipt'] == failed66_pin
                and component['originalNaturalExitCode'] == component['original66NaturalExitCode'] == 1
                and component['original66FullIntervalNeverPromoted'] is True and component['acceptedDesktopNormalComponent'] is True
                and component['fullCompilerIntervalAccepted'] is False and component['fullNormalCollectionAccepted'] is False
                and component['runtimeReady'] is False and component['testsAccepted'] is False
                and component['head'] == head and component['ticket'] == receipt['ticket'] and component['inputsUnchanged'] is True
                and component['activeSDKNone'] is True and component['activeSDK'] is None and component['modelOrConsoleNone'] is True
                and component['currentCompiledOutputPins'] == receipt['outputs'] and component['actualFreshCscOwners'] == []
                and component['genuinePriorCompilerReusedCscOwners'] == receipt['genuinePriorCompilerReusedCscOwners']
                and component['actualFailed65ComponentCompilerReceipt'] == receipt['actualFailed65ComponentCompilerReceipt']
                and component['actualFailed65IndividualComponentCustody'] == receipt['actualFailed65IndividualComponentCustody'],
                'Exact separately qualified Desktop component/current22/failed65+66 history required')
        manifest_pin = receipt['actualDesktopRuntimeClosureManifest']
        require(component['actualDesktopRuntimeClosureManifest'] == manifest_pin
                == {'path': '/workspace/astra-source/a4-current-targeted-owning-compiler66/output/Haven.Desktop-NORMAL-RUNTIME-OUTPUT-CLOSURE01.json.gz',
                    'bytes': 36885, 'sha256': 'd6b32d8406172b0e2728af0c49d9b86d09f5b382763e6a661e5c9099607214fe'},
                'Only the genuine current66 Desktop normal-stage0 manifest is admitted')
        manifest = read_record(manifest_pin, True)
        require(manifest['status'] == 'ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS'
                and manifest['head'] == head and manifest['ticket'] == receipt['ticket']
                and manifest['actualRuntimeStage']['exitCode'] == 0
                and component['actualDesktopRuntimeStage'] == manifest['actualRuntimeStage']
                and component['actualDesktopCompilerSuccessProvenance'] == manifest['actualCompilerSuccessProvenance']
                and component['qualifiedDesktopNormalFileCount'] == manifest['fileCount'] == 264
                and component['qualifiedDesktopNormalRawBytes'] == manifest['wholeCopiedBytes'] == 944837269,
                'Complete genuine264-file Desktop normal closure/component provenance differs')
        files = component['qualifiedDesktopNormalFileReadbacks']
        require(len(files) == 264 and [row['original'] for row in files] == [row['original'] for row in manifest['files']]
                and all(row['wholeRestorationVerified'] is True and row['physicalAtComponentQualification'] is True for row in files)
                and len(component['actualNormalRuntimeClosures']) == 1
                and component['actualNormalRuntimeClosures'][0]['module'] == 'Haven.Desktop'
                and component['actualNormalRuntimeClosures'][0]['manifest'] == manifest_pin
                and component['actualNormalRuntimeClosures'][0]['normalOutputFilesFullyReconstructable'] is True,
                'Every exact Desktop file needs complete archive/package plus historical physical qualification')
        require(component['componentQualificationSource']
                == {'path': '/workspace/astra-source/lifecycle-peer-sol61u51/desktop-component67/verify-failed66-desktop-normal-component67.py',
                    'bytes': 24790, 'sha256': '761f0ce10edc7d7757ad1b5e0b92200916a053468851e3f19f519a68404780a0'}
                and component['componentQualificationSourceProof']
                == {'path': '/workspace/astra-source/lifecycle-peer-sol61u51/desktop-component67/CURRENT66-DESKTOP-COMPONENT67-EXACT-FINITE-SOURCE-PROOF01.json',
                    'bytes': 10818, 'sha256': '8c3ab093dedab37af1b7e4dc356e00c3129eee7a308e0ffddd84d6fdafb42614'},
                'Exact independently received Desktop component qualifier/proof required')
    outputs = {row['module']: row for row in receipt['outputs']}
    plans = {row['module']: row for row in receipt['modulePlans']}
    require(len(outputs) == len(plans) == 22 and outputs.keys() == plans.keys(), 'Exactly22 genuine current dependency owners required')
    desktop = outputs['Haven.Desktop']
    if receipt['status'] == 'ACTUAL_TARGETED_OWNING_COMPILER_PASS':
        require(desktop['compiledInCurrentInterval'] is True and desktop['compiledSourceHead'] == head,
                'Fresh current Desktop compilation required for the changed shared pages')
        return {'kind': 'GENUINE_CURRENT_SUCCESSFUL_COMPILER_WITH_FRESH_DESKTOP', 'protectedBasisPins': []}
    require((failed66_component or receipt['status'] == 'ACTUAL_NORMAL_CLOSURE_RECOVERY66_PASS') and receipt['normalClosureRecoveryOnly'] is True
            and receipt['compilerRequested'] is False and receipt['actualFreshCscOwners'] == []
            and receipt['componentBasisOriginalNaturalExitCode'] == 1
            and receipt['componentBasisIsQualifiedFailed65IndividualComponents'] is True
            and receipt['preservedFailed65NeverClaimedFullSuccessfulInterval'] is True
            and receipt['reusedCompilerOutputNotRelabeledCurrent'] is True,
            'Only genuine successful normal recovery from separately qualified failed65 components is admitted')
    original_pin = receipt['actualFailed65ComponentCompilerReceipt']
    require(original_pin == {'path': '/workspace/astra-source/a4-current-targeted-owning-compiler65/output/CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz',
                            'bytes': 4844261, 'sha256': 'c9bba5c0ad33af23580ebf2eed255afa6ca731c6541b99d6a4797346fb77c64f'},
            'Exact immutable naturally failed65 compiler receipt required')
    custody_pin = receipt['actualFailed65IndividualComponentCustody']
    original = read_record(original_pin, True); custody = read_record(custody_pin)
    require(original['status'] == 'ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED' and original['exitCode'] == 1
            and original['headAtCapture'] == original['headAtCompletion'] == custody['head'] == head
            and original['ticket'] == custody['ticket'] == receipt['ticket'] and original['inputsUnchangedAtCompletion']
            and not original['changedInputs'] and not original['unavailableInputsAtCompletion']
            and original['actualPrimaryException'] == {'type': 'AssertionError', 'message': ''},
            'Original65 failure/head/input/provenance must remain its actual observation')
    require(custody['status'] == 'ACTUAL_CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY_PASS_NO_NORMAL_OR_RUNTIME_ACCEPTANCE'
            and custody['originalReceipt'] == custody['currentCompilerReceipt'] == original_pin
            and custody['originalNaturalExitCode'] == 1 and custody['fullCompilerIntervalAccepted'] is False
            and custody['normalClosureAccepted'] is False and custody['runtimeReady'] is False
            and custody['inputsUnchanged'] is True and custody['activeSDKNone'] is True and custody['activeSDK'] is None
            and custody['modelOrConsoleNone'] is True and custody['actualNormalRuntimeClosures'] == []
            and custody['currentCompiledOutputPins'] == original['outputs'],
            'Whole independently qualified failed65 individual component custody required; no original normal/full success promotion')
    old_outputs = {row['module']: row for row in original['outputs']}
    old_plans = {row['module']: row for row in original['modulePlans']}
    decisions = {row['module']: row for row in receipt['actualReuseDecisions']}
    require(outputs.keys() == old_outputs.keys() == old_plans.keys() == decisions.keys()
            and len(receipt['genuinePriorCompilerReusedCscOwners']) == 22
            and set(receipt['genuinePriorCompilerReusedCscOwners']) == set(outputs)
            and all(stage['exitCode'] == 0 for stage in original['stages'])
            and not any(stage['stage'].endswith('-compiler01') for stage in receipt['stages']),
            'Exactly22 preserved successful individual products and no actual recovery Csc required')
    require(len(original['actualFreshCscOwners']) == 20 and set(original['genuinePriorCompilerReusedCscOwners']) == {'Haven.Core', 'Haven.PluginFixture'}
            and len(custody['individuallySuccessfulCurrentComponentCompilerReadbacks']) == 20,
            'Original20 actual fresh Csc plus2 separately qualified historical reuse origins required')
    for module, product in outputs.items():
        before = old_outputs[module]; old_plan = old_plans[module]; current = plans[module]; decision = decisions[module]
        require(product['compiledInCurrentInterval'] is False and product['compiledSourceHead'] == before['compiledSourceHead']
                and product['originalComponentCompiledInActual65Interval'] is before['compiledInCurrentInterval']
                and product['reusedFromActualCompilerReceipt'] == original_pin
                and product['reusedFromActualIndividualComponentCustody'] == custody_pin
                and product['reusedReceiptOriginalNaturalExitCode'] == 1
                and product['completeSourceItemResourceOrderEqual'] is True
                and product['actualCompilerReferenceAndAnalyzerBytesEqual'] is True
                and all(product.get(kind) == before.get(kind) for kind in ('output', 'target', 'pdb', 'reference')),
                'Whole qualified original component/output/ref/compile-head differs: ' + module)
        require(current['canonicalProject'] == old_plan['canonicalProject'] and current['project'] == old_plan['project']
                and current['properties'] == old_plan['properties']
                and [(row['kind'], row['logical']) for row in current['physicalItems']] == [(row['kind'], row['logical']) for row in old_plan['physicalItems']]
                and [(row['kind'], row['input']) for row in current['actualReferencePins']] == [(row['kind'], row['input']) for row in old_plan['actualReferencePins']]
                and current['actualUpstreamReferenceClosure'] == old_plan['actualUpstreamReferenceClosure']
                and decision['actualCompilerRefsAndAnalyzersWholeEqual'] is True
                and decision['completeSourceItemResourceOrderEqual'] is True and decision['allResolvedRefsWholeEqual'] is True
                and decision['freshCscRequired'] is False and decision['unreviewedAbiWaiver'] is False,
                'Current whole source/resource/property/ref/analyzer/upstream equality missing: ' + module)
    require(receipt['transparentReusedCompiledSourceHeads'] == {name: row['compiledSourceHead'] for name, row in old_outputs.items()},
            'Original compiled source heads were relabelled by recovery')
    old_desktop = old_outputs['Haven.Desktop']
    require(old_desktop['compiledInCurrentInterval'] is True and old_desktop['compiledSourceHead'] == head,
            'Genuine original65 fresh Desktop compilation at the current source revision required')
    original_stage = next(stage for stage in original['stages'] if stage['stage'] == 'Haven.Desktop-compiler01')
    require(original_stage['exitCode'] == 0 and isinstance(original_stage['pid'], int) and original_stage['pid'] > 0
            and original_stage['log'] == original_stage['actualChildOutputPin'] == original_stage['exactWholeStdoutCustody']['original'],
            'Actual original65 Desktop Csc0 and complete original stdout provenance required')
    protected = [original_pin, custody_pin, original['exactSelectiveCompilerSource']]
    for key in ('failureCustodyVerifier', 'failureCustodySourceProof', 'exactRootInputBinding'):
        protected.append(custody[key])
    if component is not None:
        current_readbacks = component['wholeCurrent22ComponentReadbacks']
        require(len(current_readbacks) == 22 and {row['module'] for row in current_readbacks} == set(outputs)
                and all(row['newCscInvoked'] is False and row['wholeCurrentSourceResourcePropertiesRefsAnalyzerUpstreamEqual'] is True
                        and row['originalCompiledSourceHead'] == old_outputs[row['module']]['compiledSourceHead']
                        and row['original65CompiledInInterval'] is old_outputs[row['module']]['compiledInCurrentInterval'] for row in current_readbacks),
                'All22 current exact component comparisons and genuine original compile heads required')
        for module, current_plan in plans.items():
            for before_ref, current_ref in zip(old_plans[module]['actualReferencePins'], current_plan['actualReferencePins']):
                require({key: value for key, value in before_ref['metadata'].items() if key != 'AccessedTime'}
                        == {key: value for key, value in current_ref['metadata'].items() if key != 'AccessedTime'},
                        'Current reference metadata apart from mutable read-access time differs: ' + module)
        protected.extend([failed66_pin, desktop_component_pin, component['componentQualificationSource'],
                          component['componentQualificationSourceProof'], component['exactSourcePeer'], component['exactWholeRootLog']])
    for row in protected:
        verify_record(row)
    return {'kind': ('GENUINE_QUALIFIED_DESKTOP_NORMAL_COMPONENT_OF_FAILED66_RECOVERY_AND_FAILED65_COMPILER'
                     if failed66_component else 'GENUINE_SUCCESSFUL_RECOVERY66_OF_QUALIFIED_FAILED65_INDIVIDUAL_COMPONENTS'),
            'qualifiedDesktopNormalComponent': desktop_component_pin if failed66_component else None,
            'originalRecoveryIntervalNaturalExitCode': receipt['exitCode'],
            'fullRecoveryNormalCollectionAccepted': not failed66_component,
            'protectedBasisPins': protected, 'originalFailed65Receipt': original_pin, 'individualComponentCustody': custody_pin,
            'originalCompilerIntervalNaturalExitCode': 1, 'currentRecoveryCompilerInvoked': False,
            'originalDesktopCompilerStage': original_stage, 'originalDesktopActualCscReferencePins': old_desktop['actualCscReferencePins'],
            'originalDesktopCompilerProduct': {key: old_desktop[key] for key in ('output', 'target', 'pdb', 'reference', 'compiledSourceHead')},
            'wholeCurrent22SourceResourcePropertyRefAnalyzerUpstreamEqual': True, 'runtimeAcceptanceClaimed': False}


class Driver:
    def __init__(self, binding, input_pin):
        self.binding = binding; self.input_pin = input_pin
        self.head = binding['sourceCommit']; self.output = Path(binding['outputRoot'])
        self.stages = []; self.receipts = []; self.restored = []; self.capacity = []; self.immutable = {}; self.package_provenance = {}; self.owner_packs = {}
        self.parent = read_bound(binding['currentCompilerReceipt'], True)
        self.desktop = read_bound(binding['currentDesktopClosure'], True)
        require(re.fullmatch('[a-f0-9]{40}', self.head) and git('rev-parse', 'HEAD').decode().strip() == self.head, 'Independent current source head differs')
        require(binding['status'] == 'ROOT_SCHEDULED_CURRENT_WRITE_PRESENT_WINDOWS_BUILD_PUBLISH' and binding['runtimeAcceptanceClaimed'] is False, 'Root scheduled receiving binding required')
        require(binding['sdk'] == SDK_PIN and binding['sourcePacket'] == PACKET_PIN and binding['sealer'] == SEALER_PIN, 'Exact maintained SDK/source packet/sealer bindings differ')
        for descriptor in (SDK_PIN, PACKET_PIN, SEALER_PIN, binding['currentCompilerSource']):
            bound(descriptor); self.immutable[descriptor['path']] = descriptor
        require(binding['driver'] == pin(Path(__file__).absolute()), 'Exact reviewed driver source binding differs')
        peer = read_bound(binding['sourcePeer'])
        require(peer['status'] == 'QUALIFIED_INDEPENDENT_SOURCE_PASS_WRITE_PRESENT_WINDOWS_DRIVER63_RUNTIME_UNEXECUTED' and binding['driver'] in peer['qualifiedFiles'], 'Independent finite source receiving peer required')
        self.immutable[binding['sourcePeer']['path']] = binding['sourcePeer']
        p = self.parent
        self.production_basis = qualify_current_production_basis(p, self.head, read_bound, bound, binding.get('qualifiedDesktopNormalComponent'))
        require(binding['qualifiedDependencyCompilerBasis'] == self.production_basis, 'Exact independently Root-bound qualified component basis differs')
        for descriptor in self.production_basis['protectedBasisPins']:
            self.immutable[descriptor['path']] = descriptor
        require(p['inputsUnchangedAtCompletion'] and not p['changedInputs'] and not p['unavailableInputsAtCompletion'], 'Current compiler inputs changed or unavailable')
        require(not any(p[name] for name in ('missingPackages', 'independentTestCompilerFailures', 'desktopCompilerFailures', 'desktopRuntimeOutputFailures', 'owningTestRuntimeOutputFailures')), 'Current normal owner compiler/runtime closure has a failure')
        require(p['actualDesktopRuntimeClosureManifest'] == binding['currentDesktopClosure'] and p['exactSelectiveCompilerSource'] == binding['currentCompilerSource'], 'Actual current compiler/source/closure chain differs')
        d = self.desktop
        require(d['status'] == 'ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS' and d['head'] == self.head and d['ticket'] == p['ticket']
                and d['actualRuntimeStage']['exitCode'] == 0 and d['allActualCompiledProductIdentitiesUnchanged'] and d['noSecondCompiler'], 'Actual current complete Desktop normal closure required')
        require(len(d['files']) == d['fileCount'] and sum(x['original']['bytes'] for x in d['files']) == d['wholeCopiedBytes'], 'Complete Desktop closure count/whole bytes differ')
        self.current_outputs = {row['module']: row for row in p['outputs']}
        require(self.current_outputs['Haven.Desktop']['compiledSourceHead'] == self.head, 'Genuine original Desktop source revision differs')
        self.plans = {row['module']: row for row in p['modulePlans']}
        require(len(self.current_outputs) == len(self.plans) == 22 and not any(name in self.plans for name in ('HavenOS.Write', 'HavenOS.Present')), 'External app owners must remain separate from the genuine existing22')
        for descriptor in (binding['currentCompilerReceipt'], binding['currentDesktopClosure'], binding['currentCompilerSource']):
            self.immutable[descriptor['path']] = descriptor
        helper = d['exactResourceCustodyHelper']; registry = d['closedLayerArchiveRegistry']
        require(helper['sha256'] == CUSTODY_SHA and registry['sha256'] == REGISTRY_SHA, 'Original whole byte-custody helper/registry differ')
        self.lib = import_pure(bound(helper), 'write_present63_exact_custody')
        self.layers = read_bound(registry)
        self.immutable[helper['path']] = helper; self.immutable[registry['path']] = registry
        self.sealer = import_pure(bound(SEALER_PIN), 'write_present63_exact_sealer')
        if self.production_basis['kind'] in ('GENUINE_SUCCESSFUL_RECOVERY66_OF_QUALIFIED_FAILED65_INDIVIDUAL_COMPONENTS',
                                             'GENUINE_QUALIFIED_DESKTOP_NORMAL_COMPONENT_OF_FAILED66_RECOVERY_AND_FAILED65_COMPILER'):
            origin = d['actualCompilerSuccessProvenance']; original_stage = self.production_basis['originalDesktopCompilerStage']
            require(origin['currentCompilerInvoked'] is False and origin['compiledSourceHead'] == self.head
                    and origin['actualQualifiedComponentCustody'] == self.production_basis['individualComponentCustody']
                    and origin['actualFailed65ComponentCompilerReceipt'] == self.production_basis['originalFailed65Receipt']
                    and origin['originalCompilerIntervalNaturalExitCode'] == 1 and origin['failed65FullIntervalNeverPromoted'] is True
                    and origin['completeCurrentSourceResourceCscRefAnalyzerEquivalence'] is True
                    and origin['originalComponentCompiledInActual65Interval'] is True
                    and origin['actualOriginalNaturalExitCode'] == 0 and origin['actualOriginalCompilerStage'] == original_stage
                    and origin['actualOriginalCompilerReceipt'] == self.production_basis['originalFailed65Receipt']
                    and origin['originalCscActuallyInvoked'] is True, 'Successful66 Desktop closure has no exact actual65 fresh Csc provenance')
            raw = self.decode_archive(original_stage['exactWholeStdoutCustody'])
            require(len(raw) == original_stage['log']['bytes'] and hashlib.sha256(raw).hexdigest() == original_stage['log']['sha256'],
                    'Original65 Desktop compiler whole stdout differs')
            body = raw.decode('utf-8'); del raw
            require(re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)', body), 'Actual original65 fresh Desktop Csc command absent')
            names = [name for match in re.finditer(r'/reference:(?:"([^"]+)"|([^\s]+))', body)
                     for name in (match.group(1) or match.group(2)).split(',')]
            require(names and set(names) == {row['path'] for row in self.production_basis['originalDesktopActualCscReferencePins']},
                    'Original actual65 Desktop Csc references differ from original whole qualified reference witness')
        self.source_paths = self.capture_source_paths()
        self.sources_before = self.sources()
        self.source_row_index = {row['path']: row for row in self.sources_before}
        self.source_binding = hashlib.sha256(canonical(self.sources_before)).hexdigest()

    def capture_source_paths(self):
        paths = set()
        for item in self.parent['inputs']:
            path = Path(item['path'])
            if path.is_relative_to(ROOT):
                relative = path.relative_to(ROOT).as_posix()
            elif path.is_relative_to(SHADOW):
                relative = path.relative_to(SHADOW).as_posix()
            else:
                continue
            current = ROOT / relative
            require(current.is_file() and not current.is_symlink(), 'Current controlling source must be a physical tracked file: ' + relative)
            require({k: pin(current)[k] for k in ('bytes', 'sha256')} == {k: item[k] for k in ('bytes', 'sha256')}, 'Parent compiler source is stale at ' + relative)
            paths.add(relative)
        for raw in git('ls-files', '-z', '--', '9to1 Workspace/Write', '9to1 Workspace/Present').split(b'\0'):
            if raw:
                paths.add(raw.decode())
        packet = read_bound(PACKET_PIN)
        for row in packet['proposalFiles']:
            relative = row['target']; actual = pin(ROOT / relative)
            require({k: actual[k] for k in ('bytes', 'sha256')} == {k: row['candidate'][k] for k in ('bytes', 'sha256')}, 'The exact immutable14-path candidate has not been integrated: ' + relative)
            paths.add(relative)
        paths.add('LICENSE')
        for relative in ('framework/CUI/LICENSES/Avalonia-MIT.md', 'framework/CUI/vendor/Avalonia/NOTICE.md', '9to1 Workspace/Home/Source/Dulche/llamacpp/licenses/llama.cpp-LICENSE', '9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/LICENSE.strata'):
            if (ROOT / relative).is_file():
                paths.add(relative)
        require(paths, 'Whole source set is empty')
        return sorted(paths)

    def sources(self):
        rows = []
        require(git('rev-parse', 'HEAD').decode().strip() == self.head, 'Source HEAD changed')
        description_pin = {'path': '/workspace/astra-source/root-c31-source-restart-private03/SOURCE-AND-DEPENDENCIES.json',
                           'bytes': 2944, 'sha256': '2ddd5fbab293f08e0845554bf54a459f1a51364c6f70f5f56c0f0a2436fc770b'}
        description = read_bound(description_pin); self.immutable[description_pin['path']] = description_pin
        dependencies = {}
        for dependency in description['dependencies']:
            relative_root = dependency['path']
            require(relative_root in ('framework/CUI/vendor/Avalonia/external/XamlX',
                                      'framework/CUI/vendor/Avalonia/external/Avalonia.DBus'), 'Only the exact maintained dependency Git owners are admitted')
            owner = ROOT / relative_root
            link = git('ls-tree', self.head, '--', relative_root).decode().strip()
            require(link == '160000 commit ' + dependency['head'] + '\t' + relative_root,
                    'Exact canonical dependency gitlink differs')
            owner_head = subprocess.check_output(['git', '-C', str(owner), 'rev-parse', 'HEAD']).decode().strip()
            require(owner_head == dependency['head'] and not subprocess.check_output(['git', '-C', str(owner), 'diff', '--name-only']).strip()
                    and not subprocess.check_output(['git', '-C', str(owner), 'diff', '--cached', '--name-only']).strip(),
                    'Exact dependency owner HEAD or tracked source changed')
            bundle = {'path': str(Path(description_pin['path']).parent / dependency['bundle']),
                      'bytes': dependency['bytes'], 'sha256': dependency['sha256']}
            bound(bundle); self.immutable[bundle['path']] = bundle
            dependencies[relative_root] = (owner, dependency['head'], bundle)
        require(set(dependencies) == {'framework/CUI/vendor/Avalonia/external/XamlX',
                                      'framework/CUI/vendor/Avalonia/external/Avalonia.DBus'}, 'Both exact dependency source transports are required')
        for relative in self.source_paths:
            require(relative and not PurePosixPath(relative).is_absolute() and '..' not in PurePosixPath(relative).parts, 'Unsafe current source member')
            file_pin = pin(ROOT / relative); ownership = None
            for relative_root, (owner, owner_head, bundle) in dependencies.items():
                if relative.startswith(relative_root + '/'):
                    within = relative[len(relative_root) + 1:]
                    object_id = subprocess.check_output(['git', '-C', str(owner), 'rev-parse', owner_head + ':' + within]).decode().strip()
                    raw = subprocess.check_output(['git', '-C', str(owner), 'cat-file', 'blob', object_id])
                    ownership = {'kind': 'EXACT_RETAINED_DEPENDENCY_GIT_OWNER', 'relativeRoot': relative_root,
                                 'parentGitlink': owner_head, 'commit': owner_head, 'pathWithinOwner': within,
                                 'wholeSourceBundle': bundle, 'sourceDependencyDescription': description_pin}
                    break
            if ownership is None:
                object_id = git('rev-parse', self.head + ':' + relative).decode().strip()
                raw = git('cat-file', 'blob', object_id)
            require(re.fullmatch('[a-f0-9]{40}', object_id), 'Actual owning tracked Git blob required')
            require(len(raw) == file_pin['bytes'] and hashlib.sha256(raw).hexdigest() == file_pin['sha256'],
                    'Whole owning Git/physical current source differs: ' + relative)
            row = {'path': relative, 'bytes': file_pin['bytes'], 'sha256': file_pin['sha256'], 'gitBlob': object_id}
            if ownership is not None:
                row['gitOwner'] = ownership
            rows.append(row)
        return rows

    def decode_archive(self, row):
        if row.get('encoding') == self.lib.SCHEMA:
            bound(row['archive']); self.immutable[row['archive']['path']] = row['archive']
            manifest = self.lib.read_descriptor(row['archive']['path'])
            provenance = self.lib.manifest_input_provenance(manifest, self.layers)
            for descriptor in provenance['actualPhysicalInputs']:
                bound(descriptor); self.immutable[descriptor['path']] = descriptor
        else:
            _, transport = self.lib.read_original_gzip(row['archive']['path'], row['archive'], self.layers)
            descriptors = [transport['physical']] if transport['kind'] == 'PHYSICAL_EXACT_ORIGINAL_GZIP' else [transport['descriptor'], *transport['actualPhysicalInputs']]
            for descriptor in descriptors:
                bound(descriptor); self.immutable[descriptor['path']] = descriptor
        raw = self.lib.decode_archive(row, self.layers)
        require(len(raw) == row['original']['bytes'] and hashlib.sha256(raw).hexdigest() == row['original']['sha256'], 'Whole archive decoding differs')
        return raw

    def verify_desktop_closure(self):
        seen = set()
        for row in self.desktop['files']:
            name = self.sealer.safe_name(row['relative']); require(name not in seen, 'Duplicate current Desktop closure member'); seen.add(name)
            source = row['restoration']; original = row['original']
            if source['kind'] == 'EXACT_PACKAGE_NUPKG_MEMBER':
                container = source['container']; bound(container); self.immutable[container['path']] = container
                with zipfile.ZipFile(container['path']) as archive:
                    info = archive.getinfo(source['member'])
                    require(info.file_size == source['memberBytes'] == original['bytes'] and info.CRC == source['memberCRC32'], 'Whole current package member metadata differs')
                    value = hashlib.sha256(); count = 0
                    with archive.open(info) as stream:
                        for block in iter(lambda: stream.read(1048576), b''):
                            count += len(block); value.update(block)
                require(count == original['bytes'] and value.hexdigest() == source['memberSha256'] == original['sha256'], 'Whole current package native/managed member differs')
                self.package_provenance.setdefault(original['sha256'], []).append({'kind': 'EXACT_CURRENT_DESKTOP_PACKAGE_MEMBER', 'container': container, 'member': source['member']})
            else:
                require(source['kind'] == 'EXACT_COMPILER_CUSTODY_ARCHIVE', 'Unsupported complete current Desktop restoration source')
                raw = self.decode_archive(source['row'])
                require(len(raw) == original['bytes'] and hashlib.sha256(raw).hexdigest() == original['sha256'], 'Whole current compiler normal member differs')
                del raw
                self.package_provenance.setdefault(original['sha256'], []).append({'kind': 'EXACT_CURRENT_DESKTOP_COMPILER_ARCHIVE', 'row': source['row']})
        desktop_file = next(row for row in self.desktop['files'] if row['relative'] == 'Haven.dll')
        require({k: desktop_file['original'][k] for k in ('bytes', 'sha256')} == {k: self.current_outputs['Haven.Desktop']['target'][k] for k in ('bytes', 'sha256')}, 'Complete Desktop normal implementation differs from its actual fresh compiler product')

    def reconstruct(self):
        self.namespace = admission(self.output)
        (self.output / 'logs').mkdir(); (self.output / 'receipts').mkdir(); (self.output / 'publish').mkdir()
        for name in ('task-temp', 'cli-home', 'http-cache', 'owner-metadata', 'owner-artifacts'):
            (SCOPE / name).mkdir()
        self.verify_desktop_closure()
        final = read_bound(self.parent['finalCustody'], True)
        rows = self.parent['outputArchives'] + final['archives']
        private = {row['path']: row for row in self.parent['inputs'] + self.parent['generatedInputPins']
                   if Path(row['path']).is_relative_to(SCOPE / 'artifacts') or Path(row['path']).is_relative_to(SCOPE / 'metadata')}
        for product in self.current_outputs.values():
            for kind in PROTECTED_KINDS:
                if product.get(kind) and Path(product[kind]['path']).is_relative_to(SCOPE):
                    private[product[kind]['path']] = product[kind]
        props = self.parent['generatedOwningLayoutSuccessor']['actualProps']
        private[props['path']] = props
        index = {}
        for row in rows + [self.parent['generatedOwningLayoutSuccessor']['actualPropsCustody']]:
            for path in row['originalPaths']:
                if path in private and all(private[path][key] == row['original'][key] for key in ('bytes', 'sha256')):
                    index[path] = row
        require(private.keys() <= index.keys(), 'Actual current private input has no complete original archive donor: ' + repr(sorted(private.keys() - index.keys())))
        require(sum(row['bytes'] for row in private.values()) <= RECONSTRUCTION_LIMIT, 'Whole current compiler reconstruction exceeds the conserved raw allowance')
        restored_alias_classes = {}
        for path in sorted(private):
            destination = Path(path); require(destination.resolve(strict=False) == destination and not destination.exists(), 'Fresh logical current input required')
            raw = self.decode_archive(index[path]); require(len(raw) == private[path]['bytes'] and hashlib.sha256(raw).hexdigest() == private[path]['sha256'], 'Current donor whole bytes differ')
            destination.parent.mkdir(parents=True, exist_ok=True)
            alias_key = (index[path]['device'], index[path]['inode'], private[path]['bytes'], private[path]['sha256'])
            alias_first = restored_alias_classes.get(alias_key)
            if alias_first is None:
                with destination.open('xb') as stream:
                    stream.write(raw); stream.flush(); os.fsync(stream.fileno())
                os.chmod(destination, index[path]['mode'] & 0o7777); os.utime(destination, ns=(index[path]['mtimeNs'], index[path]['mtimeNs']))
                restored_alias_classes[alias_key] = destination
            else:
                require(destination.parent.stat().st_dev == alias_first.stat().st_dev, 'Original alias class requires one new private filesystem')
                os.link(alias_first, destination)
                require(destination.stat().st_ino == alias_first.stat().st_ino, 'Original whole alias relation not restored')
            require(pin(destination) == private[path], 'Actual restored compiler input differs')
            self.restored.append({'actualRestored': pin(destination), 'completeCurrentArchiveDonor': index[path],
                                  'historicalPhysicalIdentityReused': False, 'newPrivateDevice': destination.stat().st_dev,
                                  'newPrivateInode': destination.stat().st_ino, 'newAliasFirstPath': str(alias_first or destination),
                                  'originalAliasRelationConservedInNewPrivateLifetime': True})
            del raw
        self.private_before = list(private.values())
        self.materialize_shadow()
        config = SCOPE / 'NuGet.Config'
        raw = b'<configuration><packageSources><clear /></packageSources><packageSourceMapping><clear /></packageSourceMapping><auditSources><clear /></auditSources></configuration>'
        expected = next(row for row in self.parent['inputs'] if row['path'] == str(config))
        require(len(raw) == expected['bytes'] and hashlib.sha256(raw).hexdigest() == expected['sha256'], 'Exact cache-only inherited NuGet configuration differs')
        with config.open('xb') as stream:
            stream.write(raw)
        self.immutable[str(config)] = pin(config)
        self.make_owner_layout(Path(props['path']))
        self.env = os.environ.copy()
        self.env.update(DOTNET_ROOT=str(SDK.parent), DOTNET_CLI_HOME=str(SCOPE / 'cli-home'), NUGET_PACKAGES=str(CACHE),
                        DOTNET_NOLOGO='1', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', MSBUILDDISABLENODEREUSE='1',
                        TMPDIR=str(SCOPE / 'task-temp'), NUGET_HTTP_CACHE_PATH=str(SCOPE / 'http-cache'), DOTNET_GCHeapHardLimit=HEAP_LIMIT)
        stage = next(row for row in self.parent['stages'] if row['stage'] == 'Haven.Desktop-evaluation01')
        command = stage['command']; require(command[:3] == [str(SDK), 'msbuild', str(SHADOW / '9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj')], 'Original current Desktop evaluation argv differs')
        self.current_flags = [arg for arg in command[3:] if not arg.startswith(('-getProperty:', '-getItem:'))]
        require('-p:BuildProjectReferences=false' in self.current_flags and '-p:TargetFramework=net10.0' in self.current_flags
                and '-p:UseSharedCompilation=false' in self.current_flags and '-m:1' in self.current_flags and '-nr:false' in self.current_flags, 'Original current evaluation process/ref guards missing')
        self.check_protected()
        self.observe('after-complete-current-reconstruction')

    def materialize_shadow(self):
        xml = {raw.decode() for raw in git('ls-files', '-z').split(b'\0') if raw and Path(raw.decode()).suffix in ('.csproj', '.proj', '.props', '.targets')}
        selected = {row['target'] for row in self.parent['sourceRows']}
        require(set(row['target'] for row in read_bound(PACKET_PIN)['proposalFiles']) <= selected, 'Controlling current compiler source rows omit integrated14-path packet')
        required = set()
        for relative in xml | selected:
            path = PurePosixPath(relative).parent
            while str(path) != '.':
                required.add(path.as_posix()); path = path.parent
        SHADOW.mkdir()
        def materialize(relative):
            source = ROOT / relative; destination = SHADOW / relative
            for child in sorted(source.iterdir()):
                name = (PurePosixPath(relative) / child.name).as_posix(); target = destination / child.name
                if name in xml or name in selected:
                    require(child.is_file() and not child.is_symlink(), 'Physical controlling XML/source required')
                    target.write_bytes(child.read_bytes())
                    self.immutable[str(target)] = pin(target)
                elif child.is_dir() and name in required:
                    target.mkdir(); materialize(name)
                else:
                    target.symlink_to(child, target_is_directory=child.is_dir())
        materialize('')
        for module, plan in self.plans.items():
            require(pin(plan['project']['path']) == plan['project'] and pin(plan['canonicalProject']['path']) == plan['canonicalProject'], 'Current project whole bytes differ: ' + module)

    def make_owner_layout(self, original_props):
        props = SCOPE / 'WritePresent63OwnerOnly.props'; targets = SCOPE / 'WritePresent63OwnerOnly.targets'
        root = ET.Element('Project'); ET.SubElement(root, 'Import', Project=str(original_props))
        group = ET.SubElement(root, 'PropertyGroup', Condition="'$(MSBuildProjectName)' == 'HavenOS.Write' Or '$(MSBuildProjectName)' == 'HavenOS.Present'")
        values = {'ArtifactsPath': str(SCOPE / 'owner-artifacts'), 'ArtifactsProjectName': '$(MSBuildProjectName)-write-present63',
                  'MSBuildProjectExtensionsPath': str(SCOPE / 'owner-metadata') + '/$(MSBuildProjectName)/',
                  'ProjectAssetsFile': str(SCOPE / 'owner-metadata') + '/$(MSBuildProjectName)/project.assets.json',
                  'RestoreRecursive': 'false', 'PublishDir': '$(RootNormalOutputDirectory)'}
        for key, value in values.items():
            ET.SubElement(group, key).text = value
        ET.indent(root); ET.ElementTree(root).write(props, encoding='unicode')
        root = ET.Element('Project'); group = ET.SubElement(root, 'PropertyGroup')
        ET.SubElement(group, '_WritePresent63OriginalTargets').text = "$([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildProjectDirectory)', 'Directory.Build.targets'))"
        ET.SubElement(root, 'Import', Project='$(_WritePresent63OriginalTargets)/Directory.Build.targets', Condition="'$(_WritePresent63OriginalTargets)' != ''")
        group = ET.SubElement(root, 'ItemGroup', Condition="'$(MSBuildProjectName)' == 'HavenOS.Write' Or '$(MSBuildProjectName)' == 'HavenOS.Present'")
        update = ET.SubElement(group, 'ProjectReference', Update='@(ProjectReference)')
        ET.SubElement(update, 'GlobalPropertiesToRemove').text = '%(ProjectReference.GlobalPropertiesToRemove);RuntimeIdentifier;SelfContained;PublishDir'
        ET.indent(root); ET.ElementTree(root).write(targets, encoding='unicode')
        self.owner_props = props; self.owner_targets = targets
        self.immutable[str(props)] = pin(props); self.immutable[str(targets)] = pin(targets)

    def check_protected(self):
        require(git('rev-parse', 'HEAD').decode().strip() == self.head and not tracked_changes(), 'Current source revision/tracked bytes changed')
        require(self.sources() == self.sources_before, 'Whole current source/Git rows changed')
        for row in self.private_before:
            require(pin(row['path']) == row, 'Protected current dependency metadata/compiler output/ref changed: ' + row['path'])
        for row in self.immutable.values():
            bound(row)

    def observe(self, label):
        tree = physical_tree(self.output)
        evidence = tree['totalAllocatedBytes'] - physical_tree(self.output / 'publish')['totalAllocatedBytes']
        owners = physical_tree(SCOPE / 'owner-metadata')['totalAllocatedBytes'] + physical_tree(SCOPE / 'owner-artifacts')['totalAllocatedBytes']
        require(free(ROOT) >= DISK_SPARE, 'Actual workspace final spare was consumed')
        require(tree['totalAllocatedBytes'] <= DISK_ADMISSION - DISK_SPARE, 'Actual complete output/evidence peak exceeded the conserved disk envelope')
        require(evidence <= EVIDENCE_LIMIT and owners <= OWNER_RESIDENT_LIMIT, 'Whole evidence/new owning metadata exceeds declared reserve')
        item = {'label': label, 'workspaceAvailableBytes': free(ROOT), 'wholeUniqueOutputAllocatedBytes': tree['totalAllocatedBytes'],
                'wholeOutputLogicalBytes': tree['logicalBytes'], 'durableEvidenceAllocatedBytes': evidence, 'newOwnerTmpfsAllocatedBytes': owners,
                'actualMemoryCurrent': int(Path('/sys/fs/cgroup/memory.current').read_text()), 'atUTCUnixSeconds': time.time()}
        self.capacity.append(item); return item

    def stage(self, name, argv):
        self.check_protected(); self.observe(name + '-before')
        log = self.output / 'logs' / (name + '.log'); child = None; stream = None; primary = None; cleanup = []; natural = None; start = time.time()
        try:
            stream = log.open('xb')
            child = subprocess.Popen(argv, cwd=SHADOW, env=self.env, stdout=stream, stderr=subprocess.STDOUT)
            print('WRITE_PRESENT63_STAGE_STARTED', name, child.pid, flush=True)
        except BaseException as cause:
            primary = cause
        finally:
            # An acquired child is joined even if parent-side publication fails.
            # The child writes its whole merged stream directly to the owned file.
            # There is no bounded rendering, pipe truncation, timeout or forced stop.
            if child is not None:
                wait_attempt = 0
                while natural is None:
                    wait_attempt += 1
                    try:
                        natural = child.wait()
                    except BaseException as cause:
                        cleanup.append({'operation': 'natural-child-wait', 'attempt': wait_attempt, 'cause': repr(cause)})
                        # A first wait fault is a preserved pipeline failure, not
                        # permission to leave the already-owned healthy child.
                        # Keep joining naturally with no timeout or forced stop.
                        try:
                            terminal = child.poll()
                            if terminal is not None:
                                natural = terminal
                        except BaseException as poll_failure:
                            cleanup.append({'operation': 'natural-child-poll', 'attempt': wait_attempt, 'cause': repr(poll_failure)})
            if stream is not None:
                for operation, action in (('log-flush', stream.flush), ('log-fsync', lambda: os.fsync(stream.fileno())), ('log-close', stream.close)):
                    try:
                        action()
                    except BaseException as cause:
                        cleanup.append({'operation': operation, 'cause': repr(cause)})
        row = {'stage': name, 'argv': argv, 'pid': child.pid if child else None, 'naturalExitCode': natural,
               'forcedStop': False, 'wholeExternalLog': pin(log) if log.is_file() else None,
               'wholeStdoutCaptureComplete': child is not None and natural is not None and primary is None and not cleanup,
               'primaryFailure': repr(primary) if primary is not None else None, 'independentCleanupFailures': cleanup,
               'elapsedSeconds': time.time() - start, 'sdkHeapHardLimit': HEAP_LIMIT, 'nodeReuseDisabled': True}
        self.stages.append(row)
        exclusive_json(self.output / 'receipts' / (name + '-stage.json'), row)
        print('WRITE_PRESENT63_STAGE_TERMINAL', name, natural, flush=True)
        require(primary is None and not cleanup and natural == 0, 'Actual SDK stage failed; natural status/full log/cause preserved: ' + name)
        require(row['wholeExternalLog']['bytes'] <= LOG_LIMIT, 'Whole SDK log exceeds reserved diagnostic evidence budget; no stream was truncated')
        self.check_protected(); self.observe(name + '-after')
        return log

    def verify_current_desktop(self):
        plan = self.plans['Haven.Desktop']
        original = next(row for row in self.parent['stages'] if row['stage'] == 'Haven.Desktop-evaluation01')['command']
        log = self.stage('current-Desktop-unchanged-evaluation', original)
        evaluation = json.loads(log.read_bytes())
        require(evaluation['Properties'] == plan['properties'], 'Actual current Desktop compiler properties changed')
        current = []
        for kind, items in evaluation['Items'].items():
            for item in items:
                path = Path(item.get('FullPath', item['Identity']))
                if path.is_file():
                    require(not path.is_symlink() or path.resolve().is_relative_to(ROOT), 'Current compiler source symlink resolves outside source root')
                    current.append((kind, str(path), path.stat().st_size, sha_file(path)))
                else:
                    require(kind == 'PackageReference', 'Actual current Desktop physical source/resource/project item missing')
        expected = [(row['kind'], row['logical']['path'], row['logical']['bytes'], row['logical']['sha256']) for row in plan['physicalItems']]
        require(current == expected, 'Actual whole ordered current Desktop source/resource membership changed')
        references = [str(SDK), 'msbuild', str(SHADOW / '9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj'), *self.current_flags,
                      '-t:ResolveReferences;FindReferenceAssembliesForReferences', '-getItem:ReferencePath,ReferencePathWithRefAssemblies,Analyzer', '-v:quiet']
        actual = json.loads(self.stage('current-Desktop-unchanged-references', references).read_bytes())
        rows = []
        for kind, items in actual['Items'].items():
            for item in items:
                path = Path(item.get('FullPath', item['Identity'])); require(path.is_file(), 'Current reference missing')
                rows.append((kind, str(path), path.stat().st_size, sha_file(path)))
        old = [(row['kind'], row['input']['path'], row['input']['bytes'], row['input']['sha256']) for row in plan['actualReferencePins']]
        require(rows == old, 'Whole ordered current Desktop references/analyzers changed')

    def owner_flags(self, app, destination):
        flags = [arg for arg in self.current_flags if not arg.startswith(('-p:DirectoryBuildPropsPath=', '-p:TargetFramework='))]
        flags += ['-p:DirectoryBuildPropsPath=' + str(self.owner_props), '-p:DirectoryBuildTargetsPath=' + str(self.owner_targets),
                  '-p:TargetFramework=net10.0', '-p:RootNormalOutputModule=HavenOS.' + app,
                  '-p:RootNormalOutputDirectory=' + str(destination) + '/', '-p:RuntimeFrameworkVersion=10.0.12']
        return flags

    def owner_evaluation(self, app, project, flags):
        names = 'TargetPath,TargetRefPath,IntermediateOutputPath,ProjectAssetsFile,AssemblyName,TargetFramework,OutputType,OutDir,OutputPath,PublishDir,RuntimeIdentifier,SelfContained,BuildProjectReferences,UseSharedCompilation,RestoreRecursive,MSBuildAllProjects'
        log = self.stage(app + '-actual-owner-evaluation', [str(SDK), 'msbuild', str(project), *flags, '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
                         '-getProperty:' + names, '-getItem:Compile,EmbeddedResource,AvaloniaResource,AvaloniaXaml,AdditionalFiles,ProjectReference,PackageReference,Analyzer', '-v:quiet'])
        value = json.loads(log.read_bytes()); props = value['Properties']
        destination = self.output / 'publish' / app
        require(props['AssemblyName'] == 'HavenOS.' + app and props['TargetFramework'] == 'net10.0'
                and props['OutputType'] == 'Exe' and props['RuntimeIdentifier'] == 'win-x64'
                and props['SelfContained'].lower() == 'true' and props['BuildProjectReferences'].lower() == 'false'
                and props['UseSharedCompilation'].lower() == 'false' and props['RestoreRecursive'].lower() == 'false', 'Actual new owner properties differ')
        require(Path(props['TargetPath']) == destination / ('HavenOS.' + app + '.dll')
                and Path(props['OutDir']) == Path(props['OutputPath']) == Path(props['PublishDir']) == destination,
                'Owner must build/publish into the SAME real-disk directory to avoid repeated normal trees')
        require(Path(props['ProjectAssetsFile']).is_relative_to(SCOPE / 'owner-metadata')
                and Path(props['IntermediateOutputPath']).is_relative_to(SCOPE / 'owner-artifacts'), 'Owner generated metadata/intermediates escaped the fresh private scope')
        asset = pin(Path(props['ProjectAssetsFile'])); body = json.loads(Path(asset['path']).read_bytes())
        require(body['project']['restore']['projectPath'] == str(project) and 'net10.0/win-x64' in body['targets'], 'Genuine current owner win-x64 assets absent')
        require({str(Path(folder)) for folder in body['packageFolders']} == {str(CACHE)}, 'Owner restore uses a different/unreviewed package cache')
        source_rows = []
        for kind, items in value['Items'].items():
            for item in items:
                path = Path(item.get('FullPath', item['Identity']))
                if path.is_file():
                    relative = path.relative_to(SHADOW).as_posix() if path.is_relative_to(SHADOW) else path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else None
                    require(relative in self.source_paths or path.is_relative_to(CACHE) or path.is_relative_to(SDK.parent), 'App evaluated a new unbound physical source/resource input: ' + str(path))
                    logical = {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha_file(path)}
                    if relative is not None:
                        expected = self.source_row_index[relative]
                        require(logical['bytes'] == expected['bytes'] and logical['sha256'] == expected['sha256'],
                                'Actual copied app source/resource whole bytes differ from its original current Git/physical source row: ' + relative)
                    if not path.is_symlink():
                        self.immutable[str(path)] = logical
                    source_rows.append({'kind': kind, 'logicalPath': str(path), 'bytes': logical['bytes'], 'sha256': logical['sha256']})
                else:
                    require(kind == 'PackageReference', 'App physical source/resource/project input missing: ' + str(path))
        actual_projects = {Path(item.get('FullPath', item['Identity'])).relative_to(SHADOW).as_posix() for item in value['Items']['ProjectReference']}
        expected_projects = {'9to1 Workspace/shared/src/Haven.Application/Haven.Application.csproj', '9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj', '9to1 Workspace/shared/src/Haven.Infrastructure/Haven.Infrastructure.csproj'}
        require(actual_projects == expected_projects, 'Actual app direct owning graph changed')
        for item in value['Items']['ProjectReference']:
            require({'RuntimeIdentifier', 'SelfContained', 'PublishDir'} <= set(item['GlobalPropertiesToRemove'].split(';')), 'Owner reference must preserve original dependency output/RID identity')
        return value, asset, source_rows

    def owner_references(self, app, project, flags):
        log = self.stage(app + '-actual-owner-references', [str(SDK), 'msbuild', str(project), *flags, '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
                         '-t:ResolveReferences;FindReferenceAssembliesForReferences', '-getItem:ReferencePath,ReferencePathWithRefAssemblies,Analyzer,ResolvedRuntimePack,ResolvedAppHostPack', '-v:quiet'])
        value = json.loads(log.read_bytes()); rows = []; found = set()
        packs = []
        for kind in ('ResolvedRuntimePack', 'ResolvedAppHostPack'):
            for item in value['Items'][kind]:
                directory = Path(item['PackageDirectory'])
                require(directory.resolve() == directory and directory.is_relative_to(CACHE), 'Actual Windows runtime/host pack escaped the cache')
                packs.append({'kind': kind, 'packageDirectory': str(directory), 'actualSdkMetadata': item})
        require(any(row['kind'] == 'ResolvedRuntimePack' and Path(row['packageDirectory']) == CACHE / 'microsoft.netcore.app.runtime.win-x64/10.0.12' for row in packs)
                and any(row['kind'] == 'ResolvedAppHostPack' and Path(row['packageDirectory']) == CACHE / 'microsoft.netcore.app.host.win-x64/10.0.12' for row in packs),
                'Genuine SDK-selected Windows runtime AND apphost10.0.12 packs required')
        self.owner_packs[app] = packs
        known = {}
        for plan in self.plans.values():
            for item in plan['actualReferencePins']:
                known[(item['input']['path'], item['input']['bytes'], item['input']['sha256'])] = item['input']
        for name, output in self.current_outputs.items():
            for kind in PROTECTED_KINDS:
                if output.get(kind):
                    item = output[kind]; known[(item['path'], item['bytes'], item['sha256'])] = item
        for kind, items in value['Items'].items():
            if kind not in ('ReferencePath', 'ReferencePathWithRefAssemblies', 'Analyzer'):
                continue
            for item in items:
                path = Path(item.get('FullPath', item['Identity'])); descriptor = {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha_file(path)}
                if not path.is_relative_to(CACHE) and not path.is_relative_to(SDK.parent):
                    require((str(path), descriptor['bytes'], descriptor['sha256']) in known, 'Actual app reference does not match a complete genuine current dependency/ref: ' + str(path))
                for name, output in self.current_outputs.items():
                    if any(output.get(key) == descriptor for key in PROTECTED_KINDS):
                        found.add(name)
                rows.append({'kind': kind, 'input': descriptor, 'metadata': item})
                self.immutable[str(path)] = descriptor
        require({'Haven.Desktop', 'Haven.Application', 'Haven.Infrastructure'} <= found, 'Actual current owning app direct references were not selected')
        require(any(row['kind'] == 'ReferencePathWithRefAssemblies' and row['input'] == self.current_outputs['Haven.Desktop']['reference'] for row in rows), 'Csc must consume the exact genuine current Desktop reference assembly')
        return rows

    def collect_packages(self, app, asset):
        body = json.loads(Path(asset['path']).read_bytes()); rows = []
        packages = {library['path']: name for name, library in body['libraries'].items() if library['type'] == 'package'}
        for pack in self.owner_packs[app]:
            relative = Path(pack['packageDirectory']).relative_to(CACHE)
            require(len(relative.parts) == 2, 'SDK-selected package path must identify one exact ID/version')
            packages[relative.as_posix()] = '/'.join(relative.parts)
        for relative, name in sorted(packages.items()):
            identity, version = name.rsplit('/', 1); directory = CACHE / relative; container = directory / (identity.lower() + '.' + version.lower() + '.nupkg')
            require(directory.resolve() == directory and directory.is_relative_to(CACHE), 'Package escaped the cache')
            descriptor = pin(container); self.immutable[str(container)] = descriptor
            checksum = pin(directory / (container.name + '.sha512')); self.immutable[checksum['path']] = checksum
            nuspecs = sorted(directory.glob('*.nuspec')); require(len(nuspecs) == 1, 'Actual package license specification ambiguous')
            specification = pin(nuspecs[0]); self.immutable[specification['path']] = specification
            notices = []
            for path in sorted(directory.rglob('*')):
                if path.is_file() and not path.is_symlink() and re.search(r'(^|[._-])(license|notice|copying)([._-]|$)', path.name, re.I):
                    descriptor_notice = pin(path); notices.append(descriptor_notice); self.immutable[str(path)] = descriptor_notice
            rows.append({'package': name, 'wholeContainer': descriptor, 'checksum': checksum, 'nuspec': specification, 'actualLicenseNoticeFiles': notices})
        require(rows, 'No genuine restored package inventory')
        exclusive_json(self.output / 'receipts' / (app + '-actual-restored-package-license-inputs.json'), rows)
        return rows

    def coalesce_equal_publication_members(self, app, rows):
        if app == APPS[0]:
            return []
        first = self.output / 'publish' / APPS[0]; current = self.output / 'publish' / app; links = []
        original = {row['path']: row for row in self.first_publish_rows}
        for row in rows:
            if row['path'] not in original:
                continue
            require(row == original[row['path']], 'Two publications share a filename with different whole bytes: ' + row['path'])
            source = first / row['path']; destination = current / row['path']
            require(source.stat().st_dev == destination.stat().st_dev, 'Shared publication members must remain on the same real filesystem')
            before = {'device': destination.stat().st_dev, 'inode': destination.stat().st_ino, 'bytes': destination.stat().st_size, 'sha256': sha_file(destination)}
            temporary = destination.with_name(destination.name + '.write-present63-equal-hardlink')
            require(not temporary.exists(), 'Fresh coalescing temporary required')
            os.link(source, temporary); os.replace(temporary, destination)
            require(destination.stat().st_ino == source.stat().st_ino and sha_file(destination) == row['sha256'], 'Whole source-equal hardlink failed')
            links.append({'member': row, 'previousSecondPublicationFile': before, 'actualSharedDevice': source.stat().st_dev, 'actualSharedInode': source.stat().st_ino, 'sameCurrentPublicationBytesOnly': True})
        require(self.sealer.inventory(first) == self.first_publish_rows and self.sealer.inventory(current) == rows, 'Original whole publication membership/bytes changed during shared-file coalescing')
        return links

    def run_owner(self, app):
        destination = self.output / 'publish' / app; destination.mkdir(mode=0o700)
        project = SHADOW / ('9to1 Workspace/' + app + '/HavenOS.' + app + '.csproj')
        require(project.is_file(), 'Real owning app project absent')
        flags = self.owner_flags(app, destination)
        restore = [str(SDK), 'restore', str(project), *flags, '-r', 'win-x64', '-p:SelfContained=true', '-p:RestoreRecursive=false', '--configfile', str(SCOPE / 'NuGet.Config'), '--disable-parallel', '-v:normal']
        self.stage(app + '-actual-cache-only-owner-restore', restore)
        evaluation, assets, sources = self.owner_evaluation(app, project, flags)
        references = self.owner_references(app, project, flags)
        packages = self.collect_packages(app, assets)
        build = [str(SDK), 'build', str(project), *flags, '--no-restore', '-c', 'Debug', '-r', 'win-x64', '--self-contained', 'true', '-v:normal']
        build_log = self.stage(app + '-actual-fresh-owner-build', build)
        body = build_log.read_text(errors='replace')
        require(re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)', body), 'Actual fresh new owning Csc command absent')
        observed_csc_refs = []
        for match in re.finditer(r'/reference:(?:"([^"]+)"|([^\s]+))', body):
            for name in (match.group(1) or match.group(2)).split(','):
                path = Path(name); require(path.is_file(), 'Actual Csc referenced a missing input')
                observed_csc_refs.append({'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha_file(path)})
        expected_csc_refs = [row['input'] for row in references if row['kind'] == 'ReferencePathWithRefAssemblies']
        require(observed_csc_refs and {canonical(row) for row in observed_csc_refs} == {canonical(row) for row in expected_csc_refs},
                'Observed actual new-owner Csc references differ from the whole genuine SDK resolved reference set')
        require(self.current_outputs['Haven.Desktop']['reference'] in observed_csc_refs,
                'Observed actual new-owner Csc did not consume the genuine current Desktop reference')
        assembly = destination / ('HavenOS.' + app + '.dll'); require(assembly.is_file(), 'Actual fresh standalone app assembly absent')
        fresh_assembly = pin(assembly)
        publish = [str(SDK), 'publish', str(project), *flags, '--no-build', '--no-restore', '-c', 'Debug', '-r', 'win-x64', '--self-contained', 'true', '-o', str(destination), '-v:normal']
        publish_log = self.stage(app + '-actual-owner-publish', publish)
        require(pin(assembly) == fresh_assembly and pin(Path(assets['path'])) == assets, 'Published current app compiler/assets bytes changed')
        rows = self.sealer.inventory(destination)
        published_current_products = []
        published_names = {row['path'] for row in rows}
        for module, product in self.current_outputs.items():
            name = Path(product['target']['path']).name
            if name not in published_names:
                continue
            actual = pin(destination / name)
            require(all(actual[key] == product['target'][key] for key in ('bytes', 'sha256')),
                    'Published implementation is not the genuine current compiler target: ' + module)
            published_current_products.append({'module': module, 'actualPublished': actual, 'genuineCurrentCompilerTarget': product['target'],
                                               'compiledSourceHead': product['compiledSourceHead'], 'wholeImplementationEqual': True})
        require({'Haven.Desktop', 'Haven.Core', 'Haven.Application', 'Haven.Infrastructure'} <= {row['module'] for row in published_current_products},
                'Published genuine current Desktop and direct owning implementations are incomplete')
        source_after = self.sources(); require(source_after == self.sources_before, 'Whole source changed during actual owning build/publication')
        receipt = {'status': 'ACTUAL_ORDINARY_SDK_PUBLISH_COMPLETE', 'sourceCommit': self.head, 'sourceCommitAfter': self.head,
                   'sourceBindingSha256': self.source_binding, 'sdkVersion': '10.0.401', 'targetFramework': 'net10.0',
                   'runtimeIdentifier': 'win-x64', 'selfContained': True, 'configuration': 'Debug', 'naturalExitCode': 0, 'forcedStop': False,
                   'wholeStdoutCaptureComplete': True, 'sourceInputsBefore': self.sources_before, 'sourceInputsAfter': source_after,
                   'changedSourceInputs': [], 'trackedChangesAfter': tracked_changes(), 'argv': publish,
                   'wholeExternalLog': pin(publish_log), 'publishRoot': str(destination), 'publishFiles': rows,
                   'fileCount': len(rows), 'totalBytes': sum(row['bytes'] for row in rows), 'owner': app,
                   'actualCurrentCompilerReceipt': self.binding['currentCompilerReceipt'], 'actualCurrentDesktopClosure': self.binding['currentDesktopClosure'],
                   'actualFreshOwnerBuildStage': next(row for row in self.stages if row['stage'] == app + '-actual-fresh-owner-build'),
                   'actualRestoreStage': next(row for row in self.stages if row['stage'] == app + '-actual-cache-only-owner-restore'),
                   'actualEvaluatedOwner': evaluation, 'wholeActualSourceResourceItems': sources, 'wholeActualReferenceAnalyzerPins': references,
                   'actualProjectAssets': assets, 'actualRestoredPackageLicenseInputs': packages, 'actualSdkResolvedWindowsRuntimeAndApphostPacks': self.owner_packs[app],
                   'freshCompilerProduct': fresh_assembly, 'publishedCurrentDependencyCompilerProductReadback': published_current_products, 'actualCscReferencePins': observed_csc_refs, 'wholeResolvedAndObservedCscReferenceSetEqual': True, 'dependencyCompilerOutputBeforeAfterEqual': True,
                   'existing22OwnerSetUnmodified': True, 'runtimeAcceptanceClaimed': False, 'signingClaimed': False, 'cloudAuthenticationClaimed': False}
        receipt_pin = exclusive_json(self.output / 'receipts' / (app + '-ACTUAL-ORDINARY-SDK-PUBLISH63.json'), receipt)
        spec = {'app': app, 'receipt': receipt_pin, 'externalLog': pin(publish_log)}
        # Replay the maintained sealer's complete closure/PE/runtime/source guards
        # now, before acknowledging this as an SDK publication.
        self.sealer.verify_publication(spec, self.head, self.source_binding)
        self.receipts.append(spec)
        links = self.coalesce_equal_publication_members(app, rows)
        if app == APPS[0]:
            self.first_publish_rows = rows
        exclusive_json(self.output / 'receipts' / (app + '-EXACT-POSTPUBLICATION-SHARED-FILE-CUSTODY63.json'), {'publication': spec, 'equalByteCoalescing': links, 'wholePublicationFilesAfter': self.sealer.inventory(destination)})
        self.check_protected(); self.observe(app + '-actual-publication-complete')

    def run(self):
        self.reconstruct()
        log = self.stage('actual-sdk-version', [str(SDK), '--version'])
        require(log.read_text().strip() == '10.0.401', 'Actual SDK selected a different version')
        self.verify_current_desktop()
        for app in APPS:
            self.run_owner(app)
        self.check_protected()
        settled_process_scan = process_admission()
        terminal = {'status': 'ACTUAL_CURRENT_WRITE_PRESENT_WINDOWS_SDK_PUBLICATIONS_COMPLETE_SEAL_AND_PLATFORM_RECEIVING_PENDING',
                    'sourceCommit': self.head, 'inputBinding': self.input_pin, 'driver': pin(Path(__file__).absolute()),
                    'namespaceAdmission': self.namespace, 'wholeCurrentDependencyArchiveRestoration': self.restored,
                    'protectedCurrentDependencyMetadataCompilerAndRefPinsBefore': self.private_before,
                    'protectedCurrentDependencyMetadataCompilerAndRefPinsAfter': [pin(row['path']) for row in self.private_before],
                    'sourceInputsBefore': self.sources_before, 'sourceInputsAfter': self.sources(), 'sourceBindingSha256': self.source_binding,
                    'actualStages': self.stages, 'actualPublications': self.receipts, 'actualCapacityObservations': self.capacity, 'terminalSdkModelConsoleProcessScan': settled_process_scan,
                    'qualifiedDependencyCompilerBasis': self.production_basis,
                    'currentDesktopWholeClosureCheckedWithoutRecreatingLargeNormalTree': True,
                    'oneNormalBuildPublishDirectoryPerOwner': True, 'byteEqualCommonPublicationFilesSharePhysicalInodes': True,
                    'actualWindowsLaunchExecuted': False, 'actualSigningExecuted': False, 'testsExecuted': False,
                    'modelsExecuted': False, 'providerCredentialsIncluded': False, 'runtimeAcceptanceClaimed': False,
                    'licenseDistributionReviewAndCurrentCorrespondingSourceCompanionRequired': True}
        terminal_pin = exclusive_json(self.output / 'WRITE-PRESENT63-ACTUAL-SDK-PUBLICATIONS-TERMINAL.json', terminal)
        draft = {'status': 'EXACT_WINDOWS_SEAL_INPUT_DRAFT_REQUIRES_CURRENT_REVIEWED_LICENSE_COMPANION',
                 'sourceCommit': self.head, 'sourceBindingSha256': self.source_binding, 'runtimeAcceptanceClaimed': False,
                 'publications': self.receipts, 'licensingCompanionReceipt': None, 'actualSdkTerminalReceipt': terminal_pin,
                 'requiredCompanionStatus': 'REVIEWED_EXACT_DISTRIBUTION_LICENSE_COMPANION',
                 'sourceLicensePins': [row for row in self.sources_before if re.search(r'(LICENSE|NOTICE|COPYING)', row['path'], re.I)],
                 'knownReceivingLimits': ['Windows PE/runtime closure was inspected on Linux; Windows launch, native audio/graphics/read-aloud and persistence smoke remain unexecuted.',
                                         'No signing, installed product, account login, provider model credential, official Cloudflare MCP OAuth or inference success is implied.',
                                         'Root must review exact published native/managed binary hashes against actual package/source notices and supply the current corresponding-source companion before distribution.']}
        draft_pin = exclusive_json(self.output / 'WRITE-PRESENT63-SEAL-INPUT-DRAFT.json', draft)
        witness_path = self.output / 'WRITE-PRESENT63-ACTUAL-COMPLETE-FINAL-CAPACITY-WITNESS.json'
        # Reserve the entire small final witness BEFORE its actual whole-tree
        # observation. Filling this same allocation cannot escape that sample.
        with witness_path.open('xb') as stream:
            stream.write(b' ' * 4096); stream.flush(); os.fsync(stream.fileno())
        reserved_stat = witness_path.stat()
        actual_final = self.observe('after-whole-terminal-draft-and-reserved-final-capacity-witness')
        witness = {'status': 'ACTUAL_COMPLETE_WRITE_PRESENT63_FINAL_PUBLICATION_CAPACITY_CHECK_PASS',
                   'actualSdkTerminalReceipt': terminal_pin, 'sealInputDraft': draft_pin, 'actualCapacity': actual_final,
                   'finalWitnessReservedBytes': 4096, 'forcedStop': False, 'runtimeAcceptanceClaimed': False}
        raw = json.dumps(witness, indent=2).encode() + b'\n'
        require(len(raw) <= 4096, 'Final whole capacity witness exceeds its exact reserved allocation')
        with witness_path.open('r+b') as stream:
            stream.write(raw + b' ' * (4096 - len(raw))); stream.flush(); os.fsync(stream.fileno())
        actual_stat = witness_path.stat()
        require((actual_stat.st_dev, actual_stat.st_ino, actual_stat.st_size, actual_stat.st_blocks) ==
                (reserved_stat.st_dev, reserved_stat.st_ino, reserved_stat.st_size, reserved_stat.st_blocks)
                and json.loads(witness_path.read_bytes()) == witness, 'Final capacity witness changed its reserved physical allocation/whole bytes')
        require(free(ROOT) >= DISK_SPARE, 'Actual workspace spare after complete final witness publication was consumed')
        self.check_protected()
        print(json.dumps({'actualSdkTerminalReceipt': terminal_pin, 'sealInputDraft': draft_pin, 'actualFinalCapacityWitness': pin(witness_path)}), flush=True)

    def preserve_failure(self, cause):
        if not self.output.is_dir():
            return
        value = {'status': 'ACTUAL_WRITE_PRESENT63_RECEIVING_FAILURE_PRESERVED_NO_PUBLICATION_OR_READINESS_PROMOTION',
                 'sourceCommit': self.head, 'inputBinding': self.input_pin, 'primaryException': repr(cause),
                 'actualStages': self.stages, 'completedActualPublications': self.receipts, 'actualCapacityObservations': self.capacity,
                 'wholeTaskOwnedOutputAndPartialFileCustody': physical_tree(self.output),
                 'compilerOrPublisherFailureNotRelabelledNatural0': True, 'runtimeAcceptanceClaimed': False,
                 'anyAcquiredSdkChildNaturallyWaited': all(row['naturalExitCode'] is not None for row in self.stages if row['pid'] is not None),
                 'noTimeoutOrForcedStopIssued': True}
        exclusive_json(self.output / 'WRITE-PRESENT63-ACTUAL-FAILURE-PRESERVED.json', value)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input-bind', type=Path, required=True)
    parser.add_argument('--input-bind-sha256', required=True)
    arguments = parser.parse_args()
    require(re.fullmatch('[a-f0-9]{64}', arguments.input_bind_sha256), 'Independent whole binding SHA required')
    input_pin = pin(arguments.input_bind.absolute()); require(input_pin['sha256'] == arguments.input_bind_sha256, 'Whole Root receiving binding differs')
    binding = read_bound(input_pin); driver = None
    try:
        driver = Driver(binding, input_pin); driver.run()
        bound(input_pin)
    except BaseException as primary:
        if driver is not None:
            try:
                driver.preserve_failure(primary)
            except BaseException as publication:
                print(json.dumps({'rawPrimaryFailure': repr(primary), 'independentFailureCustodyPublicationFailure': repr(publication)}), file=sys.stderr, flush=True)
        raise


if __name__ == '__main__':
    main()
