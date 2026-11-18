# Den, memory and knowledge banks — Background Learning; Eligible learning classes (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2108 · v2 lines 12033–12067

Background Learning
Background Learning is an explicitly opt-in Dulche personalisation subsystem. It is separate from Persistent Memory. Enabling Background Learning allows Dulche to learn useful patterns, interests and reusable knowledge from eligible 9to1 activity over time; it does not itself enable or disable Persistent Memory and it does not imply continuous fine-tuning of the underlying base model.
Eligible learning classes
Background Learning can maintain three broad classes of learned information:
* 9to1 usage and project context: which authorised surfaces the user uses most, recurring workflows, recently or frequently active projects, apps commonly used together, important project terminology/entities and other usage patterns that improve contextual relevance.
* Interests and topic Knowledge Banks: recurring subjects the user appears to care about, together with reusable factual/reference material that can improve later answers and remain available offline. For example, repeated questions about cars may lead to an Interest record for cars and a local Cars Knowledge Bank containing useful terminology, concepts and sourced reference material.
* Other relevant personalisation information: additional permitted information that materially improves responses, such as preferred terminology, recurring goals, explanation preferences, established project conventions, frequently used tools, entity relationships and corrections that should influence future response behaviour.
Usage and project learning
The usage-learning layer SHOULD retain useful semantic state rather than an indiscriminate permanent clickstream. For example, it may learn that Dev and Stack are frequently used for a particular project or that a project is currently active. Raw activity used to derive these signals should be retained only to the extent required by the configured retention/privacy model.
Learned project context can help Dulche resolve ambiguous references, rank context candidates, suggest likely relevant surfaces and prepare authorised Agents with useful project context. It MUST remain scope-aware: project-specific learning does not automatically become unrelated global context.
Interest Knowledge Banks
Background Learning can create and maintain topic-specific KnowledgeBank entities. A KnowledgeBank can contain a stable ID, topic/title, entries, source/provenance metadata, confidence, freshness, creation/update state, storage/sync policy and enabled/disabled state. Knowledge Banks may be created automatically from recurring interests or explicitly managed by the user.
Knowledge Bank entries may include reusable summaries, terminology, relationships, reference facts and other material previously obtained from authorised local sources, user interactions or trusted retrieval. Factual entries MUST retain enough provenance/freshness information for Dulche to distinguish durable knowledge from information that may need refreshing.
Knowledge Banks are not the same as Persistent Memory. For example, 'the user is interested in cars' is personalisation/user context; a structured offline bank explaining engines, EV charging and vehicle terminology is subject knowledge. Dulche may use both when relevant, but they remain separately inspectable and manageable.
Structured learned state
Background Learning MUST store structured learned records rather than relying on one opaque free-form profile. Records should carry stable identity, category/type, subject/entity, learned value, provenance, confidence, creation/update state, last reinforcement/use where useful, freshness/expiry semantics and user-edited/locked state where applicable.
Explicit user corrections outrank contradictory inferred learning. If a user corrects an inferred preference, project status or interest, the system MUST revise/supersede the stale inference instead of allowing both to compete indefinitely as equally valid facts.
Selective retrieval
The complete learning store MUST NOT be injected into every request. Dulche must retrieve only learning context relevant to the active request, Agent, app and permission scope. Retrieval should use current entities/topics/project context to select the smallest useful set of records and Knowledge Bank material.
Privacy and cloud boundaries
Background Learning data is private by default and exists to personalise the user's 9to1 AI experience. It MUST NOT be sold, used to build advertising profiles or indiscriminately uploaded to third parties. Enabling Background Learning does not itself grant permission to transmit learned information to a cloud model/provider.
If a Dulche request uses a remote/cloud provider, disclosure of any learned context remains subject to the normal provider, privacy, permission and data-minimisation rules for that request. The implementation MUST distinguish permission to learn/store information from permission to disclose that information externally.
Any optional cross-device synchronisation of Background Learning state must be user-controlled and protected by the applicable authenticated/encrypted 9to1 sync/storage architecture. The feature MUST remain useful locally and must not require insecure cloud upload merely to personalise responses.
User controls
Users must be able to enable/disable Background Learning, pause new learning, inspect learned information, inspect and manage topic Knowledge Banks, correct inaccurate learning, delete individual learned records or a complete topic bank, clear learned categories and configure which apps/projects/categories may contribute where supported. Disabling learning stops creation/reinforcement of new learned state but MUST NOT silently delete existing Persistent Memory or unrelated user data.
Relationship to Persistent Memory
Persistent Memory and Background Learning are independent. Persistent Memory stores durable cross-session memories under the Memory policy; Background Learning is the opt-in process that derives usage patterns, interests, Knowledge Banks and other personalisation state. Neither feature may silently enable, disable, clear or substitute for the other.
Background Learning may identify information that would be useful as a durable memory, but any conversion into Persistent Memory must use the Memory subsystem and preserve that the source was an inference rather than an explicit user statement. Explicit Memory corrections remain authoritative over contradictory Background Learning inferences.
Background Learning acceptance requirements
* Background Learning is off until the user opts in and remains independently configurable from Persistent Memory.
* Eligible 9to1 usage/project patterns, interests/Knowledge Banks and other useful personalisation information can be learned as structured, inspectable state.
* Topic Knowledge Banks can provide relevant offline reference material and retain provenance/freshness sufficient to avoid presenting stale inferred facts as timeless truth.
* Only relevant learning context is retrieved for a request; the full learning store is not injected indiscriminately.
* Learned data is not sold, used for advertising profiles or uploaded to cloud providers merely because learning is enabled.
* Users can inspect, correct, scope and delete learning independently of Persistent Memory.
