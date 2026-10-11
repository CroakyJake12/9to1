"""Build-time exact production source extraction; never a committed decoder implementation.

The owning console Program.cs also contains Main and infrastructure workflow tests.
Only its unchanged prefix containing the real reader is compiled for browser use.
Owner source changes require review and repinning; this generator fails closed.
"""
import argparse
import hashlib
import json
from pathlib import Path

SOURCE_SHA256 = 'dcb0783470d9b5fc61ff63a5124e62b6f36080e96344d68cc918d9fb212a19e1'
PREFIX_SHA256 = '1fbace8bb954790be48cc05ccc8632a0b1efcec4bd42a8d1fdf35667038e0c4a'
MARKER = b'internal static class WaveConsoleSurface'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--manifest', required=True)
    args = parser.parse_args()
    source, output, manifest = map(Path, (args.source, args.output, args.manifest))
    raw = source.read_bytes()
    # Pins record the canonical LF source blob; a CRLF checkout is not a content change.
    raw = raw.replace(b'\r\n', b'\n')
    actual = hashlib.sha256(raw).hexdigest()
    if actual != SOURCE_SHA256 or raw.count(MARKER) != 1:
        raise SystemExit('Owner Wave source changed or marker is ambiguous; review required before browser engine compilation.')
    prefix = raw[:raw.index(MARKER)]
    if hashlib.sha256(prefix).hexdigest() != PREFIX_SHA256:
        raise SystemExit('Owner Wave decoder prefix differs from the reviewed production source.')
    # Preserve compiler diagnostics at the actual owning source, including original line numbers.
    source_name = str(source.resolve()).replace('\\', '/').replace('"', '\\"')
    generated = ('// Generated exact owner prefix; see owner-decoder-manifest.json.\n'
                 '#nullable enable\n'
                 f'#line 1 "{source_name}"\n').encode() + prefix
    output.parent.mkdir(parents=True, exist_ok=True)
    if not output.exists() or output.read_bytes() != generated:
        output.write_bytes(generated)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps({
        'source': str(source.resolve()), 'sourceSha256': actual,
        'marker': MARKER.decode(), 'prefixBytes': len(prefix), 'prefixLines': prefix.count(b'\n'),
        'prefixSha256': PREFIX_SHA256, 'generated': str(output.resolve()),
        'generatedSha256': hashlib.sha256(generated).hexdigest(),
        'transformation': 'Exact bytes before unique marker; only generated notice, nullable context and #line provenance added.',
        'authority': 'Unchanged owning Wave source; no independent browser decoder or project model.'
    }, indent=2) + '\n')


if __name__ == '__main__':
    main()
