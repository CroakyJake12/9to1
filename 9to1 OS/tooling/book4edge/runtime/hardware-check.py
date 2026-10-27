#!/usr/bin/env python3
"""Manual checks; save user observations separately from automatic detection."""
import datetime
import json
import os
from pathlib import Path
import subprocess

out = Path(os.environ.get('XDG_RUNTIME_DIR', f'/run/user/{os.getuid()}')) / 'book4edge-hardware-check.json'
boot_id = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
media = 'book4edge.media=1' in Path('/proc/cmdline').read_text().split()
report = {'bootId': boot_id, 'kernel': os.uname().release, 'kind': 'user observations', 'checks': {}, 'allHardwarePassed': False}
if out.exists():
    old = json.loads(out.read_text())
    if old.get('bootId') == boot_id:
        report = old

def save():
    report['updatedAt'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    temporary = out.with_suffix('.new')
    temporary.write_text(json.dumps(report, indent=2)+'\n')
    os.chmod(temporary, 0o600)
    temporary.replace(out)

speaker_note = ('The updated drivers enable a deliberately quiet two-speaker test. Start at 10% volume and use a brief sound sample; stop if it distorts. Full-volume and four-speaker operation are not validated.' if media else 'Internal speakers remain disabled.')
intro = subprocess.run(['zenity','--question','--title=Book4 Edge hardware check',
    '--width=560','--ok-label=Start checks','--cancel-label=Later',
    '--text=This checks the USB live session. Windows stays protected until you separately start installation.\n\nUse Settings to try each feature, then record Passed, Failed or Not tested. Results save automatically to the USB boot logs.\n\nKeep the installer plugged into USB-A. '+speaker_note+' Unavailable accessories must stay untested.'])
if intro.returncode:
    raise SystemExit(0)
checks = [
    ('display', 'Display and brightness', 'Check the picture is stable. Change brightness in Settings and with the keyboard. Try the resolution and refresh rates you normally use.'),
    ('keyboard', 'Keyboard and function keys', 'Type in a text box. Test the keys you use, including brightness and volume.'),
    ('keyboard_backlight', 'Keyboard backlight', 'Open Keyboard backlight from the app list. Test off, low and higher settings. Check the keys actually light up.'),
    ('touchpad', 'Touchpad', 'Test pointer movement, clicking, two-finger scrolling and gestures.'),
    ('touchscreen', 'Touchscreen', 'Tap buttons in several parts of the screen. Try dragging and scrolling using your finger.'),
    ('battery', 'Battery and charging', 'Check the percentage looks plausible. Unplug the charger briefly, then reconnect it. Check that the charging state changes. If the computer restarts or hangs, this is a failure.'),
    ('wifi', 'Wi-Fi', 'Open Settings > Wi-Fi. Connect to your usual network and open a web page. Check the connection remains usable for a few minutes.'),
    ('bluetooth', 'Bluetooth headphones', 'Open Settings > Bluetooth and pair your wireless headphones. Confirm they connect. Pairing may need to be repeated in Windows; pairing keys are not copied.'),
    ('bluetooth_audio', 'Wireless headphone audio', 'Select your headphones as the output in Sound settings. Play the left and right speaker tests at a comfortable volume. If your headphones have a microphone, test its input meter too. Mark Not tested if only part was tested.'),
    ('internal_microphone', 'Built-in microphone', 'Select the internal microphone in Sound settings. Speak and watch the input meter. The Bluetooth microphone does not count for this check.'),
    ('camera', 'Camera and indicator', 'Open Camera. Check for a live, correctly oriented picture and a lit camera indicator. Close Camera and verify the indicator switches off. A missing camera or incorrect indicator is a failure.'),
    ('usb_c', 'USB-C ports', 'If you have a USB-C accessory, test data in each port and both plug orientations. Charging alone does not confirm data. Otherwise choose Not tested.'),
    ('external_display', 'External display', 'If available, connect an HDMI or USB-C monitor and test picture, reconnecting and both USB-C plug orientations. Otherwise choose Not tested.'),
    ('card_reader', 'Memory card reader', 'If you have a spare card, check it appears in Files and read a file. Do not format it. Otherwise choose Not tested.'),
    ('wired_audio', 'Wired headphones and headset', 'No wired headphones are available for this session. Choose Not tested.'),
    ('usb_a_hotplug', 'USB-A hot-plug', 'The installer occupies the only USB-A port. Keep it connected and choose Not tested. Running the session from it already provides evidence that the port can read the installer.'),
    ('fingerprint', 'Fingerprint reader', 'In Settings > System > Users, check whether fingerprint login is offered. Detection alone is not a pass: registration and a successful fingerprint verification are required. Do not reset the reader or delete Windows fingerprints. If enrollment needs either action, choose Not tested and keep the logs.'),
    ('suspend', 'Sleep and wake (last)', 'Your earlier results are saved now. Close the lid for about 30 seconds, then reopen it. Check the screen, touchpad, Wi-Fi and Bluetooth reconnect. Do not change sleep modes or force deep sleep. Choose Not tested if you do not want to attempt this yet.'),
]
if media:
    checks.insert(next(i for i,c in enumerate(checks) if c[0]=='internal_microphone'),
        ('internal_speakers','Internal speakers (quiet test)',
         'Disconnect Bluetooth headphones. Select Internal speakers (quiet limit) in Sound settings. At 10% volume, briefly test left and right. Both sides must make clear sound without distortion. This tests two woofers at a strict low-volume ceiling; it does not validate full-volume playback or the tweeters.'))
else:
    report['checks']['internal_speakers'] = {'status': 'blocked', 'reason': 'Speaker amplifiers disabled pending a verified protection solution.'}
save()
for key, title, description in checks:
    response = subprocess.run(['zenity','--question','--title='+title,'--width=560',
        '--ok-label=Passed','--cancel-label=Failed','--extra-button=Not tested','--extra-button=Finish for now',
        '--text='+description+'\n\nChoose Passed only after testing the feature.'],text=True,capture_output=True)
    extra = response.stdout.strip()
    if extra == 'Finish for now':
        break
    status = 'not tested' if extra == 'Not tested' else ('passed' if response.returncode == 0 else 'failed')
    report['checks'][key] = {'status':status}
    save()
save()
subprocess.run(['zenity','--info','--title=Results saved','--width=520',
    '--text=Your observations are saved. Leave Ubuntu running for another 20 seconds so the USB logger can copy them.\n\n'+
    ('Install Ubuntu is available from the app list. It checks these results before preparing the requested 150 GiB installation. Required failures remain a stop condition.' if media else 'Shut down normally and return to Windows. Failed, blocked and untested features still need attention.')])
