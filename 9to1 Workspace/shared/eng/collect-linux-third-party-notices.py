#!/usr/bin/env python3
"""Copy available restored-package notices and record exact declarations without fetching or inventing text."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET


def collect(assets_path: Path, destination: Path, repo_root: Path) -> dict:
    assets = json.loads(assets_path.read_text())
    destination.mkdir(parents=True, exist_ok=False)
    roots = [Path(folder) for folder in assets['packageFolders']]
    entries = []
    for identity, library in sorted(assets['libraries'].items()):
        if library.get('type') != 'package':
            continue
        name, version = identity.rsplit('/', 1)
        if not all(re.fullmatch(r'[A-Za-z0-9_.+\-]+', value) for value in (name, version)):
            raise ValueError('Unexpected package identity: ' + identity)
        relative = Path(library['path'])
        if relative.is_absolute() or '..' in relative.parts:
            raise ValueError('Package path must remain within its restored package root')
        candidates = [root / relative for root in roots if (root / relative).is_dir()]
        if not candidates:
            raise FileNotFoundError('Restored package unavailable: ' + identity)
        package = candidates[0]
        specs = list(package.glob('*.nuspec'))
        if len(specs) != 1:
            raise ValueError('Expected one package specification: ' + identity)
        metadata = ET.parse(specs[0]).getroot()
        declaration = next((item for item in metadata.iter() if item.tag.rsplit('}', 1)[-1] == 'license'), None)
        entry = {'package': name, 'version': version, 'restoredSha512': library.get('sha512'),
                 'licenseDeclaration': None if declaration is None else {'type': declaration.get('type'), 'value': declaration.text},
                 'copiedNotices': []}
        available = {item for item in package.iterdir() if item.is_file() and any(word in item.name.lower() for word in ('license', 'licence', 'notice', 'copying', 'copyright'))}
        if declaration is not None and declaration.get('type') == 'file':
            notice = package / (declaration.text or '')
            if not notice.resolve().is_relative_to(package.resolve()) or not notice.is_file():
                raise ValueError('Declared package license file is missing or escapes its package: ' + identity)
            available.add(notice)
        for notice in sorted(available):
            if not notice.resolve().is_relative_to(package.resolve()):
                raise ValueError('Notice escapes package: ' + identity)
            target = destination / 'packages' / name / version / notice.relative_to(package)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(notice, target)
            entry['copiedNotices'].append({'path': str(target.relative_to(destination)), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
        entries.append(entry)
    sources = []
    # These projects compile the vendored source; its exact existing notice text is retained.
    for relative in ['framework/CUI/vendor/Avalonia/licence.md', 'framework/CUI/vendor/Avalonia/NOTICE.md',
                     'framework/CUI/vendor/Avalonia/SOURCE-MANIFEST.txt',
                     'framework/CUI/vendor/Avalonia/external/XamlX/LICENSE',
                     'framework/CUI/vendor/Avalonia/external/Avalonia.DBus/LICENSE.md']:
        source = repo_root / relative
        target = destination / 'source' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        sources.append({'path': str(target.relative_to(destination)), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    result = {'basis': 'Actual restored dependency graph; may include build-only packages, not an attribution of every published binary.',
              'packages': entries, 'sourceNotices': sources,
              'packagesWithoutBundledNoticeText': [item['package'] + '/' + item['version'] for item in entries if not item['copiedNotices']],
              'limitations': ['License expressions are declarations, not substituted license text.',
                              'Native donor closure, source distribution obligations and complete attribution require separate source-backed verification.']}
    (destination / 'inventory.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repo-root', type=Path, required=True)
    arguments = parser.parse_args()
    result = collect(arguments.assets, arguments.output, arguments.repo_root)
    print(f"Copied available notices for {len(result['packages'])} restored packages; {len(result['packagesWithoutBundledNoticeText'])} packages have no bundled notice text. See inventory.json.")
