# Assistants browser owner

This is the dedicated Assistants product surface. It uses the same Assistants Core and authored NativeUI CUI as the native product. It does not register a special Space or construct a second Den, Task store, model router or permission broker.

The current browser host lacks an authenticated Home Den owner. `BrowserAssistantsBootstrap` therefore displays the canonical Home scene with explicit setup status and refuses every action. CAKE identity and the browser's canonical Task read partition do not provide definition, conversation, model, resource or execution authority.

A trusted host must supply `IBrowserAssistantsCanonicalOwner` over its actual Home Den, conversation/Chat/Task and resource/model/Dev ports. The presentation must use the bridge's owner-issued conversation bindings and retain its actual controller, source tasks and native CUI owner before callbacks. Private context reset immediately hides old binding values and revokes real authority before externally joining the same presentation and acquisition originals. Presentation close does not cancel durable business work.

Draft close preparation is an admitted original operation: its full Task is retained before any dispatcher or native callback can run. Close joins those exact preparation Tasks before taking the source-task snapshot or joining the native owner. Final preparation readiness is checked on the actual UI thread; accepting a preparation does not constitute a successful retirement.

The focused tests are counterexamples over the actual unavailable binder and revoked binding fence. They do not establish login, installed Home startup, working AI, browser UI parity or deployment. No test or owning browser publish has run for this new source yet.

After the root grants the single heavy slot:

```sh
source /workspace/astra-coordination/fresh-shell-env.sh
cd /workspace/astra-c50-recovered
dotnet run --project apps/Web/Assistants/Tests/AssistantsBrowser.Tests.csproj -c Release --artifacts-path /workspace/astra-coordination/workers/home_web/assistants-browser-controls-01 -p:UseSharedCompilation=false -p:BuildInParallel=false
```

Owning publication remains `apps/Web/Tests/ci/run-ordinary-web-publish.py` after an exact clean source checkpoint. Its genuine wasm-tools/runtime packs, Bun frozen lock and owner-built Avalonia JS are required. Historic output URLs and source-only controls are not publication evidence.
