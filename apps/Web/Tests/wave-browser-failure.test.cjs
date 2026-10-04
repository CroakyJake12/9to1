// Source-derived error boundary regression only; no browser, quota, Audio or provider acceptance.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { test, after } = require('node:test');

const sourcePath = path.resolve(__dirname, '../Media/wave-browser.js');
const sourceBytes = fs.readFileSync(sourcePath);
const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const sourceSHA256 = digest(sourceBytes);
const source = sourceBytes.toString('utf8');
const prefixEnd = source.indexOf('function openDatabase() {');
const catchMarker = '    } catch (error) {\n';
const catchStart = source.lastIndexOf(catchMarker);
const catchEnd = source.lastIndexOf('\n    }\n}');
assert(prefixEnd >= 0 && catchStart > prefixEnd && catchEnd > catchStart,
    'Actual module failure boundary and complete prefix must be present.');
assert.equal(source.slice(catchEnd).trim(), '}\n}');
const prefix = source.slice(0, prefixEnd);
const catchBody = source.slice(catchStart + catchMarker.length, catchEnd);
// Execute the exact actual source prefix and catch, without rewriting its classification algorithm.
const classify = new Function('error', prefix + '\n' + catchBody);
const fault = new Function('code', 'message', prefix + '\nreturn fault(code, message);');
const decode = error => JSON.parse(classify(error));

for (const [name, legacyCode, requiredCode] of [
    ['NotAllowedError', 0, 'PermissionDenied'],
    ['QuotaExceededError', 22, 'StorageFull'],
    ['SecurityError', 18, 'PermissionDenied'],
    ['NotSupportedError', 9, 'CodecUnsupported'],
]) {
    test('native DOMException ' + name + ' preserves typed code and privacy', () => {
        const error = new DOMException('private-native-detail', name);
        assert(error instanceof DOMException);
        assert.equal(error.code, legacyCode);
        const actual = decode(error);
        assert.equal(actual.ok, false);
        assert.equal(typeof actual.code, 'string');
        assert.equal(actual.code, requiredCode);
        assert.equal(typeof actual.message, 'string');
        assert(!JSON.stringify(actual).includes('private-native-detail'));
    });
}

test('all actual module fault strings and messages are preserved', () => {
    const codes = [...new Set([...source.matchAll(/fault\('([^']+)'/g)].map(match => match[1]))];
    assert(codes.length > 0, 'Actual fault call sites must be inspected.');
    for (const code of codes) {
        assert.deepEqual(decode(fault(code, 'owner-message')), { ok: false, code, message: 'owner-message' });
    }
});

test('unknown provider string code and message use private generic fallback', () => {
    const error = new Error('private-provider-detail');
    error.code = 'private-provider-code';
    const actual = decode(error);
    assert.equal(actual.ok, false);
    assert.equal(actual.code, 'StorageFailed');
    assert.equal(typeof actual.message, 'string');
    assert(!JSON.stringify(actual).includes('private-provider'));
});

test('opaque values and native abort yield a typed generic failure', () => {
    for (const error of [null, undefined, 'private-detail', 22, {},
        new Error('private-detail'), new DOMException('private-detail', 'AbortError')]) {
        const actual = decode(error);
        assert.equal(actual.ok, false);
        assert.equal(actual.code, 'StorageFailed');
        assert.equal(typeof actual.message, 'string');
        assert(!JSON.stringify(actual).includes('private-detail'));
    }
});

test('throwing code accessor does not throw or expose details', () => {
    const error = Object.defineProperty({}, 'code', { get() { throw new Error('private-getter-detail'); } });
    const actual = decode(error);
    assert.equal(actual.ok, false);
    assert.equal(actual.code, 'StorageFailed');
    assert(!JSON.stringify(actual).includes('private-getter-detail'));
});

for (const laterCode of [22, 'private-changing-code']) {
    test('changing code getter returns the single validated snapshot: ' + String(laterCode), () => {
        let reads = 0;
        const error = Object.defineProperty({ message: 'owner-message' }, 'code', {
            get() { return ['RevisionConflict', 'RevisionConflict', laterCode][Math.min(reads++, 2)]; },
        });
        assert.deepEqual(decode(error), { ok: false, code: 'RevisionConflict', message: 'owner-message' });
        assert.equal(reads, 1);
    });
}

test('changing native name getter is read once', () => {
    let reads = 0;
    const error = Object.defineProperty({}, 'name', {
        get() { return reads++ === 0 ? 'QuotaExceededError' : 'private-changing-name'; },
    });
    const actual = decode(error);
    assert.equal(actual.code, 'StorageFull');
    assert.equal(reads, 1);
    assert(!JSON.stringify(actual).includes('private-changing-name'));
});

test('changing owner message getter returns the same validated value', () => {
    let reads = 0;
    const error = Object.defineProperty({ code: 'RevisionConflict' }, 'message', {
        get() { return reads++ === 0 ? 'owner-message' : 22; },
    });
    assert.deepEqual(decode(error), { ok: false, code: 'RevisionConflict', message: 'owner-message' });
    assert.equal(reads, 1);
});

after(() => {
    assert.equal(digest(fs.readFileSync(sourcePath)), sourceSHA256, 'Actual source must remain unchanged during controls.');
});
