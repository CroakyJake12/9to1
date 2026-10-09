"""Lossless byte custody only; no compiler/source/model or artifact trust assertion."""
import gzip, hashlib, json, os, pathlib, struct, sys, zlib, subprocess, re

SCHEMA = 'EXACT-RESOURCE-SUBRANGE-CUSTODY01'
BLOB_SCHEMA = 'EXACT-IMMUTABLE-GIT-BLOB-SUBRANGE-CUSTODY02'

def whole(data):
    return {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}

def checked(data, expected):
    assert whole(data) == {k: expected[k] for k in ('bytes', 'sha256')}
    return data

def pin(path):
    p = pathlib.Path(path)
    return {'path': str(p), **whole(p.read_bytes())}

def physical_gzip(row, closed_layers=None):
    encoded, _ = read_original_gzip(row['archive']['path'], row['archive'], closed_layers or {})
    return checked(gzip.decompress(encoded), row['original'])

def resource_blocks(anchor, closed_layers=None):
    raw = physical_gzip(anchor, closed_layers)
    index_end = 4 + struct.unpack_from('<i', raw, 0)[0]
    version, count = struct.unpack_from('<ii', raw, 4)
    assert version == 2 and 0 < count < 10000
    cursor = 12
    blocks = []
    names = set()
    for _ in range(count):
        size = shift = 0
        while True:
            byte = raw[cursor]; cursor += 1; size |= (byte & 127) << shift
            if not byte & 128:
                break
            shift += 7
            assert shift < 35
        name = raw[cursor:cursor + size].decode('utf-8'); cursor += size
        offset, length = struct.unpack_from('<ii', raw, cursor); cursor += 8
        assert name not in names and offset >= 0 and length >= 0 and index_end + offset + length <= len(raw)
        names.add(name)
        if length > 100000:
            body = raw[index_end + offset:index_end + offset + length]
            blocks.append({'name': name, 'anchorOffset': index_end + offset, **whole(body), 'body': body})
    assert cursor == index_end
    return blocks

def make_layer(raw, anchor, original_encoded=None, original_archive=None, original_metadata=None, closed_layers=None):
    """Returns in-memory literal gzip and full inverse. Does not mutate old custody."""
    matches = []
    for block in resource_blocks(anchor, closed_layers):
        position = raw.find(block['body'])
        if position >= 0:
            assert raw.find(block['body'], position + 1) == -1
            matches.append((position, block))
    matches.sort(key=lambda pair: pair[0])
    segments, literals, cursor, literal_cursor = [], [], 0, 0
    for position, block in matches:
        assert position >= cursor
        body = raw[cursor:position]
        if body:
            segments.append({'kind': 'literal', 'offset': literal_cursor, **whole(body)})
            literals.append(body); literal_cursor += len(body)
        segments.append({'kind': 'resource', 'offset': block['anchorOffset'], 'name': block['name'], 'bytes': block['bytes'], 'sha256': block['sha256']})
        cursor = position + block['bytes']
    body = raw[cursor:]
    if body:
        segments.append({'kind': 'literal', 'offset': literal_cursor, **whole(body)})
        literals.append(body)
    literal_raw = b''.join(literals)
    encoded = gzip.compress(raw, compresslevel=9, mtime=0)
    if original_encoded is not None:
        assert encoded == original_encoded, 'Original encoded gzip must roundtrip bit-for-bit before retirement.'
    manifest = {'schema': SCHEMA, 'original': whole(raw), 'originalEncodedGzip': whole(encoded),
        'segments': segments, 'literalOriginal': whole(literal_raw), 'resourceAnchor': anchor,
        'compression': {'algorithm': 'python-gzip', 'compresslevel': 9, 'mtime': 0,
            'python': sys.version, 'zlibCompileVersion': zlib.ZLIB_VERSION, 'zlibRuntimeVersion': zlib.ZLIB_RUNTIME_VERSION,
            'encodedHeaderHex': encoded[:10].hex()},
        'originalArchive': original_archive, 'originalPhysicalMetadata': original_metadata,
        'originalRawAndEncodedBitExactBeforeRetirement': original_encoded is not None,
        'oldPhysicalPathStillPresentAtPreparation': bool(original_archive and pathlib.Path(original_archive['path']).is_file()),
        'inverse': 'Verify each physical gzip anchor and literal whole encoded pin. Decode both, check every segment and reconstruct ordered full raw bytes; check raw SHA/length. gzip.compress(level=9,mtime=0) must match the FULL recorded encoded gzip SHA/length/header before restoring original archive path and recorded mode/mtime. Preserve original inode/device observations as historical only. No source/compile/failure/model status changes.'}
    return manifest, gzip.compress(literal_raw, compresslevel=9, mtime=0), encoded

def decode_layer(manifest, closed_layers=None):
    if manifest['schema'] == BLOB_SCHEMA:
        return decode_blob_layer(manifest)
    assert manifest['schema'] == SCHEMA
    literal = checked(gzip.decompress(checked(pathlib.Path(manifest['literalArchive']['path']).read_bytes(), manifest['literalArchive'])), manifest['literalOriginal'])
    resource = physical_gzip(manifest['resourceAnchor'], closed_layers)
    parts = []
    for segment in manifest['segments']:
        source = literal if segment['kind'] == 'literal' else resource
        parts.append(checked(source[segment['offset']:segment['offset'] + segment['bytes']], segment))
    return checked(b''.join(parts), manifest['original'])

def encoded_layer(manifest, closed_layers=None):
    raw = decode_layer(manifest, closed_layers)
    c = manifest['compression']
    assert c['algorithm'] == 'python-gzip' and c['compresslevel'] == 9 and c['mtime'] == 0
    encoded = checked(gzip.compress(raw, compresslevel=9, mtime=0), manifest['originalEncodedGzip'])
    assert encoded[:10].hex() == c['encodedHeaderHex']
    return encoded

def read_descriptor(path):
    return json.loads(pathlib.Path(path).read_bytes())

def read_original_gzip(path, expected, closed_layers):
    p = pathlib.Path(path)
    if p.is_file():
        return checked(p.read_bytes(), expected), {'kind': 'PHYSICAL_EXACT_ORIGINAL_GZIP', 'physical': pin(p)}
    descriptor = closed_layers[str(p)]
    checked(pathlib.Path(descriptor['path']).read_bytes(), descriptor)
    manifest = read_descriptor(descriptor['path'])
    assert manifest['originalArchive'] == expected
    encoded = checked(encoded_layer(manifest, closed_layers), expected)
    return encoded, {'kind': 'BITEXACT_RECONSTRUCTED_ORIGINAL_GZIP', 'originalPhysicalPathPresent': False,
        'originalLogicalPin': expected, 'descriptor': descriptor,
        **manifest_input_provenance(manifest, closed_layers)}

def decode_archive(row, closed_layers):
    if row.get('encoding') == SCHEMA:
        checked(pathlib.Path(row['archive']['path']).read_bytes(), row['archive'])
        manifest = read_descriptor(row['archive']['path'])
        return checked(decode_layer(manifest, closed_layers), row['original'])
    encoded, _ = read_original_gzip(row['archive']['path'], row['archive'], closed_layers)
    return checked(gzip.decompress(encoded), row['original'])

def restore_closed_archive(descriptor, closed_layers=None):
    manifest = read_descriptor(descriptor)
    raw = encoded_layer(manifest, closed_layers)
    original = pathlib.Path(manifest['originalArchive']['path'])
    assert not original.exists()
    original.parent.mkdir(parents=True, exist_ok=True)
    with original.open('xb') as f:
        f.write(raw); f.flush(); os.fsync(f.fileno())
    assert pin(original) == manifest['originalArchive']
    metadata = manifest['originalPhysicalMetadata']
    os.chmod(original, metadata['mode'])
    os.utime(original, ns=(metadata['atimeNs'], metadata['mtimeNs']))
    return pin(original)


def original_git_blob(repository, segment):
    """Immutable object identity, independent of HEAD/current working-tree bytes."""
    oid = segment['gitBlobObjectId']
    assert re.fullmatch(r'[0-9a-f]{40}', oid)
    kind = subprocess.check_output(['git', '-C', repository, 'cat-file', '-t', oid]).strip()
    assert kind == b'blob'
    raw = subprocess.check_output(['git', '-C', repository, 'cat-file', 'blob', oid])
    assert hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() == oid
    return checked(raw, segment)


def decode_blob_layer(manifest):
    assert manifest['schema'] == BLOB_SCHEMA
    literal = checked(gzip.decompress(checked(pathlib.Path(manifest['literalArchive']['path']).read_bytes(), manifest['literalArchive'])), manifest['literalOriginal'])
    parts = []
    for segment in manifest['segments']:
        if segment['kind'] == 'literal':
            part = checked(literal[segment['offset']:segment['offset'] + segment['bytes']], segment)
        else:
            assert segment['kind'] == 'immutableGitBlob'
            part = original_git_blob(manifest['gitRepository'], segment)
        parts.append(part)
    return checked(b''.join(parts), manifest['original'])


def manifest_input_provenance(manifest, closed_layers):
    physical = [manifest['literalArchive']]
    git_inputs = []
    if manifest['schema'] == BLOB_SCHEMA:
        for s in manifest['segments']:
            if s['kind'] == 'immutableGitBlob':
                git_inputs.append({'repository': manifest['gitRepository'], 'objectId': s['gitBlobObjectId'], 'bytes': s['bytes'], 'sha256': s['sha256'], 'transport': 'git-cat-file-blob; exact immutable blob bytes and object SHA1 verified; no working-tree dependence'})
    else:
        assert manifest['schema'] == SCHEMA
        _, transport = read_original_gzip(manifest['resourceAnchor']['archive']['path'], manifest['resourceAnchor']['archive'], closed_layers)
        if transport['kind'] == 'PHYSICAL_EXACT_ORIGINAL_GZIP':
            physical.append(transport['physical'])
        else:
            physical.append(transport['descriptor'])
            physical.extend(transport['actualPhysicalInputs'])
            git_inputs.extend(transport.get('immutableGitBlobInputs', []))
    for p in physical:
        checked(pathlib.Path(p['path']).read_bytes(), p)
    return {'actualPhysicalInputs': list({p['path']: p for p in physical}.values()), 'immutableGitBlobInputs': list({p['objectId']: p for p in git_inputs}.values())}
