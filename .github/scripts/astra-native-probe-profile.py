"""Prepare fresh original native-probe app data; no auth grant, launch or cleanup seal.

The actual owning caller must launch the probe as its direct child and preserve
original task/process/native/callback drains. Failure retains this private data.
Never archive this directory or delete it based only on a probe success marker.
"""
import hashlib
import json
import os
from pathlib import Path
import secrets
import sys
import tempfile

sys.dont_write_bytecode = True

def prepare():
    if sys.platform != 'linux':
        raise RuntimeError('Original fresh native profile producer requires Linux')
    original_root = Path(tempfile.mkdtemp(prefix='astra-native-probe-', dir='/tmp'))
    os.chmod(original_root, 0o700)
    children = {'XDG_DATA_HOME': 'data', 'XDG_CONFIG_HOME': 'config',
                'XDG_CACHE_HOME': 'cache', 'HAVEN_DATA_DIR': 'haven'}
    for name in children.values():
        (original_root / name).mkdir(mode=0o700)
        os.chmod(original_root / name, 0o700)
    nonce = secrets.token_hex(32)
    process = Path('/proc/self/stat').read_text()
    fields = process[process.rindex(')') + 1:].split()
    witness = {'SchemaVersion': 1, 'Root': str(original_root), 'Nonce': nonce,
               'ProducerPid': os.getpid(), 'ProducerStartTicks': fields[19]}
    encoded = (json.dumps(witness, sort_keys=True, separators=(',', ':')) + '\n').encode()
    witness_path = original_root / 'producer-witness.json'
    descriptor = os.open(witness_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, 'wb') as output:
        output.write(encoded)
    os.chmod(witness_path, 0o600)
    environment = {key: str(original_root / name) for key, name in children.items()}
    environment.update(ASTRA_NATIVE_PROBE_PROFILE_ROOT=str(original_root), ASTRA_NATIVE_PROBE_PROFILE_NONCE=nonce,
                       ASTRA_NATIVE_PROBE_PROFILE_WITNESS_SHA256=hashlib.sha256(encoded).hexdigest())
    return {'root': original_root, 'environment': environment, 'witness': witness,
            'witnessSha256': environment['ASTRA_NATIVE_PROBE_PROFILE_WITNESS_SHA256'],
            'qualification': 'Prepared data isolation only; no launch, UI, authority or original drain proof. Caller retains private profile on failure.'}
