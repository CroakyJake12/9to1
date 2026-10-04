"""Serve one original signed public CRL on isolated loopback for disposable TLS tests."""
import argparse, hashlib, http.server, json, os, pathlib, re, stat

p = argparse.ArgumentParser()
p.add_argument('--port', type=int, required=True)
p.add_argument('--crl', required=True)
p.add_argument('--sha256', required=True)
p.add_argument('--request-path', required=True)
p.add_argument('--receipt', required=True)
a = p.parse_args()
assert 1 <= a.port <= 65535 and re.fullmatch('[0-9a-f]{64}', a.sha256)
assert re.fullmatch(r'/astra-crl-[0-9a-f]{32}\.der', a.request_path)
crl = pathlib.Path(a.crl)
receipt = pathlib.Path(a.receipt)
assert not crl.is_symlink() and crl.is_file() and not receipt.exists() and not receipt.is_symlink()
original = crl.lstat()
assert stat.S_ISREG(original.st_mode) and 0 < original.st_size <= 1024 * 1024
data = crl.read_bytes()
assert hashlib.sha256(data).hexdigest() == a.sha256
record = {'host': '127.0.0.1', 'port': a.port, 'pid': os.getpid(), 'requestPath': a.request_path,
          'originalCrlSha256': a.sha256, 'successfulGetRequests': 0}


def retain_count():
    temporary = receipt.with_name(receipt.name + '.new')
    assert not temporary.exists() and not temporary.is_symlink()
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as output:
        output.write(json.dumps(record) + '\n')
        output.flush()
        os.fsync(output.fileno())
    os.replace(temporary, receipt)


class OriginalCrlHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != a.request_path:
            self.send_error(404)
            return
        current = crl.lstat()
        assert not crl.is_symlink() and (current.st_dev, current.st_ino, current.st_mode, current.st_size) == (
            original.st_dev, original.st_ino, original.st_mode, original.st_size)
        assert hashlib.sha256(crl.read_bytes()).hexdigest() == a.sha256
        self.send_response(200)
        self.send_header('Content-Type', 'application/pkix-crl')
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(data)
        record['successfulGetRequests'] += 1
        retain_count()

    def log_message(self, format, *args):
        pass


with http.server.HTTPServer(('127.0.0.1', a.port), OriginalCrlHandler) as server:
    retain_count()
    server.serve_forever()
