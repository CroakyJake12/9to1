#!/usr/bin/env python3
"""Ubuntu late command: carry the tested hardware payload into the new root."""
import json
import os
from pathlib import Path
import re
import shutil
import sys

from common import RELEASE, check_original, digest, find_internal, partitions, run, verify_payload


def installed_menu(root_uuid, windows_uuid):
    if not re.fullmatch(r'[a-fA-F0-9-]{36}',root_uuid) or not re.fullmatch(r'[a-fA-F0-9]{4}-[a-fA-F0-9]{4}',windows_uuid):
        raise ValueError('Unexpected filesystem UUID')
    return f'''set timeout=10
set default=ubuntu
menuentry "Ubuntu" --id ubuntu {{
    search --no-floppy --fs-uuid --set=root {root_uuid}
    insmod mmap
    if cutmem 0x8800000000 0x8fffffffff; then
        insmod fdt
        if devicetree /boot/book4edge/{RELEASE}/board.dtb; then
            linux /boot/book4edge/{RELEASE}/Image root=UUID={root_uuid} ro rootwait clk_ignore_unused pd_ignore_unused arm64.nopauth cma=256M acpi=off module_blacklist=qcom_battmgr book4edge.suite=1
            initrd /boot/book4edge/{RELEASE}/initrd
            boot
        fi
    fi
    echo "Ubuntu could not start. Windows remains available in the menu."
    sleep --interruptible 20
}}
menuentry "Windows" --id windows {{
    search --no-floppy --fs-uuid --set=windows_efi {windows_uuid}
    chainloader ($windows_efi)/EFI/Microsoft/Boot/bootmgfw.efi
    boot
}}
'''


def main(target, payload):
    target = Path(target)
    payload = Path(payload)
    if os.geteuid() or str(target) != '/target' or target.is_symlink() or not target.is_mount():
        raise ValueError('Expected the Ubuntu installer target mount')
    manifest = verify_payload(payload)
    disk = find_internal()
    records = partitions(disk)
    check_original(records,manifest['originalPartitions'],resized=True)
    if run('findmnt','-n','-o','SOURCE','--target',str(target)) != records[6]['device'] or run('findmnt','-n','-o','FSTYPE','--target',str(target)) != 'ext4':
        raise ValueError('Target is not the new internal Ubuntu partition')
    efi = target/'boot/efi'
    if not efi.is_mount() or run('findmnt','-n','-o','SOURCE','--target',str(efi)) != records[1]['device']:
        raise ValueError('EFI mount does not match the original Windows EFI partition')
    backup = Path('/run/book4edge-log-usb/installation-backups')/Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    original_efi = json.loads((backup/'efi-hashes.json').read_text())
    for name,expected in original_efi.items():
        if digest(efi/name) != expected:
            raise ValueError('An existing EFI file changed during installation: '+name)
    if (efi/'EFI/book4edge').exists():
        raise ValueError('Refusing to replace an existing Book4 Edge bootloader')

    def copy(source, relative, mode=None):
        dest = target/relative
        dest.parent.mkdir(parents=True,exist_ok=True)
        if not dest.parent.resolve().is_relative_to(target.resolve()) or dest.is_symlink():
            raise ValueError('Target path escapes the new Ubuntu root: '+relative)
        shutil.copyfile(source,dest)
        if mode is not None:
            dest.chmod(mode)

    for path in (payload/'root-overlay').rglob('*'):
        if path.is_file():
            copy(path,path.relative_to(payload/'root-overlay').as_posix(),0o644)
    for item in manifest['firmware']:
        copy(payload/'firmware'/item['file'],'usr/lib/firmware/updates/'+item['file'],0o644)
    for source,dest in [('bluetooth-address.py','usr/sbin/book4edge-bluetooth-address'),
                        ('keyboard-backlight.py','usr/bin/book4edge-keyboard-backlight')]:
        copy(payload/'runtime'/source,dest,0o755)
    copy(payload/'bluetooth-address.private','etc/book4edge/bluetooth-address',0o600)
    copy(payload/'CODEX-HANDOFF.md','var/lib/book4edge/CODEX-HANDOFF.md',0o644)
    copy(payload/'sync-boot.py','usr/lib/book4edge/sync-boot.py',0o755)
    copy(payload/'initramfs-hook','etc/initramfs-tools/hooks/book4edge',0o755)
    copy(payload/'post-update-hook','etc/initramfs/post-update.d/book4edge',0o755)
    copy(payload/'bluetooth-address.service','etc/systemd/system/book4edge-bluetooth-address.service',0o644)
    copy(payload/'keyboard-backlight.desktop','usr/share/applications/book4edge-keyboard-backlight.desktop',0o644)
    copy(payload/'keyboard.rules','etc/udev/rules.d/99-book4edge-keyboard.rules',0o644)
    config_dir = target/'etc/modprobe.d'
    config_dir.mkdir(exist_ok=True)
    (config_dir/'book4edge.conf').write_text('blacklist qcom_battmgr\n')
    (target/'etc/modules-load.d').mkdir(exist_ok=True)
    (target/'etc/modules-load.d/book4edge.conf').write_text('samsung_galaxybook_ec\nsamsung_emuec\nscmi_cpufreq\n')
    # The live storage guard and logging/init wrappers must never enter the installed OS.
    for name in ['etc/udev/rules.d/99-book4edge-internal-test.rules',
                 'etc/modprobe.d/9to1-first-boot-speakers.conf']:
        (target/name).unlink(missing_ok=True)
    package_dir = target/'var/cache/book4edge'
    package_dir.mkdir(parents=True,exist_ok=True)
    packages = [f'linux-image-{RELEASE}_7.2.7-1_arm64.deb','book4edge-media-support_0.1.0_arm64.deb']
    for name in packages:
        copy(payload/'packages'/name,'var/cache/book4edge/'+name,0o644)

    def in_target(*command):
        return run('/snap/bin/ubuntu-desktop-bootstrap.curtin','in-target','--target=/target','--',*command)

    # First package installation builds the ordinary initrd. The boot update hook
    # becomes active only after its pinned Image/DTB metadata is written below.
    in_target('dpkg','-i',*[f'/var/cache/book4edge/{name}' for name in packages])
    in_target('apt-mark','manual','linux-image-'+RELEASE,'book4edge-media-support')
    in_target('systemctl','enable','book4edge-bluetooth-address.service')
    root_uuid = run('blkid','-s','UUID','-o','value',records[6]['device'])
    for source,name in [('Image','Image'),('board.dtb','board.dtb')]:
        copy(payload/source,f'boot/book4edge/{RELEASE}/{name}',0o644)
    boot_info = dict(release=RELEASE,rootUuid=root_uuid,
                     imageSHA256=digest(payload/'Image'),dtbSHA256=digest(payload/'board.dtb'))
    (target/'etc/book4edge/boot.json').write_text(json.dumps(boot_info,indent=2)+'\n')
    in_target('update-initramfs','-u','-k',RELEASE)
    menu = target/'boot/book4edge/grub.cfg'
    menu.write_text(installed_menu(root_uuid,manifest['windowsEfiUuid']))
    in_target('grub-script-check','/boot/book4edge/grub.cfg')
    stub = target/'var/cache/book4edge/efi-stub.cfg'
    stub.write_text(f'search --no-floppy --fs-uuid --set=root {root_uuid}\nconfigfile /boot/book4edge/grub.cfg\n')
    (efi/'EFI/book4edge').mkdir(parents=True)
    in_target('grub-mkstandalone','--format=arm64-efi','--locales=','--fonts=',
              '--modules=part_gpt ext2 fat normal configfile search search_fs_uuid linux fdt mmap chain gzio test echo sleep reboot halt',
              '--output=/boot/efi/EFI/book4edge/grubaa64.efi',
              'boot/grub/grub.cfg=/var/cache/book4edge/efi-stub.cfg')
    for name,expected in original_efi.items():
        if digest(efi/name) != expected:
            raise ValueError('An original EFI file changed: '+name)
    before = run('efibootmgr','-v')
    order = re.search(r'^BootOrder: (.+)$',before,re.M)
    if not order or re.search(r'^Boot[0-9A-Fa-f]{4}\*?\s+Ubuntu \(Book4 Edge\)',before,re.M):
        raise ValueError('Unexpected existing boot entry; inspect before adding another')
    # Register our own entry after every existing entry, preserving the default.
    run('efibootmgr','--create-only','--disk','/dev/'+disk.name,'--part','1',
        '--label','Ubuntu (Book4 Edge)','--loader','\\EFI\\book4edge\\grubaa64.efi')
    created = run('efibootmgr','-v')
    new_entry = re.search(r'^Boot([0-9A-Fa-f]{4})\*?\s+Ubuntu \(Book4 Edge\)',created,re.M)
    if not new_entry:
        raise ValueError('The new Ubuntu boot entry was not registered')
    expected_order = order.group(1)+','+new_entry.group(1).upper()
    run('efibootmgr','--bootorder',expected_order)
    after = run('efibootmgr','-v')
    after_order = re.search(r'^BootOrder: (.+)$',after,re.M)
    if not after_order or after_order.group(1).upper() != expected_order.upper():
        raise ValueError('Boot order changed unexpectedly')
    (backup/'efi-boot-entries-after.txt').write_text(after+'\n')
    result = dict(installed=True,physicalBootVerified=False,rootUuid=root_uuid,
                  windowsLoaderUnchanged=True,existingBootPriorityPreserved=True,
                  nextStep='Restart manually, press F10 and choose Ubuntu (Book4 Edge). Keep the USB recovery route until Ubuntu and Windows are tested.')
    (target/'var/lib/book4edge/installation-result.json').write_text(json.dumps(result,indent=2)+'\n')
    (backup/'installation-result.json').write_text(json.dumps(result,indent=2)+'\n')
    run('sync')


if __name__ == '__main__':
    main(*sys.argv[1:])
