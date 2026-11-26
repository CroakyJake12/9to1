#!/usr/bin/python3
"""Read or explicitly acquire/release a local Dulche training window. Defaults to status."""

import argparse
import json
import os
from pathlib import Path

from dulchectl import LocalHTTP


def request(action="status", owner=None, models=None, socket_path=None):
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    con = LocalHTTP(os.fspath(socket_path or runtime / "9to1-dulche/api.sock"), 30)
    path = "/v1/resources/training-window"
    try:
        if action == "status":
            from urllib.parse import urlencode

            con.request(
                "GET", path + ("?" + urlencode({"owner": owner}) if owner else "")
            )
        else:
            if action not in ("acquire", "release"):
                raise ValueError("Unknown training window operation")
            data = {"owner": owner}
            if action == "acquire":
                data["models"] = models
            con.request(
                "POST",
                path + "/" + action,
                json.dumps(data),
                {"Content-Type": "application/json"},
            )
        r = con.getresponse()
        body = json.load(r)
        if r.status >= 400:
            raise RuntimeError(
                f"Dulche HTTP {r.status}: {body.get('error', {}).get('message', 'request rejected')}"
            )
        return body
    finally:
        con.close()


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        "action", choices=("status", "acquire", "release"), default="status", nargs="?"
    )
    p.add_argument("--owner")
    p.add_argument("--model", action="append", dest="models")
    p.add_argument("--socket")
    a = p.parse_args()
    try:
        print(json.dumps(request(a.action, a.owner, a.models, a.socket), indent=2))
    except (RuntimeError, ValueError, OSError) as exc:
        p.exit(1, str(exc) + "\n")
