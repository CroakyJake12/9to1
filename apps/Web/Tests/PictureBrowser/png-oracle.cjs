// Independent test-only PNG byte/pixel oracle. Never imported by the application.
const assert = require('node:assert/strict');
const zlib = require('node:zlib');

function crc(bytes) {
    let value = 0xffffffff;
    for (const byte of bytes) {
        value ^= byte;
        for (let bit = 0; bit < 8; bit++) value = (value >>> 1) ^ ((value & 1) ? 0xedb88320 : 0);
    }
    return (value ^ 0xffffffff) >>> 0;
}

module.exports = function decode(bytes) {
    assert(Buffer.isBuffer(bytes) && bytes.length <= 8 * 1024 * 1024);
    assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
    let width, height, channels, offset = 8, ended = false;
    const compressed = [], chunks = [];
    while (offset < bytes.length) {
        assert(!ended && bytes.length - offset >= 12, 'Complete PNG boundaries; no trailing data');
        const length = bytes.readUInt32BE(offset);
        assert(length <= bytes.length - offset - 12, 'Complete PNG chunk payload');
        const type = bytes.toString('ascii', offset + 4, offset + 8);
        const data = bytes.subarray(offset + 8, offset + 8 + length);
        assert.equal(crc(bytes.subarray(offset + 4, offset + 8 + length)), bytes.readUInt32BE(offset + 8 + length), 'PNG CRC ' + type);
        assert(chunks.length || type === 'IHDR');
        if (type === 'IHDR') {
            assert.equal(chunks.length, 0); assert.equal(length, 13);
            width = data.readUInt32BE(0); height = data.readUInt32BE(4);
            assert(width > 0 && height > 0 && width * height <= 4_000_000);
            assert.equal(data[8], 8); assert.equal(data[10], 0); assert.equal(data[11], 0); assert.equal(data[12], 0);
            channels = ({ 0: 1, 2: 3, 4: 2, 6: 4 })[data[9]];
            assert(channels, 'Oracle covers the actual noninterlaced eight-bit fixture/export formats only');
        }
        if (type === 'IDAT') compressed.push(data);
        if (type === 'IEND') { assert.equal(length, 0); ended = true; }
        chunks.push(type); offset += length + 12;
    }
    assert(ended && compressed.length > 0 && width && height && offset === bytes.length);
    const stride = width * channels;
    const raw = zlib.inflateSync(Buffer.concat(compressed), { maxOutputLength: height * (stride + 1) });
    assert.equal(raw.length, height * (stride + 1));
    const rows = [], rgba = Buffer.alloc(width * height * 4);
    const paeth = (a, b, c) => {
        const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    };
    for (let y = 0; y < height; y++) {
        const filter = raw[y * (stride + 1)]; assert(filter <= 4);
        const row = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
        for (let x = 0; x < stride; x++) {
            const a = x >= channels ? row[x - channels] : 0, b = y ? rows[y - 1][x] : 0;
            const c = y && x >= channels ? rows[y - 1][x - channels] : 0;
            row[x] = (row[x] + [0, a, b, Math.floor((a + b) / 2), paeth(a, b, c)][filter]) & 255;
        }
        rows.push(row);
        for (let x = 0; x < width; x++) {
            const start = x * channels, target = (y * width + x) * 4;
            const pixel = channels === 4 ? row.subarray(start, start + 4)
                : channels === 3 ? [...row.subarray(start, start + 3), 255]
                    : [row[start], row[start], row[start], channels === 2 ? row[start + 1] : 255];
            rgba.set(pixel, target);
        }
    }
    return { width, height, chunks, rgba };
};
