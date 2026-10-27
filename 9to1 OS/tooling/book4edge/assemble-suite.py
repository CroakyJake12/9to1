#!/usr/bin/env python3
"""Add a separate test initrd. Never write physical media or replace a fallback."""
import argparse
import gzip
import importlib.util
import json
import re
import stat
from pathlib import Path

recipe = Path(__file__).with_name('assemble-live.py')
spec = importlib.util.spec_from_file_location('live', recipe)
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)
REG = stat.S_IFREG | 0o644
EXE = stat.S_IFREG | 0o755

def suite_grub_config(previous):
    start=previous.index('menuentry ')
    end=previous.index('menuentry ',start+1)
    first=previous[start:end]
    if 'TOUCHSCREEN + BATTERY TEST (select this first)' not in first:
        raise ValueError('Expected the existing opt-in input test first')
    first=first.replace('TOUCHSCREEN + BATTERY TEST (select this first)',
                        'WIRELESS + TOUCH + BATTERY TEST (select this first)')
    first=first.replace('/book4edge/initrd;', '/book4edge/initrd-suite;')
    first=first.replace(' book4edge.log=1', ' book4edge.suite=1 book4edge.log=1')
    if first.count('/book4edge/initrd-suite') != 1:
        raise ValueError('Unexpected initrd syntax')
    return previous[:start]+first+previous[start:].replace(
        'TOUCHSCREEN + BATTERY TEST (select this first)',
        'TOUCHSCREEN + BATTERY TEST (previous input test)',1)

def retest_grub_config(previous):
    start = previous.index('menuentry ')
    end = previous.index('menuentry ', start + 1)
    first = previous[start:end]
    if 'WIRELESS + TOUCH + BATTERY TEST (select this first)' not in first:
        raise ValueError('Expected the physically tested wireless suite first')
    first = first.replace('WIRELESS + TOUCH + BATTERY TEST (select this first)',
                          'BATTERY + WIFI + BLUETOOTH RETEST (select this first)')
    first = first.replace('/book4edge/initrd-suite;', '/book4edge/initrd-retest;')
    # systemd also interprets debug, causing startup errors to scroll out of logs.
    first = first.replace(' debug ---', ' ---')
    if first.count('/book4edge/initrd-retest') != 1:
        raise ValueError('Unexpected initrd syntax')
    return previous[:start] + first + previous[start:].replace(
        'WIRELESS + TOUCH + BATTERY TEST (select this first)',
        'WIRELESS + TOUCH + BATTERY TEST (previous test)', 1)

def build(tree, base_report, firmware, radio, output, bundle=None, root_overlay=None):
    old = json.loads(base_report.read_text())
    data = (tree/'book4edge/initrd').read_bytes()
    if live.sha(data) != old['initrdSHA256']:
        raise ValueError('Base initrd does not match its recorded identity')
    stock = (tree/'casper/initrd').read_bytes()
    if live.sha(stock) != old['stockInitrdSHA256'] or not data.startswith(stock):
        raise ValueError('Authenticated stock initrd prefix changed')
    offset = (len(stock)+3)&~3
    entries, _ = live.read_cpio(gzip.decompress(data[offset:]))
    release = old['kernelRelease']
    hook_key = 'scripts/casper-bottom/98book4edge-modules'
    hook = entries[hook_key][1].decode()
    # Protect the exact inventoried UFS model even if it appears after the
    # initial casper scan. This sets a volatile kernel flag, not disk metadata.
    entries['etc/udev/rules.d/99-book4edge-internal-test.rules'] = (REG, (
        'SUBSYSTEM=="block", ATTRS{model}=="THGJFJT2T85BAT0A*", ENV{UDISKS_IGNORE}="1"\n'
        'ACTION=="add|change", SUBSYSTEM=="block", ATTRS{model}=="THGJFJT2T85BAT0A*", '
        'RUN+="/sbin/blockdev --setro /dev/%k"\n').encode())
    firmware_report = json.loads((firmware/'manifest.json').read_text())
    for item in firmware_report['firmware']:
        name = live.safe_name(item['file'])
        if not (name.startswith(('qca/','ath12k/WCN7850/hw2.0/')) or name in (
                'qcom/gen70500_sqe.fw','qcom/gen70500_gmu.bin','regulatory.db','regulatory.db.p7s',
                'qcom/x1e80100/X1E80100-GalaxyBook4Edge-tplg.bin')):
            raise ValueError('Unexpected runtime firmware path: '+name)
        payload = (firmware/name).read_bytes()
        if live.sha(payload) != item['sha256']:
            raise ValueError('Firmware checksum mismatch: '+name)
        entries['usr/lib/firmware/updates/'+name] = (REG,payload)
    if bundle:
        release = '7.2.7-9to1-book4edge16-next'
        image = live.verify_kernel_bundle(bundle,release)
        prefix = f'usr/lib/modules/{old["kernelRelease"]}'
        entries = {n:v for n,v in entries.items() if n != prefix and not n.startswith(prefix+'/')}
        entries.pop('usr/lib/book4edge/book4_ec.ko',None)
        entries.update(live.module_entries((bundle/f'linux-image-{release}_7.2.7-1_arm64.deb').read_bytes(),release))
        hook = hook.replace(old['kernelRelease'],release)
        hook = hook.replace("'blacklist snd_soc_x1e80100'", "'blacklist snd_soc_wsa883x\nblacklist snd_soc_wsa884x'")
        hook = hook.replace('Speakers disabled.', 'Internal speaker amplifiers disabled; headphones and microphones available for testing.')
        wanted = entries['conf/modules'][1].decode().splitlines()+['samsung_emuec','samsung_galaxybook_ec','scmi_cpufreq']
        available = {Path(n).name.split('.ko')[0].replace('-','_') for n in entries if '.ko' in n}
        entries['conf/modules'] = (REG, ('\n'.join(dict.fromkeys(n for n in wanted if n in available))+'\n').encode())
    else:
        image = (tree/'book4edge/Image').read_bytes()
        if live.sha(image) != old['imageSHA256']:
            raise ValueError('Baseline kernel identity changed')
    runtime = recipe.with_name('runtime')
    if bundle:
        entries['book4edge-root/usr/bin/book4edge-keyboard-backlight'] = (EXE, (runtime/'keyboard-backlight.py').read_text().encode())
        entries['book4edge-root/usr/share/applications/book4edge-keyboard-backlight.desktop'] = (REG, b'''[Desktop Entry]
Type=Application
Name=Keyboard backlight
Comment=Adjust the Samsung keyboard light
Exec=/usr/bin/book4edge-keyboard-backlight
Icon=preferences-desktop-keyboard
Terminal=false
Categories=Settings;HardwareSettings;
''')
    if not bundle:
        entries['usr/sbin/book4edge-battery-test'] = (EXE,
            recipe.with_name('battery-test').joinpath('start-battery-test.sh').read_text().encode())
    address = radio.read_text().strip()
    if not re.fullmatch(r'(?:[0-9A-F]{2}:){5}[0-9A-F]{2}',address):
        raise ValueError('Expected original Windows radio address')
    entries['usr/sbin/book4edge-boot-log'] = (EXE,recipe.with_name('boot-log.sh').read_text().encode())
    entries['book4edge-root/etc/book4edge/bluetooth-address'] = (stat.S_IFREG|0o600,(address+'\n').encode())
    entries['book4edge-root/usr/sbin/book4edge-bluetooth-address'] = (EXE,(runtime/'bluetooth-address.py').read_text().encode())
    entries['book4edge-root/usr/bin/book4edge-hardware-check'] = (EXE,(runtime/'hardware-check.py').read_text().encode())
    service = b'''[Unit]
Description=Restore this laptop original Bluetooth radio address in the live session
Before=bluetooth.service
Wants=bluetooth.service
ConditionKernelCommandLine=book4edge.suite=1
[Service]
Type=oneshot
ExecStart=/usr/sbin/book4edge-bluetooth-address
TimeoutStartSec=100
RemainAfterExit=yes
[Install]
WantedBy=multi-user.target
'''
    entries['book4edge-root/etc/systemd/system/book4edge-bluetooth-address.service'] = (REG,service)
    entries['book4edge-root/etc/systemd/system/multi-user.target.wants/book4edge-bluetooth-address.service'] = (stat.S_IFLNK|0o777,b'../book4edge-bluetooth-address.service')
    desktop = b'''[Desktop Entry]
Type=Application
Name=Book4 Edge hardware check
Comment=Test this live session and save observations to the USB logs
Exec=/usr/bin/book4edge-hardware-check
Icon=preferences-system
Terminal=false
Categories=System;
X-GNOME-Autostart-enabled=true
'''
    entries['book4edge-root/etc/xdg/autostart/book4edge-hardware-check.desktop'] = (REG,desktop)
    entries['book4edge-root/usr/share/applications/book4edge-hardware-check.desktop'] = (REG,desktop)
    if root_overlay:
        for path in root_overlay.rglob('*'):
            if path.is_file():
                name=live.safe_name(path.relative_to(root_overlay).as_posix())
                if not name.startswith('usr/share/alsa/ucm2/'):
                    raise ValueError('Only reviewed UCM profiles may be supplied as an extra overlay')
                entries['book4edge-root/'+name]=(REG,path.read_bytes())
    hook += '\n# All destinations are in the casper RAM overlay.\ncp -a /book4edge-root/. /root/\n'
    entries[hook_key]=(EXE,hook.encode())
    for name,(mode,payload) in entries.items():
        if (name.startswith('scripts/') or name.endswith('.sh')) and stat.S_ISREG(mode) and b'\r\n' in payload:
            raise ValueError('CRLF in shell script: '+name)
    overlay=live.cpio(live.add_parents(entries))
    if live.read_cpio(overlay)[0] != live.add_parents(entries):
        raise ValueError('CPIO roundtrip failed')
    combined=stock+b'\0'*(-len(stock)%4)+gzip.compress(overlay,compresslevel=6,mtime=0)
    output.mkdir(parents=True,exist_ok=False)
    (output/'firmware-manifest.json').write_bytes((firmware/'manifest.json').read_bytes())
    (output/'initrd').write_bytes(combined)
    (output/'Image').write_bytes(image)
    if bundle:
        dtb=(bundle/'x1e84100-samsung-galaxy-book4-edge-16.dtb').read_bytes()
    else:
        dtb=(tree/'book4edge/book4edge16-gpu.dtb').read_bytes()
        if live.sha(dtb) != old['displayTestDTBSHA256']:
            raise ValueError('Baseline display/input DTB changed')
    (output/'book4edge16.dtb').write_bytes(dtb)
    report={'release':release,'baseInitrdSHA256':old['initrdSHA256'],'initrdSHA256':live.sha(combined),
        'imageSHA256':live.sha(image),'dtbSHA256':live.sha(dtb),'bytes':len(combined),'physicallyTested':False,
        'sourceKernelCommit':(bundle/'source.commit').read_text().strip() if bundle else None,
        'firmwareManifestSHA256':live.sha((firmware/'manifest.json').read_bytes()),
        'rootOverlayPaths':sorted(n for n in entries if n.startswith('book4edge-root/')),
        'shellFiles':{n:live.sha(v[1]) for n,v in entries.items() if n.startswith('scripts/')},
        'internalReadOnlyGuardPresent':'blockdev --setro' in hook,
        'fallbackUnchanged':live.sha((tree/'book4edge/initrd').read_bytes())==old['initrdSHA256']}
    (output/'assembly.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('tree','base-report','firmware','radio','output'):
        parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--bundle',type=Path)
    parser.add_argument('--root-overlay',type=Path)
    args=parser.parse_args()
    print(json.dumps(build(args.tree,args.base_report,args.firmware,args.radio,args.output,args.bundle,args.root_overlay),indent=2))
