# Actual original native CUI caller-lifetime failure

The maintained CUI loader and native account bindings execute all three original controls. Sign-in view reset fails: CallbackCalls=1, CallerCancelled=True, AfterResetContinuation=0, PrivateEmpty=True. View read cancellation/true drain and explicit caller cancellation pass. The first sign-in failure prevents its later second-click assertion from running.

B6 independently replayed the same compiled native runtime after fresh extraction: 3 executed, 2 PASS, 1 meaningful FAIL, no skips; original runtime and wrapper exit 1. All 40 runtime inputs stayed byte-identical and the owned process group closed. This supports the exact CUI caller/view lifetime request. It uses a scripted lifecycle boundary, so it does not establish physical browser, OAuth/provider, permission or proposed canonical-port acceptance.

For replay, extract portable-original-runtime04.zip, then run its run-original.py with --dotnet pointing to an existing .NET 10 Linux x64 runtime and --log pointing to a fresh absolute log path. It verifies all runtime files before and after, makes no build/install/network calls and preserves the expected failing exit 1. The source-packet contains original source, command, source/binary pins and producer build identity.

Version 03 is preserved as historical packaging evidence: its authored wrapper could return success with unexpected counts and skipped after-hashing on timeout. Version 04 fixes those wrapper defects while retaining the exact original runtime and product assertions. The two-file canonical proposal in the parent request remains unapplied and requires Team A’s exact ownership ACK or maintained successor.
