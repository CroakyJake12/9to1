# Home explicit shutdown source handoff

The current Home host keeps Core running when its visible window closes. Its async Exit handler cannot finish cleanup before the desktop dispatcher stops. This change adds explicit Ctrl+Shift+Q and cancellable desktop shutdown handling: seal new host work, await the original operations, dispose Core, then request desktop exit. Independent cleanup faults remain visible and return a nonzero process result.

Only four product files change. The existing A5 Core body is reused exactly; its close attempts all registered service stops and retains original failures. Ordinary visible-window close continues to preserve the shared Core lifetime.

Root, A5 and A6 have completed source review. All 54 test assertion calls remain, with acquisition and independent cleanup corrected in both real-Core test paths. Four new methods cover five cases. They have not been compiled or run.

Use review-receipt.json for the current publication state and exact selected pins. manifest.json and the complete inverses are preserved proposal-time provenance; their old RAM labels do not describe the subsequent source freeze. Runtime inverse selection is only the HomeCoreRuntime target; no other Linux platform sources are adopted. The fixture is new relative to the remote parent, while its corrective inverse refers to the earlier frozen source02 proposal.

Team C owns receiving integration and the Windows test/package carrier. Check the exact receiving predecessor bytes, run the full maintained Home native suite and the new cases, and exercise the actual published process. Record ordinary window-close persistence separately from explicit process shutdown. A forced kill, window title, source review or successful build does not close shutdown or full-app acceptance.

Baseline: 7ded080066ec4b066519c2fb1848fec5c66fba06
Source branch: team-a/home-windows-original-shutdown-20261004
