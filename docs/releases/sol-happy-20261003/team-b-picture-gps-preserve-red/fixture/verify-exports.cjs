// Run only after the real native engine harness; checks actual PNG bytes independently.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const decode = require('./decode-png.cjs');
const root = path.resolve(process.argv[2]); let assertions = 0;
const expected = (x, y) => [x * 30, y * 40, (x + y) * 15, x === 0 && y === 0 ? 0 : x === 1 && y === 1 ? 128 : 255];
const report = { scope: 'Independent actual exported PNG pixel check, NOT browser acceptance', status: 'NOT-RUN' };
try {
    const transformed = decode(fs.readFileSync(path.join(root, 'transformed.png')));
    assert.equal(transformed.width, 3); assert.equal(transformed.height, 5); assertions += 2;
    for (let y = 0; y < 5; y++) for (let x = 0; x < 3; x++) { assert.deepEqual(transformed.pixel(x, y), expected(1 + y, 2 + x)); assertions++; }
    const alpha = decode(fs.readFileSync(path.join(root, 'alpha-roundtrip.png')));
    assert.equal(alpha.width, 8); assert.equal(alpha.height, 6); assertions += 2;
    for (let y = 0; y < 6; y++) for (let x = 0; x < 8; x++) { assert.deepEqual(alpha.pixel(x, y), expected(x, y)); assertions++; }
    report.status = 'PASS';
} catch (error) { report.status = 'FAIL'; report.error = error.stack; process.exitCode = 1; }
finally { report.assertions = assertions; report.exit = process.exitCode || 0; fs.writeFileSync(path.join(root, 'independent-pixel-results.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report)); }
