# Local `.cakeupdate` bundles

Settings exposes this channel as **Updates -> Install Update From File**. A bundle is a ZIP file with a `.cakeupdate` extension and exactly these entries:

- `manifest.json`
- one or more direct `packages/*.deb` files declared by the manifest

The manifest schema is version `1` and has only these declarative fields:

```json
{
  "schemaVersion": 1,
  "bundleId": "developer-update-001",
  "version": "1.2.3",
  "compatibility": {
    "minimumInstalledVersion": "1.0.0",
    "maximumInstalledVersion": "1.2.2",
    "architectures": ["amd64"]
  },
  "packages": [
    {
      "id": "cakeos-example",
      "version": "1.2.3",
      "architecture": "amd64",
      "path": "packages/cakeos-example_1.2.3_amd64.deb",
      "sha256": "lowercase-64-character-sha256",
      "sizeBytes": 1234,
      "dependsOn": []
    }
  ]
}
```

The validator rejects unknown manifest fields, archive traversal, non-regular entries, duplicate or undeclared packages, all content outside the two allowed locations, unsupported schema/version/architecture values, incomplete or cyclic in-bundle dependencies, and every package whose size or SHA-256 differs from the manifest. It does not extract the archive before validation.

No signing infrastructure exists in this repository, so this is explicitly a trusted local developer channel, not a release channel. The user must confirm a validated bundle in a separate action before `PkexecCakeUpdateInstaller` invokes only `/usr/libexec/cakeos/cakeos-local-update-installer` through `pkexec` with the bundle and expected bundle/manifest SHA-256 values.

That root helper is intentionally outside this UI/platform slice. It must independently repeat archive and package validation (to prevent time-of-check/time-of-use substitution), validate Debian dependencies before any package-manager change, install only the declared `.deb` files, and write final success or failure records through `CakeUpdateHistoryStore` at `/var/lib/cakeos/updates/history.v1.json`. It returns `0` only after recording success and `20` only after recording failure; any other exit means Settings attempts its fallback record and reports if it could not. This keeps a desktop process from assuming it can write system state. This repository does not claim to provide or test package installation, signing, or VM evidence.
