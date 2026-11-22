# Run the sealed Files web app locally

This procedure exercises the existing web application and its local Worker route. Begin with a previously verified WebAssembly publish directory. The launcher supports a sealed wwwroot argument and uses the existing candidate harness. A local journey can test navigation and service responses while keeping deployment separate.

The native desktop Files destination belongs to Haven. Select the corresponding launcher and target when testing its UI, profile or Drive workflow. Keep earlier hosted deployments associated with their recorded commit and timestamp. A browser wrapper is a different entry point from the native destination.

```bash
cloud/files-drive/dev/run-local.sh "$SEALED_WWWROOT"
```

[Local launcher](../../cloud/files-drive/dev/run-local.sh) · [Local journey](../../cloud/files-drive/dev/local-candidate.mjs) · [Verify a web candidate](files-candidate-local-release-2026-11-17.md) · [Launch native Files](native-files-owner-launch-2026-11-18.md)

## Dated delivery evidence

The 17 November 2026 record retains the source commits, issue #27 acknowledgement, Actions run 44095155303, wwwroot and receipt hashes, and the later native Files correction. [Original record](evidence/files-delivery-before-atlas-v4/files-local-candidate-2026-11-17.md) · [Native Files owner correction](../specification/owner-references/2026-11-17-native-files-drive/owner-correction.json)
