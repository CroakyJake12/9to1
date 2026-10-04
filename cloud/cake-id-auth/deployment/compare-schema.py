"""Reproduce metadata-only D1 migration comparison using local SQLite."""
import json
import pathlib
import re
import sqlite3

root = pathlib.Path(__file__).resolve().parent.parent
actual = json.loads((root / 'deployment/evidence/actual-schema-settings.json').read_text())
expected_db = sqlite3.connect(':memory:')
for migration in sorted((root / 'migrations').glob('*.sql')):
    expected_db.executescript(migration.read_text())
expected = {r[1]: dict(zip(('type', 'name', 'tbl_name', 'sql'), r)) for r in expected_db.execute('SELECT type,name,tbl_name,sql FROM sqlite_master WHERE sql IS NOT NULL')}
observed = {r['name']: r for r in actual['schema']['result'][0]['results']}
assert set(observed) - set(expected) == {'_cf_KV'}
assert not set(expected) - set(observed)
normalize = lambda value: re.sub(r'\s+', ' ', value.strip()).lower()
actual_db = sqlite3.connect(':memory:')
for item in observed.values():
    if item['type'] == 'table' and item['name'] != 'sqlite_sequence':
        actual_db.execute(item['sql'])
for item in observed.values():
    if item['type'] == 'index':
        actual_db.execute(item['sql'])
for name, item in expected.items():
    for field in ('type', 'tbl_name', 'sql'):
        assert normalize(item[field]) == normalize(observed[name][field]), (name, field)
    if item['type'] == 'table' and name != 'sqlite_sequence':
        query = f'PRAGMA foreign_key_list("{name}")'
        assert expected_db.execute(query).fetchall() == actual_db.execute(query).fetchall(), name
assert json.loads((root / 'deployment/evidence/actual-foreign-key-mode.json').read_text())['result'][0]['results'] == [{'foreign_keys': 1}]
print('PASS: 35 maintained schema objects and parsed foreign keys match actual D1 metadata; provider foreign_keys=1; no provider writes')
