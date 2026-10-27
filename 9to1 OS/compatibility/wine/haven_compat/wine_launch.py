"""Runs inside Bubblewrap; initializes Wayland within the private Wine prefix."""
import os
import subprocess
import sys


def main() -> None:
    wine = "/opt/haven-wine/bin/wine"
    subprocess.run([wine, "reg.exe", "add", r"HKCU\Software\Wine\Drivers", "/v", "Graphics",
                    "/t", "REG_SZ", "/d", "wayland", "/f"], check=True)
    os.execv(wine, [wine, sys.argv[1]])


if __name__ == "__main__":
    main()
