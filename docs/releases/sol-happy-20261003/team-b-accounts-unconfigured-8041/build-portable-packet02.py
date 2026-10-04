#!/usr/bin/env python3
"""Build only after the root's separate large disk-writer gate."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import shutil
import zipfile

base = Path(__file__).resolve().parent
inventory_path = base / 'PORTABLE-INTEROP7-PACKAGE-INVENTORY02.json'
expected_inventory_sha = '9cdcf53dd00f70f76718982257052cd2fcfd281c8f7a0239f1e68116a307d21d'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
if os.environ.get('B5_INTEROP7_PACKAGE_WRITER_GRANTED') != 'granted':
    raise RuntimeError('Root large disk-writer gate is absent')
if sha(inventory_path) != expected_inventory_sha:
    raise RuntimeError('Frozen inventory changed')
inventory = json.loads(inventory_path.read_text())
output = Path(inventory['outputZip'])
if output.exists() or shutil.disk_usage('/workspace').free < inventory['totalUncompressedBytes'] + 256000000:
    raise RuntimeError('Fresh output or required workspace reserve unavailable')
rows = inventory['members'] + [
    {'sourcePath': str(inventory_path), 'member': 'PACKAGE-INVENTORY.original.json', 'bytes': inventory_path.stat().st_size, 'sha256': sha(inventory_path)},
    {'sourcePath': str(base / 'README.md'), 'member': 'README.md', 'bytes': (base / 'README.md').stat().st_size, 'sha256': sha(base / 'README.md')},
    {'sourcePath': str(Path(__file__).resolve()), 'member': 'build-portable-packet.original.py', 'bytes': Path(__file__).stat().st_size, 'sha256': sha(__file__)},
]
if len({r['member'] for r in rows}) != len(rows):
    raise RuntimeError('Duplicate packet member')
for row in rows:
    if Path(row['member']).is_absolute() or '..' in Path(row['member']).parts:
        raise RuntimeError('Unsafe packet member')
    path = Path(row['sourcePath'])
    if path.stat().st_size != row['bytes'] or sha(path) != row['sha256']:
        raise RuntimeError('Frozen source input changed')
receipt = {'scope': inventory['scope'], 'state': 'BUILDING', 'inventorySha256': expected_inventory_sha, 'archive': str(output)}
try:
    with zipfile.ZipFile(output, mode='x', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=False) as packet:
        for row in rows:
            if shutil.disk_usage('/workspace').free < 256000000:
                raise RuntimeError('Workspace reserve reached')
            info = zipfile.ZipInfo(row['member'], date_time=(2026, 10, 4, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            packet.writestr(info, Path(row['sourcePath']).read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=6)
            if output.stat().st_size > inventory['archiveMaximumBytes']:
                raise RuntimeError('Packet exceeds100MiB bound')
    with zipfile.ZipFile(output) as packet:
        entries = packet.infolist()
        expected = {r['member']: r for r in rows}
        if len(entries) != len(expected) or set(packet.namelist()) != set(expected):
            raise RuntimeError('Packet full member inventory differs')
        for entry in entries:
            raw = packet.read(entry.filename)
            row = expected[entry.filename]
            if len(raw) != row['bytes'] or hashlib.sha256(raw).hexdigest() != row['sha256']:
                raise RuntimeError('Packet member byte verification failed')
    for row in rows:
        if Path(row['sourcePath']).stat().st_size != row['bytes'] or sha(row['sourcePath']) != row['sha256']:
            raise RuntimeError('Source changed during packaging')
    receipt.update(state='EXACT_PACKET_VERIFIED', archiveSha256=sha(output), archiveBytes=output.stat().st_size,
                   memberCount=len(rows), rawBytes=sum(r['bytes'] for r in rows), allSourceInputsUnchanged=True,
                   allArchiveMembersBytesVerified=True, workspaceFreeBytes=shutil.disk_usage('/workspace').free,
                   members=rows)
except Exception as error:
    receipt.update(state='PACKET_BUILD_FAILURE_PRESERVED', safeFailureType=type(error).__name__)
    raise
finally:
    receipt['recordedUTC'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    (base / 'PORTABLE-INTEROP7-BUILD-RECEIPT02.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({k: receipt[k] for k in ['state', 'archiveSha256', 'archiveBytes', 'memberCount', 'rawBytes', 'allSourceInputsUnchanged']}))
