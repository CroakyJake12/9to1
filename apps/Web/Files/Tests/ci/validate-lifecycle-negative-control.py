#!/usr/bin/env python3
"""Post-run negative-control validator. Never compiles or runs an owner/fixture."""
import argparse, hashlib, json, re
from pathlib import Path

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--suite',required=True,choices=('lifecycle-original01','lifecycle-original02'))
    parser.add_argument('--diagnostics',required=True,type=Path)
    parser.add_argument('--actual-driver-exit',required=True,type=int)
    parser.add_argument('--source-commit',required=True)
    parser.add_argument('--plan',required=True,type=Path)
    args=parser.parse_args()
    plan_bytes=args.plan.read_bytes()
    if hashlib.sha256(plan_bytes).hexdigest()!='8fab4bf3283e2f2f7342dec20f38a2dab1d46c9fa0aa3de4c29bcb76c842af2e':
        raise RuntimeError('Reviewed source07 control plan changed; do not silently widen expected defect mechanisms')
    plan=json.loads(plan_bytes)
    criteria=plan['allFourteenCases']; expected=plan['negativeExpectedFailedCases'][args.suite]
    result=json.loads((args.diagnostics/'result.json').read_text())
    commands=json.loads((args.diagnostics/'commands.json').read_text())
    native=[x for x in commands if x['name']=='native']
    if args.actual_driver_exit!=1 or result['status']!='FAIL' or result['error']!="RuntimeError('native exit 1')" or result.get('sourceCustodyError') or result.get('sourceCommit')!=args.source_commit:
        raise RuntimeError('Negative control must preserve actual native exit1; compilation/setup/custody failures are not expected-red evidence')
    if len(native)!=1 or native[0]['exit']!=1 or not native[0]['normalEOF'] or not native[0]['familyClosed'] or not native[0]['finalECHILD'] or native[0]['signals'] or native[0]['error']:
        raise RuntimeError('Actual native normal-family exit1 witness missing')
    for command in commands:
        if command['name']!='native' and command['exit']!=0 or not command['normalEOF'] or not command['familyClosed'] or not command['finalECHILD'] or command['signals'] or command['error']:
            raise RuntimeError('Ordinary prerequisite or owned command custody failed')
    text=(args.diagnostics/'native.log').read_text()
    passed=re.findall(r'^PASS (actual-[^\r\n]+)$',text,re.MULTILINE)
    failure_lines=re.findall(r'^FAIL (actual-[^:]+): ([^\r\n]*)$',text,re.MULTILINE)
    failed=[name for name,_ in failure_lines]
    if len(passed)+len(failed)!=14 or len(set(passed+failed))!=14 or set(passed+failed)!=set(criteria) or set(failed)!=set(expected):
        raise RuntimeError('Same14 case coverage or expected defect identity differs; preserve actual result instead of widening expected failures')
    expected_lines=plan['negativeExpectedFirstFailureLines'][args.suite]
    if len(failure_lines)!=len(expected_lines) or dict(failure_lines)!=expected_lines:
        raise RuntimeError('Predicted case failed at a different mechanism/assertion; setup/backend errors do not establish the intended regression')
    summary=f'RESULT expected=14 executed=14 passed={len(passed)} failed={len(failed)}; realOwnerGroupAuthority=BLOCKED browser=NOT_RUN'
    if len(re.findall('^'+re.escape(summary)+'$',text,re.MULTILINE))!=1:
        raise RuntimeError('Complete original14 summary/authority qualification missing')
    observed={'status':'EXPECTED_RED_CONTROL','sourceCommit':args.source_commit,'suite':args.suite,'criteriaExpected':14,'criteriaExecuted':14,'criteriaPassed':len(passed),'criteriaFailed':len(failed),'failedCases':failed,'intendedFirstFailureLinesMatched':True,'actualFirstFailureLines':dict(failure_lines),'actualNativeExit':1,'actualDriverExit':1,'notProductPass':True,'realOwnerGroupAuthority':'BLOCKED','browser':'NOT_RUN','nativeLogSHA256':hashlib.sha256((args.diagnostics/'native.log').read_bytes()).hexdigest()}
    (args.diagnostics/'negative-control-result.json').write_text(json.dumps(observed,indent=2)+'\n')
    print(json.dumps(observed,indent=2))
    return 0

if __name__=='__main__':
    raise SystemExit(main())
