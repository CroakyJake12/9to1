# Smoke-test the native Files window on Linux

Use the executable from an admitted Linux x64 Haven build. Point the native smoke helper at that exact executable, a fresh private scratch parent and an explicit report location. The product path is through the real Haven window and Files startup route.

Keep the report’s revision, executable identity, display information, timeout and exit result together. Run a separate close/reopen probe for profile persistence. A window startup can succeed even when cloud consent, remote item access or installation remain unverified.

```bash
python3 -B eng/development/native_files_linux_smoke.py --executable "$HAVEN_APPHOST" --scratch-parent "$SCRATCH" --report "$REPORT"
```

[Smoke helper](../../eng/development/native_files_linux_smoke.py) · [Workflow](../../.github/workflows/native-files-linux-smoke.yml) · [Normal-close probe](native-files-normal-close-2026-11-18.md)

## Dated delivery evidence

The 18 November 2026 record retains the pinned graphical smoke workflow, its platform evidence and the recorded Drive gates. [Original record](evidence/files-delivery-before-atlas-v4/native-files-linux-smoke-2026-11-18.md)
