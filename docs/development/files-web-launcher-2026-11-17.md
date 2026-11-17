# 9to1 Files — optional Ubuntu desktop shortcut

This is a **launcher for the existing, owner-authenticated HTTPS Files web app**, not another Files implementation, Google Drive client, synchronizer, or credentials store. It opens:

`https://9to1.uk/files/#/app.files`

The private Worker and canonical Files application remain the only storage/identity paths. The launcher never talks to Google APIs or stores an account token. Sign in using the normal 9to1 Account flow; connect Google Drive in the actual Files app when needed. Cloudflare Access or an incomplete Google OAuth configuration can still prevent using Files. Compiling or launching the shortcut is not evidence that a live Google Drive session works.

## Try it without installing

From a checkout:

```bash
bash eng/development/files-web-launcher.sh --check
bash eng/development/files-web-launcher.sh
```

The launcher chooses the first available Chromium-family browser (Chrome, Chromium, Brave) and requests a minimal app-style window. If none is found, it opens the URL in your default browser through `xdg-open`. It does not bypass the browser sandbox or alter browser profiles. If redirected to sign-in, complete the normal login in your browser. The shortcut can also be placed as a bookmark instead.

## Optional local desktop integration

Run this manually on your own Ubuntu desktop, not on the cloud worker or production server:

```bash
bash eng/development/files-web-launcher.sh --install
```

This creates **only** `~/.local/bin/9to1-files-web` and `~/.local/share/applications/9to1-files-web.desktop`, without root access. It deliberately refuses to overwrite either path, including a symlink. It does **not** install a browser, change the OS default handler or modify your data. Remove these two files yourself to uninstall the optional shortcut.

Do not modify the Fixed HTTPS URL to a localhost/test origin and treat that as real Drive access: the real OAuth callback, authentication and Worker service are specifically tied to `https://9to1.uk`. For CI or synthetic-provider acceptance, continue to use the repository's dedicated non-deploying browser tests instead.

## Verification

```bash
python3 -B -m unittest discover -s eng/development -p 'test_files_web_launcher.py' -v
bash -n eng/development/files-web-launcher.sh
```

The seven tests use temporary home directories and executable browser shims. They assert the exact app URL and argv, no interaction in check mode, explicit installation, refusal to overwrite including symlink protection, and rejection of unsafe paths and unknown options. They do not authenticate to 9to1, Google or Cloudflare, and do not claim native desktop or real-Drive acceptance. The repository's existing Files WebAssembly and Cloudflare Worker remain unchanged by this utility.
