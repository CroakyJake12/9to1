// Independent test decoder for exported 8-bit PNG fixtures. Not linked into application code.
const assert = require('node:assert/strict');
const zlib = require('node:zlib');
module.exports = function decode(bytes) {
    assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
    let width, height, channels, offset = 8; const compressed = [];
    while (offset + 12 <= bytes.length) {
        const length = bytes.readUInt32BE(offset); const type = bytes.toString('ascii', offset + 4, offset + 8);
        assert(offset + length + 12 <= bytes.length); const data = bytes.subarray(offset + 8, offset + 8 + length);
        if (type === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); assert.equal(data[8], 8); assert.equal(data[12], 0); channels = ({ 0: 1, 2: 3, 4: 2, 6: 4 })[data[9]]; assert(channels); }
        if (type === 'IDAT') compressed.push(data);
        offset += length + 12; if (type === 'IEND') break;
    }
    const raw = zlib.inflateSync(Buffer.concat(compressed)); const stride = width * channels;
    assert.equal(raw.length, height * (stride + 1)); const rows = [];
    const paeth = (a, b, c) => { const p = a + b - c; const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; };
    for (let y = 0; y < height; y++) {
        const filter = raw[y * (stride + 1)]; assert(filter <= 4); const row = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
        for (let x = 0; x < stride; x++) {
            const a = x >= channels ? row[x - channels] : 0, b = y > 0 ? rows[y - 1][x] : 0, c = y > 0 && x >= channels ? rows[y - 1][x - channels] : 0;
            row[x] = (row[x] + ([0, a, b, Math.floor((a + b) / 2), paeth(a, b, c)][filter])) & 255;
        }
        rows.push(row);
    }
    return { width, height, pixel(x, y) {
        const row = rows[y]; const start = x * channels;
        if (channels === 4) return [...row.subarray(start, start + 4)];
        if (channels === 3) return [...row.subarray(start, start + 3), 255];
        return [row[start], row[start], row[start], channels === 2 ? row[start + 1] : 255];
    } };
};
