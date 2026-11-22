# Check that the native Files window closes normally

Start with the verified Linux x64 apphost and an empty, private scratch directory. This probe asks the actual native window to close and observes the process lifetime. Preserve a refusal, non-zero exit or timeout as its own result rather than relabelling forced termination as normal close.

Run the same-profile reopen check separately when testing durable state across launches. Record which executable, display and profile were used. A normal close does not demonstrate a completed cloud account or Drive workflow.

```sh
cd /absolute/path/to/9to1
SCRATCH="$(mktemp -d)"             # a brand-new owner-private mode-0700 directory
python3 -B eng/development/native_files_normal_close.py \
  --executable "/absolute/private/current-candidate/installed/Haven" \
  --scratch-parent "$SCRATCH" --timeout-seconds 45
```

[Normal-close helper](../../eng/development/native_files_normal_close.py) · [Helper tests](../../eng/development/test_native_files_normal_close.py) · [Same-profile reopen](native-files-profile-reopen-2026-11-18.md)

## Dated delivery evidence

The 18 November 2026 record retains the the original Linux normal-close checks and their limits, including earlier shutdown evidence. [Original record](evidence/files-delivery-before-atlas-v4/native-files-normal-close-2026-11-18.md)
