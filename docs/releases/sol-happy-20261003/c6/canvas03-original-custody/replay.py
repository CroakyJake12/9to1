"""Read-only original custody replay; never execute any archived payload."""
from pathlib import Path, PurePosixPath
import argparse, hashlib, json, stat, zipfile

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def safe(z):
    names = set()
    for f in z.infolist():
        assert not PurePosixPath(f.filename).is_absolute() and '\\' not in f.filename
        assert all(p not in ('', '.', '..') for p in f.filename.split('/'))
        assert not f.is_dir() and f.filename not in names
        names.add(f.filename)
        assert stat.S_IFMT(f.external_attr >> 16) not in (stat.S_IFLNK, stat.S_IFIFO, stat.S_IFSOCK)
        assert not f.flag_bits & 1 and f.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
    assert z.testzip() is None

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--custody', type=Path, required=True)
    root = parser.parse_args().custody
    here = Path(__file__).resolve().parent
    admission = json.loads((here / 'admission.json').read_text())
    raw = (root / 'index.zip').read_bytes()
    assert len(raw) == admission['indexBytes'] and sha(raw) == admission['indexSha256']
    with zipfile.ZipFile(root / 'index.zip') as z:
        safe(z)
        assert z.namelist() == ['manifest.json']
        raw = z.read('manifest.json')
    assert len(raw) == admission['manifestBytes'] and sha(raw) == admission['manifestSha256']
    manifest = json.loads(raw)
    zs = {}
    pieces = 0
    try:
        for part, seal in enumerate(admission['archives']):
            raw = (root / ('outer-' + str(part) + '.zip')).read_bytes()
            assert len(raw) == seal['outerBytes'] and sha(raw) == seal['outerSha256']
            with zipfile.ZipFile(root / ('outer-' + str(part) + '.zip')) as z:
                safe(z)
                assert z.namelist() == [manifest['bundles'][part]['file']]
                raw = z.read(z.namelist()[0])
            assert len(raw) == seal['innerBytes'] and sha(raw) == seal['innerSha256']
            inner = root / ('inner-' + str(part) + '.zip')
            assert inner.read_bytes() == raw
            zs[part] = zipfile.ZipFile(inner)
            safe(zs[part])
            assert set(zs[part].namelist()) == {q['piece'] for f in manifest['files'] for q in f['pieces'] if q['artifactPart'] == part}
        for f in manifest['files']:
            hh = hashlib.sha256()
            length = 0
            for q in f['pieces']:
                assert q['offset'] == length and q['path'] == f['path']
                raw = zs[q['artifactPart']].read(q['piece'])
                assert len(raw) == q['bytes'] and sha(raw) == q['sha256']
                hh.update(raw)
                length += len(raw)
                pieces += 1
            assert length == f['bytes'] and hh.hexdigest() == f['sha256']
            raw = (root / 'originals' / f['path']).read_bytes()
            assert len(raw) == f['bytes'] and sha(raw) == f['sha256']
    finally:
        for z in zs.values():
            z.close()
    aliases = json.loads((here / 'original-evidence-aliases.json').read_text())
    for q in aliases:
        raw = (here / q['path']).read_bytes()
        assert len(raw) == q['bytes'] and sha(raw) == q['sha256']
        assert Path(q['originalPath']).read_bytes() == raw
    print(json.dumps({'wholeFiles': len(manifest['files']), 'pieces': pieces, 'publicOriginalAliases': len(aliases), 'errors': 0, 'payloadExecution': False}))

if __name__ == '__main__':
    main()
