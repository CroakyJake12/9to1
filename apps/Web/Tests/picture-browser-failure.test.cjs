// Controlled error classification only; no browser/storage/provider/engine acceptance.
// This file belongs at apps/Web/Tests/picture-browser-failure.test.cjs.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const sourcePath = path.resolve(__dirname, '../Media/picture-browser.js');
const source = fs.readFileSync(sourcePath, 'utf8');
// Evaluate current production declarations. The VM has no browser listeners or storage APIs;
// private producer/classifier exposure exists only inside this controlled test context.
const declarations = source.replace(/^export (?=(?:async )?function )/gm, '');
const actual = vm.runInNewContext(declarations + '\n({ ownFault: fault, classify: error => JSON.stringify(browserFailure(error)) })',
    Object.create(null), { filename: sourcePath });
const classify = actual.classify;
const ownFault = actual.ownFault;
const privateMessage = 'PRIVATE_NATIVE_PATH_OR_TOKEN';
function check(name, body) { test(name, body); }
function equal(actual, expected) { assert.deepEqual(actual, expected); }
function receipt(error, code, message) {
    const result = JSON.parse(classify(error));
    equal(Object.keys(result).sort(), ['code', 'message', 'ok']);
    equal(result.ok, false); equal(typeof result.code, 'string'); equal(result.code, code);
    if (message !== undefined) equal(result.message, message);
    else { assert.equal(typeof result.message, 'string'); assert.ok(!result.message.includes(privateMessage)); }
    return result;
}
for (const [name, code] of [['QuotaExceededError', 'StorageFull'], ['SecurityError', 'PermissionDenied'],
    ['NotAllowedError', 'PermissionDenied'], ['UnknownError', 'StorageFailed']]) {
    check('native-' + name, () => receipt(new DOMException(privateMessage, name), code));
}
for (const code of ['CapabilityUnavailable', 'StorageBlocked', 'InvalidArgument', 'CapacityExceeded',
    'RevisionConflict', 'StorageFailed', 'OperationBusy', 'OperationCancelled', 'DocumentNotFound']) {
    check('own-string-' + code, () => receipt(ownFault(code, 'documented own message'), code, 'documented own message'));
}
check('unknown-string-does-not-authorize-private-message', () => receipt({ code: 'Unrecognized', message: privateMessage }, 'StorageFailed'));
check('null-error-falls-back', () => receipt(null, 'StorageFailed'));
check('numeric-code-zero-uses-native-name', () => receipt({ code: 0, name: 'NotAllowedError', message: privateMessage }, 'PermissionDenied'));
check('numeric-code22-uses-native-name', () => receipt({ code: 22, name: 'QuotaExceededError', message: privateMessage }, 'StorageFull'));
check('throwing-code-getter-falls-back', () => receipt({ get code() { throw new Error(privateMessage); } }, 'StorageFailed'));
check('code-getter-is-read-once', () => {
    let reads = 0;
    const error = ownFault('RevisionConflict', 'documented own message');
    Object.defineProperty(error, 'code', { get() { return ['RevisionConflict', 'RevisionConflict', 22][reads++]; } });
    receipt(error, 'RevisionConflict', 'documented own message');
    equal(reads, 1);
});
check('code-getter-cannot-return-late-private-string', () => {
    let reads = 0;
    const error = ownFault('RevisionConflict', 'documented own message');
    Object.defineProperty(error, 'code', { get() { return ['RevisionConflict', 'RevisionConflict', privateMessage][reads++]; } });
    receipt(error, 'RevisionConflict', 'documented own message');
    equal(reads, 1);
});
check('name-getter-is-read-once', () => {
    let reads = 0;
    receipt({ code: 0, get name() { reads++; return 'QuotaExceededError'; }, message: privateMessage }, 'StorageFull');
    equal(reads, 1);
});
check('native-message-getter-not-read', () => {
    receipt({ code: 22, name: 'QuotaExceededError', get message() { throw new Error(privateMessage); } }, 'StorageFull');
});
check('own-message-getter-is-read-once', () => {
    let reads = 0;
    const error = ownFault('OperationCancelled', 'documented own message');
    Object.defineProperty(error, 'message', { get() { reads++; return 'documented own message'; } });
    receipt(error, 'OperationCancelled', 'documented own message');
    equal(reads, 1);
});
check('throwing-own-message-falls-back', () => {
    const error = ownFault('OperationCancelled', 'documented own message');
    Object.defineProperty(error, 'message', { get() { throw new Error(privateMessage); } });
    receipt(error, 'StorageFailed');
});
check('nonstring-own-message-uses-safe-fallback', () => receipt(Object.assign(ownFault('RevisionConflict', 'documented own message'), { message: { secret: privateMessage } }), 'RevisionConflict'));
for (const code of ['CapabilityUnavailable', 'StorageBlocked', 'InvalidArgument', 'CapacityExceeded',
    'RevisionConflict', 'StorageFailed', 'OperationBusy', 'OperationCancelled', 'DocumentNotFound']) {
    check('spoofed-known-code-' + code, () => receipt({ code, message: privateMessage }, 'StorageFailed'));
}
check('unbranded-known-code-message-getter-not-read', () => {
    let reads = 0;
    receipt({ code: 'RevisionConflict', get message() { reads++; throw new Error(privateMessage); } }, 'StorageFailed');
    equal(reads, 0);
});
check('unbranded-known-code-keeps-native-name-mapping', () => {
    receipt({ code: 'RevisionConflict', name: 'QuotaExceededError', message: privateMessage }, 'StorageFull');
});
check('own-fault-prototype-does-not-transfer-brand', () => {
    const producerError = ownFault('RevisionConflict', privateMessage);
    receipt(Object.create(producerError), 'StorageFailed');
});
check('unbranded-changing-known-code-reads-once-without-message', () => {
    let reads = 0;
    receipt({ get code() { return ['RevisionConflict', privateMessage][reads++]; }, message: privateMessage }, 'StorageFailed');
    equal(reads, 1);
});
