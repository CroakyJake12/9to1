# Models, discovery and universal model routing — Discover; Universal Model Picker (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0441 · v2 lines 2256–2256

Discover

### V2-B0442 · v2 lines 2259–2259

9-1 Discover is the model browser/discovery surface inside Home. It MUST aggregate model/catalogue information from configured providers rather than depend on one catalogue. Search, filter and comparison MUST support model identity, provider/origin, modality, capabilities, architecture/parameter information when known, context limits, quantisation/format where known, local/cloud status, hardware-fit estimate, install/connection state, entitlement/cost class and privacy/residency information.

### V2-B0443 · v2 lines 2262–2262

Discover MUST treat Chat, Image and Voice as first-class model categories. Advanced discovery additionally exposes Audio and Video models. Voice MUST NOT be buried under Audio: conversational/live voice is a primary model category because it has distinct runtime, quality and capability requirements.

### V2-B0444 · v2 lines 2265–2265

Voice discovery MUST expose capability metadata such as Conversational compatibility, Monologue compatibility, Live Listener compatibility, Live Translate compatibility, visual understanding, tool/action support, streaming, interruption/turn-taking support, supported languages/accents and translation language pairs when known, one-way/bidirectional/multi-language live interpretation support, local/cloud execution, latency characteristics, privacy/residency and entitlement/cost.

### V2-B0445 · v2 lines 2268–2268

Discover MUST recommend the highest-quality compatible Voice models/configurations for the user's required mode and constraints. Recommendation dimensions MAY include reasoning quality, speech naturalness, speech-recognition accuracy, expressiveness, instruction following, turn-taking quality, interruption handling, long-form coherence, audio-context understanding, visual capability, tool reliability and latency. Unknown measurements remain Unknown rather than being invented.

### V2-B0446 · v2 lines 2271–2271

Voice recommendations MUST be contextual rather than a single universal ranking. Discover may surface labels such as Highest quality, Best for Conversational, Best for Monologue, Best for Live Listener, Best for Live Translate, Best local and Fastest when supported by actual capability/evaluation data. Each recommendation MUST explain the relevant reasons/constraints rather than presenting an unexplained badge. Live Translate recommendations should consider speech-recognition accuracy, translation quality, output-speech quality, latency, turn detection, code-switching and multi-language support where measured.

### V2-B0447 · v2 lines 2274–2274

Discover MAY recommend either a native realtime Voice Model or, where 9to1 supports it, a compatible composite voice configuration. Composite implementation detail is secondary to the normal user-facing Voice selection and MUST NOT force ordinary users to manually assemble STT/reasoning/TTS pipelines.

### V2-B0448 · v2 lines 2277–2277

When a selected Voice Model exposes multiple named voices, Discover MUST show those voices as selectable variants with the underlying Voice Model as secondary metadata. Voice-quality/model recommendations and named-voice selection are distinct concepts.

### V2-B0449 · v2 lines 2280–2280

A model listing MUST preserve stable provider-scoped identity even when multiple providers expose the same human-readable model name. Unknown metadata MUST remain unknown rather than being fabricated.

### V2-B0450 · v2 lines 2283–2283

Discover MUST support the relevant install/pull/connect/open-in-picker action for providers that expose those capabilities. Install/connect actions route through Dulche/provider/package APIs and the normal permission/account model.

### V2-B0451 · v2 lines 2286–2286

Universal Model Picker

### V2-B0452 · v2 lines 2289–2337

The Universal Model Picker is the single shared model/provider selection and routing interface for the 9to1 ecosystem. On Windows/regular 9to1 it is hosted by Home; on 9to1-OS the same canonical state is surfaced by the OS. Apps, Agents and AI surfaces MUST NOT create incompatible private model pickers, provider registries or fallback logic when this shared capability applies.
Compact picker and full manager
The compact picker is used inside apps, Agents and AI surfaces when selecting the effective model/route. The full manager lives in Home/Settings → AI/Models and adds provider configuration, installation/acquisition, aliases, defaults, route ordering, background routing, fallbacks, privacy, resource and lifecycle controls.
Entries expose stable Dulche model identity plus user-facing name/alias, provider/source, local/cloud/remote state, modalities/capabilities, tool/structured-output support, reasoning controls, context/output limits, loading/availability, privacy/residency, entitlement/cost where known and local RAM/VRAM fit where meaningful. Display names are never used as canonical identity.
Simple defaults and capability categories
The normal picker keeps the primary capability configuration approachable: Chat Model/Route, Image Model/Route and Voice Model/Voice. Advanced exposes Audio Model/Route and Video Model/Route. The user may instead choose one Active Model/Route and allow all compatible capabilities to inherit it, with ordered fallback resolving unsupported capabilities.
Chat is the primary general-purpose route for conversation, reasoning, writing, coding, tool use, agentic work and multimodal chat where supported.
Image supports an explicit Inherit from Active/Chat route. Inheritance means use that ordered route and its native image-generation/editing-capable candidates; it MUST NOT silently substitute an unrelated unconfigured image provider. A dedicated Image route is an explicit override.
Voice is the first-class conversational/live-voice selection used by Vision & Voice for supported Conversational, Monologue, Live Listener and Live Translate modes. When the selected Voice Model exposes named voices, the user-facing field is labelled Voice and selects VoiceID while showing VoiceModelID as secondary metadata. When there are no named variants the field remains Voice Model.
Advanced Audio covers dedicated transcription, synthesis, audio understanding/transformation or music/audio generation where applicable. Advanced Video covers video generation, editing/transformation or video-native understanding. These category routes may inherit the Active route or use explicit independent ordered routes.
Active and Background routes
Users may configure a single Active Model/Route for all foreground AI work or assign capability-specific/app/Agent overrides. Separately, a Background Model/Route can be configured for non-interactive work such as background summarisation, indexing/classification, Background Learning processing, permitted Automations AI work, proactive suggestions and other low-priority background tasks.
If no Background route is configured, background work inherits the Active route subject to normal capability/privacy/policy filtering. A task-specific explicit model outranks Agent/app/category defaults; a configured Background route applies only to eligible background work and MUST NOT override an explicit Agent/task requirement.
Ordered Model Routes
Home/Dulche MUST support versioned ordered Model Routes shared by regular 9to1 and 9to1-OS. A Model Route is an ordered list of stable configured model identities plus per-entry enabled/disabled state and routing policy. Users can reorder candidates directly in Home, including by drag/reorder UI, without changing the models' canonical identities.
Example: an Active route may be ChatGPT 6 Sol → Gemma 4 → Fable 5. The ordering means use the highest-priority candidate that is both eligible and available for the request. The router MUST evaluate compatibility before invocation rather than deliberately sending impossible requests to higher entries first.
Foreground and background workloads may use different ordered routes. A user can therefore choose a powerful cloud model first for interactive work while using a small local model first for background work. Users who want simplicity can select a single model and leave fallback entries empty.
Eligibility-first routing
For every routed request Dulche first derives task requirements such as modality/capability, tool use, structured output, context/output requirements, reasoning support and foreground/background role. It then filters candidates against provider/model capability, privacy/locality policy, administrator policy, connection/authentication permission, user fallback policy, entitlement and other hard constraints before considering route priority.
After filtering, Dulche selects the highest-priority available candidate. A lower candidate may be used automatically when a higher candidate is unavailable because of a classified routing condition such as usage/quota exhaustion, provider/model outage, unsupported required capability, context incompatibility, authentication/provider unavailability when another authorised option exists, or local resource insufficiency after permitted reclamation.
Capability mismatch should normally be detected before invocation. For example, if the first two models in a route cannot generate video and the third can, a video-generation request routes directly to the third eligible model rather than failing through the first two.
Conditions that are not model fallback
Model fallback is not a generic retry mechanism. Errors such as missing user files, invalid tool arguments, target-app permission denial, required human approval, business-logic rejection or an external service action failure do not justify silently switching models unless the failure is independently classified as a model/provider-routing condition.
Fallback privacy and control
Each route and candidate can restrict fallback, including no fallback, local-only fallback, cloud-allowed fallback or provider-specific constraints. A route containing cloud candidates does not itself authorise sensitive context to leave the device. Privacy, administrator and task policies are evaluated before route ordering.
A request explicitly pinned to a model with fallback disabled MUST fail transparently rather than silently substitute another model. Likewise a local-only request cannot fall through to cloud merely because the local model is unavailable.
Routing transparency
The effective model and routing reason remain inspectable. Home/Dulche records route identity, selected model and skipped/fallback reasons where relevant, for example 'Sol skipped: usage exhausted; Gemma selected' or 'Sol skipped: video-generation unsupported; Fable selected'. This metadata may be surfaced subtly in normal UI and in more detail through diagnostics/Action Graph without cluttering every response.
Selection inheritance and overrides
The configuration hierarchy MUST be deterministic: request-specific explicit selection → Agent/task override → app/surface override → capability/category route → Background route for eligible background work or Active route for foreground work → configured route fallbacks. A lower-level explicit override wins, while inherited state is visibly distinguishable from an explicit override.
LLM Space and Desktop Space overrides MAY exist but remain scoped. Category overrides are independent; for example an LLM Space may override Chat while inheriting global Image/Voice/Audio/Video routes.
Reasoning and capability validation
The picker renders only reasoning/effort levels actually exposed by the selected model/provider. It MUST NOT invent Low/Medium/High controls for a model that does not support them. Incompatible explicit selections are blocked or clearly explained unless an authorised fallback route can satisfy the requirement.
Aliases and model identity
Users can assign friendly aliases/nicknames to models. Aliases are presentation conveniences and may be duplicated; all routing objects store stable provider-scoped Dulche ModelID values so duplicate names never create ambiguous execution.
Local model lifecycle
Where supported, the full manager provides discover/install or pull, import, verify, download progress, load, unload, remove, update/replace, rollback, storage-location and compatible CPU/GPU/offload configuration. These operations use Dulche Runtime/Den and provider/package APIs rather than a second model-host implementation.
Provider support
Routes may contain compatible local runtimes, configured commercial/cloud providers, self-hosted/OpenAI-compatible endpoints, Ollama, another Dulche device, enterprise endpoints and authorised provider/Plugin implementations. Provider authentication and credentials remain owned by the appropriate shared credential/provider system.
Resource awareness
For local candidates the picker may show estimated RAM/VRAM requirement, current availability, loading state and reclaimable resources when known. On 9to1-OS this integrates with the AI-aware resource manager; on other Home platforms it uses the available Dulche/runtime resource information without pretending OS-level reclamation exists where it does not.
Administrator and managed-profile policy
Managed profiles may limit approved models/providers, acquisition, external cloud use, maximum cost/usage class or fallback destinations. Policy filtering happens before user ordering so reordering a blocked provider cannot bypass a School/organisation restriction.
Universal Model Picker acceptance requirements
* Regular 9to1/Home and 9to1-OS use one canonical Dulche model catalogue and versioned routing contract.
* Users can use one Active Model for everything compatible or configure separate capability/app/Agent and Background routes.
* Ordered model routing automatically falls through on classified quota, availability, capability, context or resource conditions while respecting privacy, permissions and admin policy.
* Capability mismatch is filtered before invocation where knowable; model fallback is not misused for unrelated tool/app/business-logic failures.
* Users can reorder, enable/disable and inspect route candidates and understand which model ultimately handled a request.

### V2-B2015 · v2 lines 11566–11567

Model resolution
Dulche supports three model-resolution paths:

### V2-B2016 · v2 lines 11570–11570

Dulche Den

### V2-B2017 · v2 lines 11573–11575

The user's Den may store models using both a stable ID and a user-facing name. Either can be used for lookup. User-facing names are not required to be unique.
Dulche.Den.GetModelByID(id);
Returns the model matching the supplied ID.

### V2-B2018 · v2 lines 11578–11579

Dulche.Den.ListModelsByName(name);
Returns all Den models matching the supplied user-facing name, including their IDs.

### V2-B2019 · v2 lines 11582–11582

Models.toml

### V2-B2020 · v2 lines 11585–11587

Models.toml contains the models available to the current Dulche configuration and the capabilities/properties associated with them.
Dulche.GetModelByID(id);
Returns the configured model matching the supplied ID.

### V2-B2021 · v2 lines 11590–11591

Dulche.ListModelsByConfig(property);
Returns all configured models matching the supplied property. A property may describe a capability, such as reasoning support, or an identifying property such as an ID.

### V2-B2022 · v2 lines 11594–11594

Local directory

### V2-B2023 · v2 lines 11597–11599

Dulche can resolve a model directly from a local directory, with support for as many compatible model file types as practical.
Dulche.GetModelFromDir(directory);
Where an operation fails because the requested model cannot be resolved, Dulche returns "model not found" rather than throwing an exception.

### V2-B2024 · v2 lines 11602–11605

Model discovery and acquisition
Dulche can discover and obtain models from supported sources including llama.cpp, Ollama and Hugging Face.
Dulche.FindModel(source, id);
Checks whether the requested model exists at the specified source.

### V2-B2025 · v2 lines 11608–11609

Dulche.ListModels(source, property);
Lists models from the specified source that match the supplied property.

### V2-B2026 · v2 lines 11612–11613

Dulche.Pull(source, model);
Pulls a model temporarily. A pulled model survives endpoint suspension, but is discarded when the endpoint is stopped.

### V2-B2027 · v2 lines 11616–11617

Dulche.Install(source, model);
Installs a model persistently.

### V2-B2028 · v2 lines 11620–11622

Model replacement
Dulche.Replace(oldModel, newModel);
Replaces one model with another. When no replacement policy is specified, Dulche applies the safe replacement policy: preserve only metadata explicitly classified as model-independent (for example a user-facing alias), recalculate model-derived capabilities/limits from the replacement artifact, and reset configuration values that are invalid or unsupported by the replacement. Immutable artifact/model identity MUST NOT be copied from the old model.

### V2-B2029 · v2 lines 11627–11628

Dulche.Replace(oldModel, newModel, preserveConfig);
Preserves compatible properties from the old model where possible.

### V2-B2030 · v2 lines 11631–11632

Dulche.Replace(oldModel, newModel, wipe);
Replaces the model without preserving its previous configuration.

### V2-B2031 · v2 lines 11635–11635

safe MAY be specified explicitly as the preservation policy. Under safe, only fields classified by the replacement schema as model-independent/safe-to-carry MAY be preserved; all model-derived or compatibility-sensitive fields MUST be recalculated or reset.

### V2-B2154 · v2 lines 12472–12480

Universal Model Picker
On 9to1-OS, the Universal Model Picker is the OS-hosted presentation of the same canonical Dulche/Home model/provider state used by regular 9to1. Model identities, provider connections, ordered Model Routes, active/background routing, category overrides, fallbacks, permissions and usage state MUST remain interoperable across Home and 9to1-OS rather than becoming separate OS-only configuration.
The OS picker provides the same compact selection experience plus deeper integration with Settings → AI, system resource management, administrator policy and local-model lifecycle. It can expose local/cloud/remote identity, capabilities, reasoning/effort controls, aliases/nicknames, route order, fallback state, locality/privacy, loading/residency, RAM/VRAM fit and per-app/Agent/Desktop-Space overrides.
9to1-OS may host the picker natively while Windows regular 9to1 hosts it through Home, but both clients MUST read/write the same versioned routing contracts and Dulche model catalogue. A route configured in Home remains meaningful on 9to1-OS subject to device-local model availability, provider authentication, administrator policy and device-specific resource constraints.
OS resource and policy integration
When a selected local model requires additional memory, the picker integrates with the 9to1-OS resource broker to show required/available/reclaimable RAM/VRAM and may request permitted reclamation before declaring the model unavailable. It MUST distinguish Installed, Unloaded, Loading, Loaded, Offloaded/Partially loaded where supported and Unavailable due to resources.
Administrator-managed user profiles can restrict allowed models/providers, cloud use, model acquisition and routing fallback. Model ordering MUST never bypass those restrictions; policy-ineligible candidates are filtered before route selection.
Shared-routing invariant
Ordered model fallback is a shared 9to1 capability, not an OS-specific behaviour. Requests from regular 9to1/Home and 9to1-OS use the same eligibility-first routing semantics: determine required capabilities and policy constraints, filter the configured route, then choose the highest-priority eligible available model and fall through only on classified model/provider-routing conditions.
