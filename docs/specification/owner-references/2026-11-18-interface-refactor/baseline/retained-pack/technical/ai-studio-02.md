# AI Studio — harnesses, agents and evaluations — Permission Simulator; Dependency Inspector (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1107 · v2 lines 4872–4872

Context Inspector is a contextual AI Studio debugging tool showing what the model actually received or would receive for a request/run. It MUST distinguish Agent/Harness instructions, active Skills, Space/context instructions, selected artifacts, retrieved chunks, memory items, tool schemas, summarised context and omitted material.

### V2-B1108 · v2 lines 4875–4875

For each context item, Context Inspector SHOULD show source identity/revision, provenance, full/summarised/retrieved status, token/size estimate where available, retrieval metadata and why it was selected or omitted. It MUST NOT imply that omitted sources were considered by the model.

### V2-B1109 · v2 lines 4878–4878

Permission Simulator

### V2-B1110 · v2 lines 4881–4881

Permission Simulator evaluates effective capabilities for a selected Agent/Harness/request context without executing the consequential action. It MUST show Allowed, Ask or Denied outcomes plus the contributing Agent, surface, Space, project/entity, caller/organisation and Home trust/policy decisions.

### V2-B1111 · v2 lines 4884–4884

The simulator MUST explain the controlling reason for a denial/approval requirement and MUST use the same permission-resolution contracts as real execution. It is a diagnostic surface, not a way to grant permissions.

### V2-B1112 · v2 lines 4887–4887

Dependency Inspector

### V2-B1113 · v2 lines 4890–4890

Dependency Inspector visualises the canonical dependency graph of Agents, Harnesses, Tools, models, Skills, Plugins, MCP capabilities, schemas, knowledge sources, Automations, other Agents and related resources.

### V2-B1114 · v2 lines 4893–4893

It MUST highlight missing dependencies, updates, disabled packages, revoked permission/access, version/schema incompatibility, inaccessible source and dependency cycles. Clicking a dependency SHOULD open/inspect the canonical entity where permitted.

### V2-B1115 · v2 lines 4896–4896

Comparison view

### V2-B1116 · v2 lines 4899–4899

Compare is a reusable contextual view for compatible entities/runs, including Agent revision vs revision, model vs model, Harness run vs run, Eval run vs baseline, Run Profile vs Run Profile and instruction/configuration revisions.

### V2-B1117 · v2 lines 4902–4902

Comparison MAY show side-by-side outputs plus evaluation scores, deterministic test results, tool/action differences, Action Graph differences, latency, tokens/cost, errors, retries, selected context and effective model/provider. Model-quality comparisons SHOULD support multiple repetitions where needed rather than overgeneralising from one stochastic run.

### V2-B1118 · v2 lines 4905–4905

Documentation Generator

### V2-B1119 · v2 lines 4908–4908

Documentation Generator creates human-readable documentation from canonical definitions rather than requiring a separately maintained copy. It MUST be able to derive relevant identity/version, purpose, availability, dependencies, permissions, actions/APIs, schemas, examples, errors and compatibility information.

### V2-B1120 · v2 lines 4911–4911

Supported outputs MAY include Markdown, HTML, Write documents, developer-site documentation or other typed export targets. Regeneration/update MUST reflect the canonical definition and MUST NOT silently overwrite hand-authored material without an explicit merge/replacement workflow.

### V2-B1121 · v2 lines 4914–4914

Templates

### V2-B1122 · v2 lines 4917–4917

Templates MUST be real inspectable versioned starter definitions/projects. Supported template families SHOULD include Harness, Agent, Skill, Plugin, MCP, Evaluation, Test Suite, Schema, Model Router, Generative UI and Run Profile where appropriate.

### V2-B1123 · v2 lines 4920–4920

Instantiating an Agent template creates a new canonical Den-backed Dulche Agent. Instantiating project/resource templates creates new entities with their own stable IDs; template identity/version remains provenance rather than shared mutable state.

### V2-B1124 · v2 lines 4923–4923

Validation, install and deployment boundaries

### V2-B1125 · v2 lines 4926–4926

AI Studio MUST clearly distinguish authoring, validation, test/preview, build/package, install, activate and deploy/run concepts. Building a valid Plugin/Skill/MCP package does not automatically enable it, grant permissions, connect accounts or establish trust. Activating an Agent does not bypass runtime permissions. Deploying/running a Harness does not make its draft edits production state.

### V2-B1126 · v2 lines 4929–4929

All validation MUST use the same canonical Dulche/Home schema, dependency, model-capability, permission and runtime contracts used after activation/install. A preview that succeeds only because it bypassed real contracts is non-conformant.

### V2-B1127 · v2 lines 4932–4932

AI Studio API

### V2-B1128 · v2 lines 4935–4967

9to1.AIStudio.ListProjects();
9to1.AIStudio.CreateProject(type, templateID?);
9to1.AIStudio.OpenProject(projectID);
9to1.AIStudio.ValidateProject(projectID);
9to1.AIStudio.TestProject(projectID, testConfig?);
9to1.AIStudio.ExportProject(projectID, destination);
9to1.AIStudio.Harness.CreateFromTemplate(templateID, config);
9to1.AIStudio.AgentBuilder.Create(definition);
9to1.AIStudio.AgentBuilder.Open(agentID);
9to1.AIStudio.AgentBuilder.UpdateDraft(agentID, patch, expectedRevision?);
9to1.AIStudio.AgentBuilder.Validate(agentID, revision?);
9to1.AIStudio.AgentBuilder.Preview(agentID, input, options?);
9to1.AIStudio.AgentBuilder.Activate(agentID, revision);
9to1.AIStudio.AgentBuilder.Share(agentID, principalID, role);
9to1.AIStudio.ToolBuilder.Create(type, config);
9to1.AIStudio.ToolBuilder.Validate(projectID);
9to1.AIStudio.ToolBuilder.Install(projectID);
9to1.AIStudio.Playground.Run(config, input);
9to1.AIStudio.Evaluations.Create(config);
9to1.AIStudio.Evaluations.Run(evalSuiteID, target, runProfileID?);
9to1.AIStudio.Tests.CreateSuite(config);
9to1.AIStudio.Tests.Run(testSuiteID, target, runProfileID?);
9to1.AIStudio.Replay.Open(runID);
9to1.AIStudio.Replay.RestartFrom(runID, nodeID, overrides?);
9to1.AIStudio.Schemas.Create(schema);
9to1.AIStudio.ModelRouters.Create(policy);
9to1.AIStudio.GenerativeUI.Create(definition);
9to1.AIStudio.RunProfiles.Create(profile);
9to1.AIStudio.ContextInspector.Get(runOrRequestID);
9to1.AIStudio.PermissionSimulator.Evaluate(config);
9to1.AIStudio.Dependencies.Get(entityRef);
9to1.AIStudio.Compare(inputs, options?);
9to1.AIStudio.Documentation.Generate(entityRef, format, options?);

### V2-B1129 · v2 lines 4970–4970

These are minimum canonical entry points. User-visible operations across builders, test/debug tools and resources MUST have equivalent typed APIs under the global API-parity rule.

### V2-B1130 · v2 lines 4973–4973

Failure behaviour

### V2-B1131 · v2 lines 4976–4976

AI Studio MUST return structured failures including ProjectNotFound, AgentNotFound, InvalidDefinition, ValidationFailed, DependencyMissing, DependencyConflict, SchemaIncompatible, ModelCapabilityMismatch, PermissionDenied, PreviewSideEffectDenied, EvaluationTargetUnavailable, GraderUnavailable, TestAssertionFailed, ReplayPointUnavailable, ReplaySideEffectConflict, ContextUnavailable, RunProfileUnavailable, RouterInvalid, DocumentationGenerationFailed and RevisionConflict in addition to the global error contract.

### V2-B1132 · v2 lines 4979–4979

Acceptance requirements

### V2-B1133 · v2 lines 4982–5005

AI Studio is implementation-complete only when acceptance coverage verifies at least:
* Navigation exposes Build, Test & Debug and Resources using the defined surfaces without treating Dulche Agents as duplicate AI Studio projects.
* A Harness can be created from a template, validated and run through the real Dulche runtime with Action Graph visibility.
* Harness advanced graph uses the shared Home Node Graph Framework and remains optional for simple Harnesses.
* Agent Builder directly creates/edits a Den-backed Dulche Agent and preserves the structured Workspace-Agents-style layout including collapsed More Options.
* Tool Builder can create/test/install Skill, Plugin and MCP outputs using their real Dulche contracts.
* Playground can combine model/Agent/Skills/Plugins/context/Run Profile and exposes effective runtime details.
* A Playground result can be saved as an Evaluation Case and deterministic Test where applicable.
* Evaluations support structured cases/graders and distinguish deterministic results from model/human judgements.
* Test Suites assert tool/action/schema/state/permission behaviour and preserve reproducible failing run data.
* Replay opens an actual prior Dulche run and creates a safe branch/new run without mutating the original history.
* Schema Builder exposes dependency impact before activating a breaking shared-schema revision.
* Permission Simulator explains the actual effective permission outcome without executing the action.
* Context Inspector shows included/retrieved/summarised/omitted context truthfully with provenance.
* Model Router policies integrate with Universal Model Picker/fallback privacy rules rather than creating a competing model system.
* Generative UI Builder restricts generated UI to approved CUI components/actions and validates accessibility/responsive behaviour.
* Dependency Inspector detects missing/disabled/incompatible/cyclic dependencies and opens canonical entities.
* Run Profiles alter execution environment/policy without mutating the underlying Agent/Harness/Tool definition.
* Compare can compare compatible revisions/runs/models with outputs plus relevant eval/test/runtime differences.
* Documentation Generator derives documentation from canonical definitions and preserves explicit merge/overwrite semantics.
* Templates create independent entities with template provenance rather than shared mutable copies.
* Authoring, validation, preview/test, install, activation and runtime execution remain separate permissioned lifecycle stages.
* Close/reopen AI Studio preserves projects, drafts and resource state without making AI Studio the runtime owner.
* Core operations across builders and test/debug/resources have equivalent typed API coverage.

### V2-B2321 · v2 lines 13475–13475

### UX27-OTHER-013 — AI Studio builders and test boundaries

### V2-B2322 · v2 lines 13477–13477

**B/D.** Harness, Agent, Tool, Skill, Plugin, MCP, Schema, Router and Generative UI builders operate on typed versioned definitions with clear Draft/Active/Test states. Property forms expose meaningful validation and dependencies, not an arbitrary JSON editor as the sole required user experience. Graph edits and form edits update the same definition.

### V2-B2323 · v2 lines 13479–13479

Playground/evaluation/replay results identify the model, inputs, definition revision, permissions and actual run. Publishing/installing a definition is distinct from testing it. Permission simulation cannot grant live permissions. A generated avatar preview does not imply the Agent is executing the activity shown unless a real event drives it.
