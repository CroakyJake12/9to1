"""Desktop .exe chooser. No program runs until a backend is selected."""
from __future__ import annotations

import argparse
import getpass
import hashlib
import json
import os
import shutil
import sys
import threading
from pathlib import Path

from . import winboat
from .broker import CompatibilityBroker, CompatibilityError
from .manifest import AppManifest, MountGrant
from .runtime import pe_arch, runtime_metadata


def config_path() -> Path:
    return Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config")) / "9to1" / "compat.json"


def read_config() -> dict:
    path = config_path()
    if not path.exists():
        return {}
    value = json.loads(path.read_text())
    if not isinstance(value, dict) or set(value) - {"wineRuntime", "winboat"}:
        raise ValueError("Invalid 9to1 compatibility configuration")
    return value


def wine_manifest(executable: Path, runtime: str) -> AppManifest:
    identity = hashlib.sha256(os.fsencode(executable)).hexdigest()[:24]
    title = "".join(c for c in executable.name if ord(c) >= 32)[:120]
    return AppManifest.from_dict(AppManifest(
        app_id="exe-" + identity, display_name=title, backend="wine", runtime=runtime,
        entrypoint=r"Z:\mnt\haven-share\program.exe",
        mounts=(MountGrant(str(executable), "/mnt/haven-share/program.exe", "ro"),),
    ).to_dict())


def choices(executable: Path, config: dict, broker: CompatibilityBroker) -> dict:
    architecture = pe_arch(executable)
    available = []
    reasons = []
    if broker.runtime_root.is_dir():
        for directory in sorted(broker.runtime_root.iterdir()):
            if not directory.is_dir() or not os.access(directory / "bin" / "wine", os.X_OK):
                continue
            try:
                metadata = runtime_metadata(directory)
                if architecture in metadata["windowsArchitectures"]:
                    available.append(directory.name)
            except (ValueError, OSError) as exc:
                reasons.append(str(exc))
    selected = config.get("wineRuntime")
    if selected is None and available:
        selected = available[0]
    wine_reason = "Runs in a separate Wine environment with access to this file only."
    wine_ready = selected in available
    if not wine_ready:
        wine_reason = reasons[0] if reasons else f"Install a managed Wine runtime that supports {architecture} applications."
    elif not shutil.which("bwrap"):
        wine_ready, wine_reason = False, "Install Bubblewrap to use the Wine layer."
    elif not os.environ.get("WAYLAND_DISPLAY"):
        wine_ready, wine_reason = False, "Sign in to an Ubuntu Wayland desktop session to use the Wine layer."
    connection = config.get("winboat")
    boat_ready = False
    boat_reason = "Set up a WinBoat Windows guest and configure its connection first."
    if connection is not None:
        connection = winboat.validate_connection(connection)
        boat_ready = bool(shutil.which("xfreerdp3") and os.environ.get("DISPLAY"))
        boat_reason = (f"Sends a copy of this file to your Windows guest at {connection['host']}."
                       if boat_ready else "Install freerdp3-x11 and use a desktop session with XWayland.")
    return {"architecture": architecture,
            "wine": {"available": wine_ready, "runtime": selected, "reason": wine_reason},
            "winboat": {"available": boat_ready, "reason": boat_reason}}


def execute(backend: str, executable: Path, config: dict, broker: CompatibilityBroker,
            options: dict, password: str = "") -> None:
    if not options[backend]["available"]:
        raise ValueError(options[backend]["reason"])
    if backend == "wine":
        manifest = wine_manifest(executable, options["wine"]["runtime"])
        broker.register_app(manifest)
        broker.launch(manifest)
    else:
        winboat.launch(config["winboat"], executable, password)


def show_dialog(executable: Path, config: dict, broker: CompatibilityBroker, options: dict) -> int:
    try:
        import gi
        gi.require_version("Gtk", "4.0")
        from gi.repository import Gio, GLib, Gtk
    except ImportError as exc:
        raise ValueError("Install python3-gi and gir1.2-gtk-4.0 for the EXE chooser") from exc

    app = Gtk.Application(application_id="org.ninetone.ExeChooser", flags=Gio.ApplicationFlags.NON_UNIQUE)

    def activate(application):
        window = Gtk.ApplicationWindow(application=application, title="Open Windows application")
        window.set_default_size(540, 320)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        for edge in ("top", "bottom", "start", "end"):
            getattr(box, "set_margin_" + edge)(24)
        window.set_child(box)
        title = Gtk.Label(label=executable.name, wrap=True, xalign=0)
        title.add_css_class("title-2")
        box.append(title)
        box.append(Gtk.Label(label="How would you like to open this application?", xalign=0))
        error = Gtk.Label(wrap=True, xalign=0)
        buttons = []

        def complete(message):
            if message:
                error.set_text(message)
                for backend, button in buttons:
                    button.set_sensitive(options[backend]["available"])
            else:
                window.close()
            return False

        def start(backend, password=""):
            for _, button in buttons:
                button.set_sensitive(False)
            error.set_text("Connecting…" if backend == "winboat" else "Starting application…")
            def worker():
                try:
                    execute(backend, executable, config, broker, options, password)
                    message = ""
                except (ValueError, OSError, CompatibilityError) as exc:
                    message = str(exc)
                GLib.idle_add(complete, message)
            threading.Thread(target=worker, daemon=False).start()

        def clicked(_button, backend):
            if backend == "wine":
                start(backend)
                return
            dialog = Gtk.Dialog(title="Windows guest password", transient_for=window, modal=True)
            dialog.add_button("Cancel", Gtk.ResponseType.CANCEL)
            dialog.add_button("Connect", Gtk.ResponseType.OK)
            entry = Gtk.PasswordEntry()
            entry.set_show_peek_icon(True)
            dialog.get_content_area().append(entry)
            def response(widget, result):
                password = entry.get_text()
                widget.destroy()
                if result == Gtk.ResponseType.OK:
                    start(backend, password)
            dialog.connect("response", response)
            dialog.present()

        for backend, label in (("wine", "Wine OS layer"), ("winboat", "WinBoat OS layer")):
            button = Gtk.Button(label=label)
            button.set_sensitive(options[backend]["available"])
            button.connect("clicked", clicked, backend)
            box.append(button)
            box.append(Gtk.Label(label=options[backend]["reason"], wrap=True, xalign=0))
            buttons.append((backend, button))
        box.append(error)
        cancel = Gtk.Button(label="Cancel")
        cancel.connect("clicked", lambda _: window.close())
        box.append(cancel)
        window.present()

    app.connect("activate", activate)
    return app.run([])


def show_error(message: str) -> None:
    """Desktop launchers have no terminal; make early validation failures visible."""
    try:
        import gi
        gi.require_version("Gtk", "4.0")
        from gi.repository import Gio, Gtk
        app = Gtk.Application(application_id="org.ninetone.ExeChooser.Error", flags=Gio.ApplicationFlags.NON_UNIQUE)
        def activate(application):
            dialog = Gtk.MessageDialog(application=application, text="Unable to open Windows application",
                                       secondary_text=message, message_type=Gtk.MessageType.ERROR,
                                       buttons=Gtk.ButtonsType.CLOSE)
            dialog.connect("response", lambda widget, _: widget.destroy())
            dialog.present()
        app.connect("activate", activate)
        app.run([])
    except (ImportError, ValueError):
        pass  # The stderr message remains useful when GTK itself is unavailable.


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=("wine", "winboat"), help="Explicit CLI choice; default opens the dialog")
    parser.add_argument("--plan", action="store_true", help="Report choices without creating state or launching anything")
    parser.add_argument("executable", type=Path)
    args = parser.parse_args(argv)
    try:
        executable = args.executable.resolve(strict=True)
        if not executable.is_file() or executable.suffix.lower() != ".exe":
            raise ValueError("Select a local .exe file")
        config, broker = read_config(), CompatibilityBroker()
        options = choices(executable, config, broker)
        if args.plan:
            print(json.dumps(options, indent=2))
            return 0
        if args.backend:
            password = getpass.getpass("Windows guest password: ") if args.backend == "winboat" and options["winboat"]["available"] else ""
            execute(args.backend, executable, config, broker, options, password)
            return 0
        return show_dialog(executable, config, broker, options)
    except (ValueError, OSError, CompatibilityError) as exc:
        print(f"9to1: {exc}", file=sys.stderr)
        if not args.backend and not args.plan and (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
            show_error(str(exc))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
