"""Shared checks for the private, machine-specific installer payload."""
import hashlib
import json
from pathlib import Path
import subprocess

RELEASE = '7.2.7-9to1-book4edge16-next'
UBUNTU_BYTES = 150 * 1024**3
DISK_BYTES = 511864471552
MODEL = 'THGJFJT2T85BAT0A'
EFI_GUID = 'ef1ea59f-b473-4131-9921-38821bb6b2d0'


def run(*args, **kwargs):
    return subprocess.run(args, check=True, text=True, capture_output=True, **kwargs).stdout.strip()


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_payload(payload):
    payload = Path(payload).resolve(strict=True)
    expected = Path('/etc/book4edge/install-manifest-sha256').read_text().strip()
    if digest(payload/'manifest.json') != expected:
        raise ValueError('Installer manifest identity changed')
    manifest = json.loads((payload/'manifest.json').read_text())
    for name, expected_hash in manifest['files'].items():
        path = payload/name
        if path.is_symlink() or not path.resolve(strict=True).is_relative_to(payload) or digest(path) != expected_hash:
            raise ValueError('Installer file failed verification: ' + name)
    return manifest


def find_internal():
    found = []
    for path in Path('/sys/class/block').iterdir():
        if (path/'partition').exists() or not (path/'device/model').is_file():
            continue
        if (path/'device/model').read_text().strip() == MODEL and int((path/'size').read_text())*512 == DISK_BYTES:
            found.append(path)
    if len(found) != 1:
        raise ValueError('The expected internal UFS disk was not uniquely found')
    return found[0]


def partitions(disk):
    records = {}
    for part in disk.iterdir():
        if not (part/'partition').is_file():
            continue
        number = int((part/'partition').read_text())
        device = '/dev/' + part.name
        records[number] = dict(device=device, offset=int((part/'start').read_text())*512,
            size=int((part/'size').read_text())*512,
            guid=run('blkid','-p','-s','PART_ENTRY_UUID','-o','value',device).lower(),
            partition_type=run('blkid','-p','-s','PART_ENTRY_TYPE','-o','value',device).lower())
    return records


def check_original(records, original, resized=False):
    expected_numbers = {int(p['PartitionNumber']) for p in original}
    if set(records) != expected_numbers | ({6} if resized else set()):
        raise ValueError('Unexpected internal partition set')
    for p in original:
        n = int(p['PartitionNumber'])
        size = int(p['Size']) - (UBUNTU_BYTES if resized and n == 3 else 0)
        expected = (int(p['Offset']), size, p['Guid'].strip('{}').lower(), p['GptType'].strip('{}').lower())
        actual = (records[n]['offset'], records[n]['size'], records[n]['guid'], records[n]['partition_type'])
        if actual != expected:
            raise ValueError('Windows partition identity/layout changed: ' + str(n))
    if resized:
        end_c = records[3]['offset'] + records[3]['size']
        if records[6]['offset'] != end_c or records[6]['size'] != UBUNTU_BYTES:
            raise ValueError('Ubuntu must occupy exactly the requested 150 GiB')


def storage_plan(disk_device, original):
    config = [dict(id='internal', type='disk', path=disk_device, ptable='gpt', preserve=True)]
    for p in original:
        n = int(p['PartitionNumber'])
        item = dict(id=f'p{n}', type='partition', device='internal', number=n,
                    offset=int(p['Offset']), size=int(p['Size']), preserve=True,
                    partition_type=p['GptType'].strip('{}').lower(),uuid=p['Guid'].strip('{}').lower())
        if n == 1:
            item.update(flag='boot', grub_device=True)
        elif n == 3:
            item.update(size=int(p['Size'])-UBUNTU_BYTES, resize=True)
        config.append(item)
    windows = next(p for p in original if int(p['PartitionNumber']) == 3)
    config += [dict(id='ubuntu', type='partition', device='internal', number=6,
                    offset=int(windows['Offset'])+int(windows['Size'])-UBUNTU_BYTES,
                    size=UBUNTU_BYTES, preserve=False),
               dict(id='root-fs',type='format',volume='ubuntu',fstype='ext4',label='Ubuntu',preserve=False),
               dict(id='root-mount',type='mount',device='root-fs',path='/'),
               dict(id='efi-fs',type='format',volume='p1',fstype='fat32',preserve=True),
               dict(id='efi-mount',type='mount',device='efi-fs',path='/boot/efi')]
    return config
