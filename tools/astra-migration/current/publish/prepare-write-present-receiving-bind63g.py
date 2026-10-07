#!/usr/bin/env python3
"""Create exclusive Root receiving bindings from real pinned receipts. No SDK runs."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re
import subprocess

BASE = Path('/workspace/astra-source/friday-write-present-sol61u62')
ROOT = Path('/workspace/astra-consolidated')
DRIVER = BASE / 'driver63/run-write-present-current-windows-publish63i.py'
DRIVER_SHA = 'a142f436e405495ddfaa389a6d2703d00b50c6bf22f14193a2e1a014b7a4aad5'
COMPILER = Path('/workspace/astra-source/a4-current-targeted-owning-compiler66/output/CURRENT-TARGETED-OWNING-COMPILER66-RECEIPT.json.gz')
SEALER = BASE / 'package/seal-write-present-win62.py'
PACKET = BASE / 'WRITE-PRESENT62C-EXACT-SOURCE-CANDIDATE-AND-PROGRESSIVE-PACKAGE-HANDOFF01.json'
SDK = Path('/workspace/astra-tools/dotnet-10.0.401/dotnet')


def require(value, message):
    if not value:
        raise RuntimeError(message)


def pin(path):
    path = Path(path).absolute()
    require(path.is_file() and not path.is_symlink(), 'Regular absolute whole file required')
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1048576), b''):
            value.update(block)
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': value.hexdigest()}


def read_exact(path, expected_sha, zipped=False):
    require(re.fullmatch('[a-f0-9]{64}', expected_sha), 'Independent whole input SHA required')
    descriptor = pin(path); require(descriptor['sha256'] == expected_sha, 'Independent whole input binding differs')
    raw = Path(descriptor['path']).read_bytes()
    return descriptor, json.loads(gzip.decompress(raw) if zipped else raw)


def exact_record(descriptor, zipped=False):
    require(set(descriptor) == {'path', 'bytes', 'sha256'} and pin(descriptor['path']) == descriptor, 'Whole receipt-derived binding differs')
    raw = Path(descriptor['path']).read_bytes()
    return json.loads(gzip.decompress(raw) if zipped else raw)


def write_fresh(path, value):
    path = Path(path).absolute()
    require(path.is_relative_to(BASE) and path.resolve(strict=False) == path and path.parent.is_dir(), 'Fresh isolated receiving file below this lane required')
    with path.open('x') as stream:
        json.dump(value, stream, indent=2); stream.write('\n')
    print(json.dumps(pin(path)), flush=True)


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


def verify_whole_descriptor(row):
    require(pin(row['path']) == row, 'Whole production basis source/proof/receipt differs')


def build_binding(arguments):
    receipt_pin, receipt = read_exact(arguments.compiler_receipt, arguments.compiler_receipt_sha256, True)
    head = arguments.expected_commit
    require(re.fullmatch('[a-f0-9]{40}', head), 'Independent complete current source revision required')
    current = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
    require(current == head == receipt['headAtCapture'] == receipt['headAtCompletion'], 'Actual compiled/current receiving head differs')
    require((arguments.qualified_desktop_component is None) == (arguments.qualified_desktop_component_sha256 is None),
            'Qualified Desktop component path and independent SHA must be supplied together')
    qualified_component_pin = None
    if arguments.qualified_desktop_component is not None:
        qualified_component_pin, unused_component = read_exact(arguments.qualified_desktop_component, arguments.qualified_desktop_component_sha256)
    production_basis = qualify_current_production_basis(receipt, head, exact_record, verify_whole_descriptor, qualified_component_pin)
    driver_pin = pin(DRIVER); require(driver_pin['sha256'] == DRIVER_SHA and driver_pin['bytes'] == 85082, 'Immutable qualified driver63i differs')
    peer_pin, peer = read_exact(arguments.source_peer, arguments.source_peer_sha256)
    require(peer['status'] == 'QUALIFIED_INDEPENDENT_SOURCE_PASS_WRITE_PRESENT_WINDOWS_DRIVER63_RUNTIME_UNEXECUTED'
            and driver_pin in peer['qualifiedFiles'], 'Exact independent source receiving peer required')
    desktop_pin = receipt['actualDesktopRuntimeClosureManifest']; desktop = exact_record(desktop_pin, True)
    require(desktop['status'] == 'ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS' and desktop['head'] == head
            and desktop['actualRuntimeStage']['exitCode'] == 0 and desktop['ticket'] == receipt['ticket'], 'Genuine current complete Desktop closure required')
    compiler_source = receipt['exactSelectiveCompilerSource']; exact_record_source = pin(compiler_source['path'])
    require(exact_record_source == compiler_source, 'The actual current compiler source whole bytes differ')
    value = {'status': 'ROOT_SCHEDULED_CURRENT_WRITE_PRESENT_WINDOWS_BUILD_PUBLISH', 'sourceCommit': head,
             'currentCompilerReceipt': receipt_pin, 'currentDesktopClosure': desktop_pin,
             'currentCompilerSource': compiler_source, 'driver': driver_pin, 'sourcePeer': peer_pin,
             'sdk': pin(SDK), 'sourcePacket': pin(PACKET), 'sealer': pin(SEALER),
             'outputRoot': str(arguments.output_root.absolute()), 'runtimeAcceptanceClaimed': False,
             'previous62FailureRetainedNotUsedAsSuccessfulBasis': True,
             'configuration': 'Debug', 'runtimeIdentifier': 'win-x64', 'selfContained': True,
             'compilerNewAppOwnerSetNotInventedIntoExisting22': True, 'qualifiedDependencyCompilerBasis': production_basis,
             'qualifiedDesktopNormalComponent': qualified_component_pin}
    write_fresh(arguments.output, value)


def seal_binding(arguments):
    terminal_pin, terminal = read_exact(arguments.actual_sdk_terminal, arguments.actual_sdk_terminal_sha256)
    require(terminal['status'] == 'ACTUAL_CURRENT_WRITE_PRESENT_WINDOWS_SDK_PUBLICATIONS_COMPLETE_SEAL_AND_PLATFORM_RECEIVING_PENDING',
            'Actual completed current app SDK receiving receipt required')
    require(not (Path(terminal_pin['path']).parent / 'WRITE-PRESENT63-ACTUAL-FAILURE-PRESERVED.json').exists(),
            'Actual driver failure history cannot be success-sealed')
    capacity_pin, capacity = read_exact(arguments.final_capacity_witness, arguments.final_capacity_witness_sha256)
    require(capacity['status'] == 'ACTUAL_COMPLETE_WRITE_PRESENT63_FINAL_PUBLICATION_CAPACITY_CHECK_PASS'
            and capacity['actualSdkTerminalReceipt'] == terminal_pin
            and Path(capacity_pin['path']).parent == Path(terminal_pin['path']).parent,
            'Whole final terminal/draft/capacity publication witness required')
    require(capacity['actualCapacity']['workspaceAvailableBytes'] >= 134217728
            and capacity['actualCapacity']['wholeUniqueOutputAllocatedBytes'] <= 1218122064 - 134217728
            and capacity['actualCapacity']['durableEvidenceAllocatedBytes'] <= 35559951 + 67108864,
            'Actual final disk/evidence/spare allocation exceeded conserved limits')
    head = terminal['sourceCommit']
    require(head == arguments.expected_commit and re.fullmatch('[a-f0-9]{40}', head), 'Exact compiled publication revision required')
    require(terminal['sourceInputsBefore'] == terminal['sourceInputsAfter']
            and terminal['protectedCurrentDependencyMetadataCompilerAndRefPinsBefore'] == terminal['protectedCurrentDependencyMetadataCompilerAndRefPinsAfter'],
            'Actual whole source/dependency before-after comparison failed')
    require(len(terminal['actualPublications']) == 2 and {row['app'] for row in terminal['actualPublications']} == {'Write', 'Present'}, 'Exactly two genuine app publications required')
    for row in terminal['actualPublications']:
        receipt = exact_record(row['receipt']); exact_record_log = pin(row['externalLog']['path'])
        require(exact_record_log == row['externalLog'] and receipt['sourceCommit'] == receipt['sourceCommitAfter'] == head
                and receipt['status'] == 'ACTUAL_ORDINARY_SDK_PUBLISH_COMPLETE' and receipt['naturalExitCode'] == 0
                and receipt['sourceBindingSha256'] == terminal['sourceBindingSha256'], 'Actual app receipt/log chain differs')
    notices_pin, notices = read_exact(arguments.licensing_companion_receipt, arguments.licensing_companion_receipt_sha256)
    require(notices['status'] == 'REVIEWED_EXACT_DISTRIBUTION_LICENSE_COMPANION' and notices['sourceCommit'] == head,
            'Actual current reviewed binary notice/source licensing companion required')
    source_pin, source = read_exact(arguments.corresponding_source_receipt, arguments.corresponding_source_receipt_sha256)
    require(source['status'] == 'EXACT_PRIVATE_CURRENT_CORRESPONDING_SOURCE_HANDOFF' and source['sourceCommit'] == head,
            'Current private corresponding-source handoff required; a historical C45 source delivery alone is insufficient')
    require(source['coveredSourceInputs'] == terminal['sourceInputsBefore'] and source['completeCurrentSourceBuildAndLicenseFilesIncluded'] is True,
            'Private corresponding-source delivery must cover the actual publication source rows/build/license inputs')
    require(source['modelWeightsOrCredentialsIncluded'] is False, 'Source delivery must exclude private model/credentials')
    for row in source['wholeTransportPins']:
        require(pin(row['path']) == row, 'Corresponding-source whole transport bytes differ')
    value = {'sourceCommit': head, 'sourceBindingSha256': terminal['sourceBindingSha256'], 'runtimeAcceptanceClaimed': False,
             'publications': terminal['actualPublications'], 'licensingCompanionReceipt': notices_pin,
             'currentCorrespondingSourceCompanionReceipt': source_pin, 'actualSdkTerminalReceipt': terminal_pin, 'actualFinalCapacityWitness': capacity_pin,
             'actualWindowsLaunchExecuted': False, 'signingClaimed': False, 'deploymentClaimed': False}
    write_fresh(arguments.output, value)


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest='command', required=True)
    build = commands.add_parser('build')
    build.add_argument('--compiler-receipt', type=Path, default=COMPILER)
    build.add_argument('--compiler-receipt-sha256', required=True)
    build.add_argument('--qualified-desktop-component', type=Path)
    build.add_argument('--qualified-desktop-component-sha256')
    build.add_argument('--source-peer', type=Path, required=True)
    build.add_argument('--source-peer-sha256', required=True)
    build.add_argument('--expected-commit', required=True)
    build.add_argument('--output-root', type=Path, required=True)
    build.add_argument('--output', type=Path, required=True)
    seal = commands.add_parser('seal')
    seal.add_argument('--actual-sdk-terminal', type=Path, required=True)
    seal.add_argument('--actual-sdk-terminal-sha256', required=True)
    seal.add_argument('--final-capacity-witness', type=Path, required=True)
    seal.add_argument('--final-capacity-witness-sha256', required=True)
    seal.add_argument('--licensing-companion-receipt', type=Path, required=True)
    seal.add_argument('--licensing-companion-receipt-sha256', required=True)
    seal.add_argument('--corresponding-source-receipt', type=Path, required=True)
    seal.add_argument('--corresponding-source-receipt-sha256', required=True)
    seal.add_argument('--expected-commit', required=True)
    seal.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    build_binding(arguments) if arguments.command == 'build' else seal_binding(arguments)


if __name__ == '__main__':
    main()
