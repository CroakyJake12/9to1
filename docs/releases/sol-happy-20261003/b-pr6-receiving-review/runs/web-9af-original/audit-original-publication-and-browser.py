import hashlib
import io
import json
import pathlib
import re
import subprocess
import zipfile
from PIL import Image

r = pathlib.Path('/tmp/team-c-c5-web-9af-20261004')
raw = json.loads((r / 'browser-nine/raw/results.json').read_text())
diag = json.loads((r / 'browser-nine/diagnostics/result.json').read_text())
tuple_ = json.loads((r / 'verified-sealed-tuple.json').read_text())
src = tuple_['sourceCommit']
seal = json.loads((r / 'original-publication/seal.json').read_text())
manifest = json.loads((r / 'original-publication/publish-manifest.json').read_text())
assert raw['counts'] == diag['counts'] == {'discovered': 9, 'executed': 9, 'passed': 9, 'failed': 0, 'notRun': 0}
assert raw['exit'] == 0 and raw['acceptance'] == 'PASS_SCOPED_NINE'
assert len(raw['outcomes']) == 9 and all(v['state'] == 'PASS' for v in raw['outcomes'].values())
assert raw['candidate']['sourceCommit'] == diag['sourceCommit'] == src
assert raw['candidate']['manifestSHA256'] == seal['receiptSha256']
assert raw['candidate']['sourceBeforeSHA256'] == raw['candidate']['sourceAfterSHA256'] == tuple_['sourceCustody']['sha256']
assert raw['bundleInventoryBefore'] == raw['bundleInventoryAfter']
# Original producer and browser inventories use different ordering; compare exact path/body maps.
assert len(raw['bundleInventoryBefore']) == len({v['path'] for v in raw['bundleInventoryBefore']}) == 285
assert {v['path']: v for v in raw['bundleInventoryBefore']} == {v['path']: v for v in manifest['publishFiles']}
assert raw['runnerSHA256AtStart'] == raw['runnerSHA256AtEnd']
assert raw['oracleSHA256AtStart'] == raw['oracleSHA256AtEnd']
for p, key in [('apps/Web/Tests/PictureBrowser/run-picture-browser.cjs', 'runnerSHA256AtStart'), ('apps/Web/Tests/PictureBrowser/png-oracle.cjs', 'oracleSHA256AtStart')]:
    body = subprocess.check_output(['git', '-C', '/workspace/9to1', 'show', src + ':' + p])
    assert hashlib.sha256(body).hexdigest() == raw[key]
assert raw['contextClosed'] and raw['serverClosed'] and raw['portRebind']
assert len(raw['launches']) == len(raw['closedLaunches']) == 2
assert len({v['browserPID'] for v in raw['launches']}) == 2
assert all(v['contextCloseResolved'] for v in raw['closedLaunches'])
commands = []
for directory in ['publisher', 'browser-nine/diagnostics']:
    cc = json.loads((r / directory / 'commands.json').read_text())
    assert all(q['exit'] == 0 and q['normalEOF'] and q['finalECHILD'] and q['familyClosed'] and q['signals'] == [] and all(v['gone'] for v in q['births']) for q in cc)
    commands.extend(cc)
assert len(commands) == 26
traces = []
for p in [r / 'browser-nine/raw/picture-first-process.zip', r / 'browser-nine/raw/picture-final-process.zip']:
    with zipfile.ZipFile(p) as z:
        assert z.testzip() is None
        assert len(z.namelist()) == len(set(z.namelist()))
        traces.append({'path': str(p), 'bytes': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'members': len(z.infolist()), 'crc': 'PASS'})
actual = r / 'browser-nine/raw/actual-crop-remove-all.png'
# picture-original.png is the original UI screenshot, not the imported fixture.
fixture_bytes = subprocess.check_output(['git', '-C', '/workspace/9to1', 'show', src + ':apps/Web/Tests/PictureAdmission/Fixtures/Picture8x6.png'])
assert hashlib.sha256(fixture_bytes).hexdigest() == raw['fixture']['sha256']
assert len(fixture_bytes) == raw['fixture']['bytes']
original = Image.open(io.BytesIO(fixture_bytes)).convert('RGBA')
crop = Image.open(actual).convert('RGBA')
assert original.size == (8, 6) and crop.size == (5, 3)
assert crop.tobytes() == original.crop((1, 2, 6, 5)).tobytes()
png = raw['outcomes']['real-png-download-exact-pixels']['observed']
assert png['pixelsChecked'] == 15 and hashlib.sha256(crop.tobytes()).hexdigest() == png['rgbaSHA256']
assert hashlib.sha256(actual.read_bytes()).hexdigest() == png['actualPngSHA256']
warnings = {n: sorted(set(re.findall(r'warning (\w+)', (r / 'publisher' / n).read_text()))) for n in ['build.log', 'publish.log']}
summary = {
    'schemaVersion': 1, 'status': 'ACTUAL_ORDINARY_WEB_PUBLICATION_AND_ORIGINAL_BROWSER9_PASS_SCOPED',
    'sourceCommit': src, 'officialRun': 37227141927, 'officialJob': 111509039692,
    'tuple': tuple_['sealedTuple'], 'sourceCustody': tuple_['sourceCustody'], 'counts': raw['counts'],
    'originalGroups': list(raw['outcomes']), 'originalRunnerAndOracleWholeGitUnchanged': True,
    'bundleWholeInventoryBeforeAfterUnchanged': True, 'exactManifestPathMapsEqual': True,
    'currentCommands': {'normalClosedFamilies': len(commands), 'normalEOF': True, 'finalECHILD': True, 'allActualBirthsGone': True, 'signals': 0},
    'browser': {'pids': [q['browserPID'] for q in raw['launches']], 'twoClosedContexts': True, 'serverClosed': True, 'portRebind': True, 'actualVersion': diag['actualChromiumVersion'], 'traces': traces},
    'independentActualPngPixels': {'fixtureDimensions': [8, 6], 'actualCropDimensions': [5, 3], 'pixels': 15, 'bytes': 60, 'everyRGBAEqualToOriginalRealFixtureCrop': True, 'rgbaSha256': png['rgbaSHA256'], 'actualPngSha256': png['actualPngSHA256']},
    'retainedWarningCodes': warnings,
    'qualification': 'Actual original nine local Picture groups only. Authentication/native fetch/provider authority/GPS Preserve/AT/hardware/installed/deployment/full parity NOT_RUN; original negative receipts and prior source tuples unchanged. Browser original final resource bound is not deleted/transient peak/RSS proof. Whole official publication container exceeds connector cap; original payload verified by bounded producer parts.'
}
target = r / 'actual-browser-nine-and-publication-result.json'
target.write_text(json.dumps(summary, indent=2) + '\n')
print('PASS:', len(commands), 'normal closed families, original browser9, 15 exact actual crop pixels; warning codes:', warnings)
print('Receipt SHA256:', hashlib.sha256(target.read_bytes()).hexdigest())
