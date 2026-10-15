#!/usr/bin/env python3
"""Prepare the existing Ubuntu graphical installer after live hardware checks."""
import http.client
import json
import os
from pathlib import Path
import shutil
import socket
import sys
import tempfile

from common import RELEASE, UBUNTU_BYTES, check_original, digest, find_internal, partitions, run, storage_plan, verify_payload
import re

PAYLOAD = Path('/cdrom/book4edge-install')
REQUIRED = ('display','keyboard','touchpad','touchscreen','battery','wifi',
            'internal_microphone','internal_speakers','camera','keyboard_backlight','fingerprint')


def accepted(report, boot_id):
    if report.get('bootId') != boot_id:
        raise ValueError('Complete the hardware checklist in this live session first')
    missing = [name for name in REQUIRED if report.get('checks',{}).get(name,{}).get('status') != 'passed']
    if missing:
        raise ValueError('These required checks have not passed: ' + ', '.join(missing))


def backend_state():
    connection = http.client.HTTPConnection('localhost', timeout=10)
    connection.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    connection.sock.settimeout(10)
    connection.sock.connect('/run/subiquity/socket')
    try:
        connection.request('GET','/meta/status')
        response = connection.getresponse()
        if response.status != 200:
            raise ValueError('Could not read the Ubuntu installer state')
        return json.load(response)['state']
    finally:
        connection.close()


def installer_config(disk, original, backup):
    return {'autoinstall':dict(version=1, **{
        'interactive-sections':['identity'], 'locale':'en_GB.UTF-8',
        'keyboard':{'layout':'gb'}, 'timezone':'Europe/London',
        'source':{'id':'ubuntu-desktop'}, 'kernel':{'package':'linux-generic'},
        'refresh-installer':{'update':False},
        'storage':{'config':storage_plan(disk,original),
                   'grub':{'update_nvram':False,'reorder_uefi':False,'remove_duplicate_entries':False}},
        'debconf-selections':'grub-efi-arm64 grub2/force_efi_extra_removable boolean false\ngrub-efi-arm64 grub2/update_nvram boolean false\n',
        'packages':['grub-efi-arm64-bin','efibootmgr','python3','zenity','libspa-0.2-libcamera','gnome-snapshot','fprintd','libpam-fprintd'],
        'late-commands':[['python3',str(PAYLOAD/'finish.py'),'/target',str(PAYLOAD)]],
        'error-commands':[['cp','-a','/var/log/installer',str(backup/'installer-error-logs')]],
    })}


def main():
    if os.geteuid() != 0 or 'book4edge.media=1' not in Path('/proc/cmdline').read_text().split():
        raise ValueError('Use Install Ubuntu from the updated driver live session')
    manifest = verify_payload(PAYLOAD)
    if os.uname().release != RELEASE:
        raise ValueError('The live kernel does not match the installation payload')
    uid = int(os.environ.get('PKEXEC_UID','-1'))
    if uid < 1000:
        raise ValueError('Start this installer from the live desktop')
    report_path = Path(f'/run/user/{uid}/book4edge-hardware-check.json')
    if report_path.is_symlink() or report_path.stat().st_uid != uid:
        raise ValueError('Unexpected hardware checklist ownership')
    boot_id = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    accepted(json.loads(report_path.read_text()), boot_id)
    if backend_state() not in ('WAITING','NEEDS_CONFIRMATION'):
        raise ValueError('The Ubuntu installer is already active; finish or cancel it first')
    if Path('/target').is_mount() or Path('/autoinstall.yaml').exists():
        raise ValueError('An installation has already been prepared in this session')
    disk = find_internal()
    records = partitions(disk)
    original = manifest['originalPartitions']
    check_original(records, original)
    if run('blkid','-s','TYPE','-o','value',records[3]['device']) != 'ntfs':
        raise ValueError('Windows is not an ordinary NTFS volume; stop here')
    # No --force: a dirty or hibernated Windows volume must be handled in Windows.
    info = run('/snap/ubuntu-desktop-bootstrap/current/sbin/ntfsresize', '--info', '--no-action', records[3]['device'], env={**os.environ,'LC_ALL':'C'})
    minimum = re.search(r'You might resize at (\d+) bytes', info)
    if not minimum or int(minimum.group(1)) + 1024**3 > records[3]['size'] - UBUNTU_BYTES:
        raise ValueError('NTFS has not confirmed sufficient space for the requested 150 GiB')
    usb = Path('/run/book4edge-log-usb')
    backup = usb/'installation-backups'/boot_id
    if not usb.is_mount() or backup.exists():
        raise ValueError('Verified USB logging mount is missing or an installation backup already exists')
    if run('findmnt','-n','-o','SOURCE','--target',str(usb)) != run('blkid','-t','PARTUUID=38af0c27-e16d-425f-8595-da118220143d','-o','device'):
        raise ValueError('Installation backup destination is not the expected USB')
    backup.mkdir(mode=0o700,parents=True)
    (backup/'partitions.json').write_text(json.dumps(records,indent=2))
    (backup/'windows-ntfs-info.txt').write_text(info+'\n')
    (backup/'partition-table.sfdisk').write_text(run('sfdisk','--dump','/dev/'+disk.name)+'\n')
    (backup/'efi-boot-entries-before.txt').write_text(run('efibootmgr','-v')+'\n')
    efi_dir = Path(tempfile.mkdtemp(prefix='book4edge-efi-'))
    try:
        run('mount','-o','ro,nosuid,nodev,noexec',records[1]['device'],str(efi_dir))
        loader = efi_dir/'EFI/Microsoft/Boot/bootmgfw.efi'
        if digest(loader) != manifest['windowsLoaderSHA256']:
            raise ValueError('Windows Boot Manager changed; refresh its verified reference')
        shutil.copytree(efi_dir,backup/'efi',symlinks=False)
        efi_hashes = {p.relative_to(efi_dir).as_posix():digest(p) for p in efi_dir.rglob('*') if p.is_file()}
        (backup/'efi-hashes.json').write_text(json.dumps(efi_hashes,indent=2))
    finally:
        if efi_dir.is_mount():
            run('umount',str(efi_dir))
        efi_dir.rmdir()
    config = installer_config('/dev/'+disk.name,original,backup)
    # JSON is a YAML subset. It avoids serializing any account password ourselves.
    seed = json.dumps(config,indent=2)+'\n'
    (backup/'planned-autoinstall.json').write_text(seed)
    guard = Path('/etc/udev/rules.d/99-book4edge-internal-test.rules')
    guard_content = guard.read_bytes()
    if b'blockdev --setro' not in guard_content:
        raise ValueError('Expected live storage protection rule is missing')
    service = 'snap.ubuntu-desktop-bootstrap.subiquity-server.service'
    run('systemctl','stop',service)
    unlocked = ['/dev/'+disk.name,records[1]['device'],records[3]['device']]
    try:
        Path('/autoinstall.yaml').write_text(seed)
        guard.unlink()
        run('udevadm','control','--reload-rules')
        for device in unlocked:
            run('blockdev','--setrw',device)
        run('systemctl','start',service)
    except Exception:
        # Always attempt every restoration even if stopping the service fails.
        guard.write_bytes(guard_content)
        Path('/autoinstall.yaml').unlink(missing_ok=True)
        recovery = [('systemctl','stop',service),('udevadm','control','--reload-rules')]
        recovery += [('blockdev','--setro',d) for d in reversed(unlocked)]
        for command in recovery:
            try:
                run(*command)
            except Exception as rollback_error:
                print('Restoration needs attention: '+str(rollback_error),file=sys.stderr)
        raise
    print('Installer prepared: Ubuntu 150 GiB; Windows and recovery partitions preserved. Enter your Ubuntu account details and review the installer summary.')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(str(error),file=sys.stderr)
        raise SystemExit(1)
