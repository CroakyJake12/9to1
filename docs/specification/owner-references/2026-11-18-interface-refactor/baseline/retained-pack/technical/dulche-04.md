# Dulche runtime, requests and execution services — Dulche multi-engine inference runtime; Runtime abstraction and dispatcher (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2248 · v2 lines 13268–13270

Pass 4: Pre-Release
Dulche multi-engine inference runtime
Pass 4 MUST add a Dulche-owned inference-runtime abstraction so model definitions and higher-level Dulche features do not depend directly on one execution engine. llama.cpp remains the broad/default local backend. Dulche MUST also include an upstream-trackable Strata fork as a specialised backend for model architectures where Strata is supported and materially better suited.

### V2-B2249 · v2 lines 13273–13274

Runtime abstraction and dispatcher
Dulche MUST expose one stable internal runtime contract for model loading, prompt/prefill, decode, context/KV handling, multimodal inputs where supported, cancellation, streaming, usage/telemetry, errors and shutdown. Engine-specific behaviour belongs behind adapters. Agents, Spaces, Plugins, Skills and other Dulche callers MUST target the Dulche runtime contract rather than importing llama.cpp- or Strata-specific APIs.

### V2-B2250 · v2 lines 13277–13277

A Runtime Dispatcher MUST select the effective inference engine per model. Selection MUST be capability-based rather than a simple dense-versus-MoE rule. The compatibility registry MUST be able to consider model architecture/family, file/weight format, quantisation, required runtime features, OS, available VRAM, system RAM, CPU capabilities and any other hard backend requirement. A model manifest MAY declare a preferred engine and fallback engines, but the manifest MUST NOT make the model permanently dependent on one backend.

### V2-B2251 · v2 lines 13280–13281

Automatic selection
Automatic selection is the default. Dulche MUST first remove incompatible engines, then choose between compatible engines using declared capability fit and locally measured performance where useful. Calibration MAY benchmark compatible engines for a model/quantisation/hardware fingerprint and cache the result so repeated launches do not re-run expensive benchmarks. Prefill and decode MUST be measured separately where they have materially different performance characteristics.

### V2-B2252 · v2 lines 13284–13284

Automatic selection MUST NOT enable quality-reducing or behaviour-changing experimental flags merely to win a performance comparison. Runtime choice MUST remain inspectable through diagnostics/logging, including the selected engine and the reason it was selected.

### V2-B2253 · v2 lines 13287–13288

Manual engine selection
Users and callers MUST also be able to override automatic selection and swap the active inference engine explicitly. The public Dulche API MUST include the following forms:

### V2-B2254 · v2 lines 13291–13293

Dulche.setInferenceEngine(Llama.cpp);
Dulche.setInferenceEngine(Strata);
Dulche.getInferenceEngine();

### V2-B2255 · v2 lines 13296–13296

setInferenceEngine(...) establishes an explicit manual override for the applicable Dulche runtime scope and takes precedence over automatic engine selection while that override is active. getInferenceEngine() returns the effective engine currently selected for that scope. The UI/runtime settings MUST also make the effective engine and whether it was selected Automatically or Manually inspectable.

### V2-B2256 · v2 lines 13299–13299

A manual override MUST NOT be silently ignored. If the requested engine cannot run the selected model or current hardware configuration, Dulche MUST return a structured incompatibility result explaining the unmet requirement. It MUST NOT silently substitute another engine and pretend the requested engine was used. Returning to Automatic selection MUST be available through the corresponding runtime/settings control.

### V2-B2257 · v2 lines 13302–13303

Strata fork
Dulche MUST carry a maintained Strata fork rather than treating a separately installed Strata application as a required user dependency. The fork MUST preserve applicable upstream and third-party licence notices, keep model-weight licences separate, and remain intentionally close enough to upstream that security, correctness and performance improvements can be reviewed and merged without repeatedly re-forking the project.

### V2-B2258 · v2 lines 13306–13306

The Dulche Strata adapter MUST expose Strata through the same Dulche runtime contract used by llama.cpp. Strata-specific optimisations such as expert caching/residency, concurrent CPU/GPU expert execution, specialised prefill, MTP/speculative execution and model-specific memory placement MAY be used when supported, but they MUST remain implementation details behind the common runtime boundary.

### V2-B2259 · v2 lines 13309–13310

Fallback and failure behaviour
In Automatic mode, if the preferred engine fails to initialise or becomes invalid for the selected model/hardware, Dulche MAY try the next compatible registered engine and MUST record the fallback and reason. A fallback MUST preserve model identity and required capabilities; Dulche MUST NOT silently load a different model merely to make an engine work.

### V2-B2260 · v2 lines 13313–13313

In Manual mode, automatic cross-engine fallback is disabled unless the caller/user explicitly permits it after the failure. Engine crashes, load failures, out-of-memory conditions and unsupported-feature errors MUST surface through the normal structured Dulche error model and MUST leave the runtime in a recoverable state.

### V2-B2261 · v2 lines 13316–13317

Pre-release acceptance
Pass 4 is not complete for this requirement until llama.cpp and the Dulche Strata fork can coexist in one shipping Dulche installation; compatible models can be routed automatically; the three public set/get calls above work; incompatible manual selections fail clearly; automatic fallback is tested; engine choice is inspectable; benchmark/calibration state survives normal relaunch where applicable; and switching engines does not break higher-level Dulche callers, model identity, permissions, context ownership or persisted conversation/run state.

### COMPANION-B0084 · companion lines 555–557

SPUI-194 — DULCHE SHARED CONTEXT RUNTIME (.UCML), TRUST BOUNDARY AND CRYPTO CORRECTION
Create the proposed shared context storage/runtime with versioned “.UCML” (User Context Markdown Language) representation, where appropriate structured metadata accompanies Markdown-compatible human-readable content, including source, scope, owner, consent, relevant identities/revisions, expiry/retention, security classification and permissions. Use the existing Dulche Den/Memory/Spaces context owners rather than inventing incompatible duplicate history or bypassing explicit memory policy. UCML is a storage/interchange format, NOT by itself a cryptographic protection. Enforce per-user encrypted storage, authenticated authorised access and appropriate separation for conversation/Space/personal/team/organisation contexts, including local/remote and cross-device boundaries. Daily user-scoped rotation of a DERIVED DATA-ACCESS/ENVELOPE KEY or equivalent sound cryptographic design MAY fulfil the owner's desired daily refreshed unique key, with secure retention of the ability to decrypt legitimately retained older records; never rotate destructively or imply every user gets the same secret.
CRITICAL OWNER-APPROVED SECURITY CORRECTION: MODELS MUST NEVER BE GIVEN, DISPLAYED OR INJECTED A MASTER KEY, DECRYPTION KEY, CREDENTIAL OR RAW AUTHENTICATION SECRET. A model-visible daily key plus “REDACTED if leaked” is NOT acceptable. Hold all key material ONLY inside authenticated trusted key management (OS keystore/HSM/secure Dulche service as suitable). Expose a purpose-built, scoped Dulche Context Broker, accessible through an authenticated dedicated MCP/interface where appropriate. A closed-source bundled MCP may be used as the owner's deployment choice, but CLOSED SOURCE DOES NOT CONSTITUTE the security boundary. Requests require caller identity, explicit current permission, tenant/user/Space scope, task-purpose compatibility and limited retrieval; model receives only least-privilege approved relevant CONTEXT, not any key that allows decryption. Opaque capability/handle references may be passed where safe, but must not be reusable bearer secrets the model can leak. Protect integrity, logging/redaction, revocation, key-loss recovery and encryption-state migration.
