# Third-party source and donor provenance

The first-party donor inventory records source roots, revisions and verification material. Review the entry for the exact feature being changed. Product wrappers preserve upstream notices alongside the owning project, and release packages must carry the required notices. [Donor inventory](../../eng/donor-sources.json) · [Distribution notices](../../THIRD_PARTY.md)

For the legacy HUI snapshot, retain the exact donor revision stored with the vendor tree. Use the broker cancellation record when tracing the separately pinned llama.cpp runtime. [Legacy HUI pin](../../9to1%20OS/HUI/vendor/.donor-revision) · [Broker cancellation evidence](../dulche/shared-runtime-cancellation.md)

- **UI framework.** CUI uses the maintained Avalonia source under `framework/CUI/vendor/Avalonia/`. The equivalence record binds the inspected fork to its input. Older HUI material retains its separate donor pin. [Avalonia verification](../../eng/donor-verification/avalonia-fork-equivalence.json)
- **Files.** The Windows donor under `9to1 Workspace/Files/Source/Files/`, the Linux native surface and the web client have different build and runtime evidence. Identify the actual owner and target before claiming donor parity.
- **Model runtimes.** Dulche’s llama.cpp path and Thunder’s NativeStrata integration have distinct source locks, bridges and runtime receipts. Preserve the relevant lock when replacing a donor. [llama.cpp lock](../../9to1%20Workspace/Home/Source/Dulche/llamacpp/upstream.lock.json) · [NativeStrata fork](../../9to1%20Models/Dulche%20Alpha/InferenceEngines/NativeStrata/MAINTAINED-FORK.md)
- **Other app donors.** For Canvas/Rnote, Boards/AppFlowy, office and media components, check the per-app registration, local modifications and consuming path before attributing behaviour to the upstream implementation.

Run the repository’s donor verification for the affected source. A successful inventory check records provenance; runtime and distribution checks have their own evidence. [Inventory verifier](../../eng/check-donor-sources.py)
