"""RemoteApp adapter for an explicitly configured, already running WinBoat guest.

This is separate from the broker's sandboxed provider: it shares one copied EXE
with the chosen Windows guest over RDP. It does not provision or start a VM.
"""
from __future__ import annotations

import re
import shutil
import subprocess
import tempfile
from pathlib import Path


def validate_connection(value: dict) -> dict:
    if not isinstance(value, dict) or set(value) != {"host", "port", "username"}:
        raise ValueError("WinBoat connection needs host, port and username")
    host, port, username = value["host"], value["port"], value["username"]
    if not isinstance(host, str) or not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?", host):
        raise ValueError("WinBoat host must be an IPv4 address or DNS hostname")
    if type(port) is not int or not 1 <= port <= 65535:
        raise ValueError("Invalid WinBoat RDP port")
    if not isinstance(username, str) or not username or len(username) > 256 or any(ord(c) < 32 for c in username):
        raise ValueError("Invalid WinBoat username")
    return dict(value)


def rdp_command(connection: dict, share: Path, binary: str) -> list[str]:
    connection = validate_connection(connection)
    if "," in str(share):
        raise ValueError("RDP staging directory cannot contain a comma")
    return [binary, f"/v:{connection['host']}:{connection['port']}", f"/u:{connection['username']}",
            "/from-stdin:force", "/cert:tofu", "/dynamic-resolution", "-clipboard",
            f"/drive:NineToOne,{share}", r"/app:program:\\tsclient\NineToOne\program.exe"]


def launch(connection: dict, executable: Path, password: str) -> None:
    binary = shutil.which("xfreerdp3")
    if not binary:
        raise ValueError("Install freerdp3-x11 to connect to a WinBoat guest")
    if any(character in password for character in ("\n", "\r", "\0")):
        raise ValueError("Password cannot contain line breaks or NUL")
    # Never expose the original parent directory, the user's home, or a raw disk.
    with tempfile.TemporaryDirectory(prefix="9to1-winboat-", dir="/tmp") as temporary:
        share = Path(temporary)
        shutil.copyfile(executable, share / "program.exe")
        result = subprocess.run(rdp_command(connection, share, binary), input=password + "\n",
                                text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                check=False)
        if result.returncode:
            raise ValueError("WinBoat connection failed. Check the guest, credentials, certificate and RemoteApp setup.")
