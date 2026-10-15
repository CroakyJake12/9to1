#!/usr/bin/env python3
"""Launch the guarded installer from the desktop, keeping account setup interactive."""
import subprocess

result = subprocess.run(['pkexec','/usr/bin/python3','/usr/lib/book4edge/installer/prepare.py'],
                        text=True,capture_output=True)
if result.returncode:
    subprocess.run(['zenity','--error','--no-markup','--title=Ubuntu installation',
                    '--width=600','--text='+(result.stderr.strip() or 'Installation was not prepared.')])
    raise SystemExit(result.returncode)
subprocess.run(['zenity','--info','--no-markup','--title=Ubuntu installation',
                '--width=600','--text='+result.stdout.strip()])
subprocess.run(['/snap/bin/ubuntu-desktop-bootstrap'],check=True)
