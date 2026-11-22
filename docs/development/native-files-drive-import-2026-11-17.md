# Import a Desktop OAuth client for native Files

Use an owner-approved Google Desktop OAuth client JSON file on the intended Ubuntu profile. The importer requires an absolute path to a private 0600 file. Keep its contents local and out of logs, chat and source control. The tool checks the record before updating the native client configuration.

After import, run the preflight under the same profile, then use the native Files connection flow. Importing a client registration does not sign in the user or demonstrate access to a Drive file.

```bash
python3 -B eng/development/native_files_drive_import.py --client-json "$DESKTOP_CLIENT_FILE"
python3 -B eng/development/native_files_drive_preflight.py --json
```

## Imported value and source lifetime

The supported Desktop OAuth file is local, bounded, owned by the current user and reached through a symlink-free path under that user’s home. Import uses an exclusive, non-overwriting write to the native public client ID file. It does not use a downloaded registration to grant scopes or store a refresh token. If a conflicting destination already exists, preserve both the input and existing configuration and inspect the reported outcome.

[Importer](../../eng/development/native_files_drive_import.py) · [Preflight](../../eng/development/native_files_drive_preflight.py)

## Dated delivery evidence

The 17 November 2026 record retains the original import restrictions, test results, incomplete native Drive workflow and private configuration custody. [Original record](evidence/files-delivery-before-atlas-v4/native-files-drive-import-2026-11-17.md)
