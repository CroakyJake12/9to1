#!/usr/bin/env python3
"""Control the native Samsung keyboard LED through UPower's public interface."""
from pathlib import Path
import re
import subprocess


def brightness(method, value=None):
    command = ['gdbus', 'call', '--system', '--dest', 'org.freedesktop.UPower',
               '--object-path', '/org/freedesktop/UPower/KbdBacklight',
               '--method', 'org.freedesktop.UPower.KbdBacklight.' + method]
    if value is not None:
        command.append(str(value))
    result = subprocess.run(command, text=True, capture_output=True, timeout=8, check=True)
    if value is None:
        match = re.fullmatch(r'\((?:int32 )?([0-9]+),\)', result.stdout.strip())
        if not match:
            raise ValueError('Unexpected keyboard brightness response')
        return int(match[1])


def main():
    led = Path('/sys/class/leds/samsung-galaxybook::kbd_backlight')
    if not led.is_dir():
        raise ValueError('The Samsung keyboard-light driver is not available in this session.')
    maximum = brightness('GetMaxBrightness')
    if maximum != int((led / 'max_brightness').read_text()) or maximum != 3:
        raise ValueError('The keyboard-light range does not match this laptop.')
    current = brightness('GetBrightness')
    if not 0 <= current <= maximum:
        raise ValueError('The current keyboard-light level could not be read.')
    choice = subprocess.run(['zenity', '--scale', '--title=Keyboard backlight',
                             '--text=Choose the keyboard light level. 0 turns it off.',
                             '--min-value=0', '--max-value=3', '--step=1',
                             '--value=' + str(current), '--ok-label=Apply'],
                            text=True, capture_output=True)
    if choice.returncode:
        return
    level = int(choice.stdout.strip())
    if not 0 <= level <= maximum:
        raise ValueError('Invalid keyboard-light level')
    brightness('SetBrightness', level)
    if brightness('GetBrightness') != level:
        raise ValueError('The keyboard driver did not confirm the selected level.')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        subprocess.run(['zenity', '--error', '--title=Keyboard backlight',
                        '--width=480', '--text=The keyboard light could not be changed.\n\n' + str(error)])
        raise SystemExit(1)
