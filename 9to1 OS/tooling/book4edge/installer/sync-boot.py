#!/usr/bin/env python3
"""Refresh only the initrd belonging to the accepted kernel/DTB pair."""
import hashlib
import json
from pathlib import Path
import shutil
import sys

metadata = Path('/etc/book4edge/boot.json')
if not metadata.exists():
    raise SystemExit(0)
config = json.loads(metadata.read_text())
release = sys.argv[1]
if release != config['release']:
    # New distribution kernels do not silently replace this hardware-specific pair.
    raise SystemExit(0)
directory = Path('/boot/book4edge')/release
for name,key in [('Image','imageSHA256'),('board.dtb','dtbSHA256')]:
    with (directory/name).open('rb') as stream:
        if hashlib.file_digest(stream,'sha256').hexdigest() != config[key]:
            raise SystemExit('Pinned Book4 Edge boot payload changed: '+name)
source = Path('/boot')/('initrd.img-'+release)
if source.stat().st_size < 1024*1024:
    raise SystemExit('Generated initrd is unexpectedly small')
temporary = directory/'initrd.new'
shutil.copyfile(source,temporary)
temporary.replace(directory/'initrd')
