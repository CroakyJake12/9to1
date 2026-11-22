# Use the optional Files website launcher

The Ubuntu launcher opens the existing Files website using the owner’s browser. Run its check first to inspect local prerequisites. Launching the site uses its normal browser session. An install is a separate opt-in operation that adds desktop integration for that user.

Use this launcher when testing the web app. For native Files, follow the native candidate and profile procedure. The shortcut does not perform account enrolment, sync local folders or deliver a native package.

```bash
bash eng/development/files-web-launcher.sh --check
bash eng/development/files-web-launcher.sh
```

```bash
bash eng/development/files-web-launcher.sh --install
```

```bash
python3 -B -m unittest discover -s eng/development -p 'test_files_web_launcher.py' -v
bash -n eng/development/files-web-launcher.sh
```

[Website launcher](../../eng/development/files-web-launcher.sh) · [Native Files](native-files-owner-launch-2026-11-18.md)

## Dated delivery evidence

The 17 November 2026 record retains the launcher checks, installed integration scope and the limits of web verification. [Original record](evidence/files-delivery-before-atlas-v4/files-web-launcher-2026-11-17.md)
