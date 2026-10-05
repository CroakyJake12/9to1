"""Build-time exact production source extraction; never a committed decoder implementation.

The owning console Program.cs also contains Main and infrastructure workflow tests.
Only its unchanged prefix containing the real reader is compiled for browser use.
Owner source changes require review and repinning; this generator fails closed.
"""
import argparse
import hashlib
import json
from pathlib import Path

SOURCE_SHA256 = '4cabd81a662c87df0cc8f70326099b5b358c182c184a6f0c0b60344bdaf77b6f'
PREFIX_SHA256 = '5426f61c5568b4728d3409d3dd3722dd4a70b23867b86168f0967da4b6a7125a'
MARKER = b'internal static class WaveConsoleSurface'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--manifest', required=True)
    args = parser.parse_args()
    source, output, manifest = map(Path, (args.source, args.output, args.manifest))
    raw = source.read_bytes()
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
