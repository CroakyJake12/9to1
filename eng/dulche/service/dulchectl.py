#!/usr/bin/env python3
"""Manage the local Dulche service, including emergency stop when the GUI is unavailable."""

import argparse
import http.client
import json
import os
from pathlib import Path
import socket
import sys


class LocalHTTP(http.client.HTTPConnection):
    def __init__(self, path, timeout):
        super().__init__("localhost", timeout=timeout)
        self.path = path

    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect(self.path)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    default = (
        Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
        / "9to1-dulche/api.sock"
    )
    p.add_argument("--socket", default=os.environ.get("DULCHE_SOCKET", str(default)))
    p.add_argument(
        "--idempotency-key", help="Stable operation identifier for resubmission"
    )
    p.add_argument("--timeout", type=float, default=900)
    sub = p.add_subparsers(dest="action", required=True)
    sub.add_parser("status")
    sub.add_parser("stop-all")
    sub.add_parser("allow-new")
    sub.add_parser("stop").add_argument("id")
    sub.add_parser("resume").add_argument("id")
    api = sub.add_parser("api")
    api.add_argument("method", choices=["GET", "POST"])
    api.add_argument("path")
    api.add_argument(
        "json", nargs="?", default="{}", help="JSON object or '-' for standard input"
    )
    a = p.parse_args()
    method = "POST"
    body = {}
    if a.action == "api":
        path = a.path
        method = a.method
        body = json.loads(sys.stdin.read() if a.json == "-" else a.json)
    elif a.action == "status":
        method = "GET"
        path = "/v1/status"
    elif a.action in ("stop-all", "allow-new"):
        path = "/v1/agents/" + a.action
    else:
        path = f"/v1/agents/{a.id}/{a.action}"
    conn = LocalHTTP(a.socket, a.timeout)
    try:
        headers = {"Content-Type": "application/json"}
        if a.idempotency_key:
            headers["Idempotency-Key"] = a.idempotency_key
        conn.request(
            method, path, json.dumps(body) if method == "POST" else None, headers
        )
        r = conn.getresponse()
        if r.getheader("Content-Type", "").startswith("text/event-stream"):
            for line in r:
                sys.stdout.buffer.write(line)
                sys.stdout.buffer.flush()
        else:
            print(json.dumps(json.load(r), indent=2, ensure_ascii=False))
        return int(r.status >= 400)
    finally:
        conn.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, http.client.HTTPException) as e:
        sys.exit(f"Dulche unavailable: {e}")
