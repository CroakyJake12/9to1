#!/usr/bin/env python3
"""Generate a short live-USB menu; Windows chainloads its existing EFI loader."""
import argparse
from pathlib import Path
import re


def render(windows_uuid, updated_drivers=False):
    if not re.fullmatch(r'[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}', windows_uuid):
        raise ValueError('Expected the verified Windows EFI FAT filesystem UUID')
    common = ('boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth '
              'cma=256M console=tty0 acpi=off loglevel=6 printk.time=1 log_buf_len=4M '
              'fbcon=nodefer noplymouth plymouth.enable=0 systemd.show_status=1 '
              'systemd.log_target=kmsg systemd.log_level=info udev.log_level=info '
              'init=/usr/sbin/book4edge-init module_blacklist=qcom_battmgr '
              'book4edge.suite=1 book4edge.log=1')

    def ubuntu(title, identity, directory, initrd, extra='', indent=''):
        text = f'''menuentry "{title}" --id {identity} {{
    if prepare_ubuntu; then
        set gfxpayload=keep
        if devicetree /{directory}/book4edge16-gpu.dtb; then
            if linux /{directory}/Image {common}{extra} ---; then
                if initrd /{directory}/{initrd}; then
                    boot
                fi
            fi
        fi
    fi
    echo "Ubuntu could not start. Return to the menu or restart."
    sleep --interruptible 15
}}
'''
        return ''.join(indent + line if line.strip() else line for line in text.splitlines(True))

    menu = '''set timeout=-1
set default=ubuntu-live
loadfont unicode

# Linux-only preparation stays inside its entry. Windows uses the firmware's
# original hardware description and its existing Microsoft EFI executable.
function prepare_ubuntu {
    if [ "$lockdown" = "y" ]; then
        echo "This Ubuntu build requires the existing Secure Boot setting."
        return 1
    fi
    insmod mmap
    if cutmem 0x8800000000 0x8fffffffff; then
        insmod fdt
        return 0
    fi
    echo "Required Ubuntu memory reservation failed."
    return 1
}

'''
    menu += ubuntu('Ubuntu (live USB)', 'ubuntu-live', 'book4edge', 'initrd-retest', ' book4edge.battery=1')
    menu += f'''
menuentry "Windows" --id windows {{
    insmod part_gpt
    insmod fat
    insmod search_fs_uuid
    insmod chain
    if search --no-floppy --fs-uuid --set=windows_efi {windows_uuid.upper()}; then
        if [ -f ($windows_efi)/EFI/Microsoft/Boot/bootmgfw.efi ]; then
            if chainloader ($windows_efi)/EFI/Microsoft/Boot/bootmgfw.efi; then
                boot
            fi
        fi
    fi
    echo "Windows Boot Manager was not found. Restart and select it with F10."
    sleep --interruptible 15
}}

submenu "More options" {{
'''
    if updated_drivers:
        menu += ubuntu('Ubuntu (updated drivers)', 'ubuntu-updated', 'book4edge-next', 'initrd', indent='    ')
    menu += '''    menuentry "Firmware settings" {
        fwsetup
    }
    menuentry "Restart" {
        reboot
    }
    menuentry "Power off" {
        halt
    }
}
'''
    return menu


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--windows-uuid', required=True)
    parser.add_argument('--updated-drivers', action='store_true')
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.write_text(render(args.windows_uuid, args.updated_drivers), encoding='utf-8', newline='\n')
