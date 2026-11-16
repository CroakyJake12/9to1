#!/usr/bin/env python3
"""Read-only sanity checks for installed Qwen/OpenCode revision materials."""
import json
import hashlib
from pathlib import Path
import sys

repo = Path(__file__).resolve().parents[1]
errors = []
try:
    config = json.loads((repo / '.opencode/opencode.json').read_text(encoding='utf-8'))
    assert '.opencode/study/always-on.md' in config.get('instructions', [])
    assert isinstance(config.get('model'), str)
    assert isinstance(config['agents']['build'], dict)
except (ValueError, OSError, KeyError, AssertionError) as err:
    errors.append(f'Existing OpenCode config not linked correctly: {err}')
try:
    state = json.loads((repo / 'study/state/learning_state.json').read_text(encoding='utf-8'))
    assert state['schema'] == '9to1.revision-learning-state/v1'
except (ValueError, OSError, KeyError, AssertionError) as err:
    errors.append(f'Learner state missing/invalid: {err}')
try:
    index = json.loads((repo / 'study/materials/SOURCE_INDEX.json').read_text(encoding='utf-8'))
    for source in index['files']:
        path = repo / source['path']
        if not path.is_file():
            errors.append(f'Missing material: {source["path"]}')
        elif hashlib.sha256(path.read_bytes()).hexdigest() != source['sha256']:
            errors.append(f'Material differs from bundled export (update source index if intentional): {source["path"]}')
except (ValueError, OSError, KeyError) as err:
    errors.append(f'Source index missing/invalid: {err}')

if errors:
    for error in errors:
        print('FAIL:', error)
    sys.exit(1)
print('PASS: OpenCode instruction link, learner-state schema and all source-file SHA256 hashes.')
print('NOTE: This does not test model inference, installed 9to1, live Boards, or OCR marking accuracy.')
