"""Read-only custody replay. Never execute original payloads or access the network."""
from pathlib import Path, PurePosixPath
import argparse, hashlib, json, stat, zipfile

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def safe_zip(z):
    names = set()
    for item in z.infolist():
        name = item.filename
        assert not PurePosixPath(name).is_absolute() and '\\' not in name
        assert all(part not in ('', '.', '..') for part in name.split('/'))
        assert name not in names and not item.is_dir()
        names.add(name)
        assert stat.S_IFMT(item.external_attr >> 16) not in (stat.S_IFLNK, stat.S_IFIFO, stat.S_IFSOCK)
        assert not item.flag_bits & 1
        assert item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
    assert z.testzip() is None

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--custody', type=Path, required=True)
    args = parser.parse_args()
    root = args.custody
    here = Path(__file__).resolve().parent
    report = json.loads((here / 'custody-report.json').read_text())
    verified = 0
    pieces = 0
    for group, index in report['indices'].items():
        raw = (root / (group + '-index.zip')).read_bytes()
        assert len(raw) == index['outerBytes'] and digest(raw) == index['outerSha256']
        with zipfile.ZipFile(root / (group + '-index.zip')) as z:
            safe_zip(z)
            assert z.namelist() == ['manifest.json']
            manifest_raw = z.read('manifest.json')
        assert len(manifest_raw) == index['manifestBytes']
        assert digest(manifest_raw) == index['manifestSha256']
        manifest = json.loads(manifest_raw)
        inner = {}
        try:
            for part, seal in enumerate(report['archives'][group]):
                outer = root / (group + '-outer-' + str(part) + '.zip')
                raw = outer.read_bytes()
                assert len(raw) == seal['outerBytes'] and digest(raw) == seal['outerSha256']
                with zipfile.ZipFile(outer) as z:
                    safe_zip(z)
                    assert len(z.namelist()) == 1
                    raw = z.read(z.namelist()[0])
                assert len(raw) == seal['innerBytes'] and digest(raw) == seal['innerSha256']
                path = root / (group + '-inner-' + str(part) + '.zip')
                assert path.read_bytes() == raw
                inner[part] = zipfile.ZipFile(path)
                safe_zip(inner[part])
                expected = {p['piece'] for f in manifest['files'] for p in f['pieces'] if p['artifactPart'] == part}
                assert set(inner[part].namelist()) == expected
            for f in manifest['files']:
                hh = hashlib.sha256()
                length = 0
                for p in f['pieces']:
                    assert p['offset'] == length and p['path'] == f['path']
                    raw = inner[p['artifactPart']].read(p['piece'])
                    assert len(raw) == p['bytes'] and digest(raw) == p['sha256']
                    hh.update(raw)
                    length += len(raw)
                    pieces += 1
                assert length == f['bytes'] and hh.hexdigest() == f['sha256']
                raw = (root / group / f['path']).read_bytes()
                assert len(raw) == f['bytes'] and digest(raw) == f['sha256']
                verified += 1
        finally:
            for z in inner.values():
                z.close()
    for alias in json.loads((here / 'original-evidence-aliases.json').read_text()):
        raw = (here / alias['path']).read_bytes()
        assert len(raw) == alias['bytes'] and digest(raw) == alias['sha256']
        assert Path(alias['originalPath']).read_bytes() == raw
    print(json.dumps({'wholeFiles': verified, 'pieces': pieces, 'errors': 0, 'payloadExecution': False}))

if __name__ == '__main__':
    main()
