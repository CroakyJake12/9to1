# Migrate a saved Assistant definition

The migration reads the legacy SQLite agent catalogue and presents an explicit review before importing into the current Assistant or Specialist definition. Preserve the personal Home Den and definition identity. The conversation remains owned by Spaces.

Import only the reviewed rows and source scope. Retain source material, original revisions and an idempotent receipt so interruption or a second attempt cannot produce a second definition. Treat memory lineage as a separate permissioned operation.

Use the actual local source and controller for integration checks. Inspect a conflict or changed source before allowing a retry.

## Explicit review and recovery

List pages are bounded. Preview reads the actual selected SQLite row, including disabled and built-in entries, without seeding the catalogue. It supplies the original definition, compatible review notes, scoped-memory/run availability and preserved links. No Assistant/Specialist type is inferred or preselected. The source-issued preview is an opaque current presentation object; constructing IDs or reusing another view's preview does not authorize conversion.

Confirmation compares the exact source definition fingerprint and actual destination revision, reserves the original SQLite writer while checking/committing, and preserves the stable legacy GUID. An unrelated Den record with that GUID is a collision, regardless of name or record type. The first Den write stages the original row's complete raw fields, original store/row identity, profile and the user's exact operation/type/configuration. The existing canonical classifier supplies configured product metadata and captures its unclassified before-state. The module does not create another definition serializer, chat engine, task store, model router or permission system.

Legacy SQLite definitions, runs, conversations/messages, knowledge records, provenance/privacy/retention metadata, retrieval records and resource references remain in their original owners. `ReadPreservedLinksAsync` reads current links through the owned source; these are durable references and display observations, never membership, execution or resource grants. Natural conversation adoption must use a genuine canonical source-owner membership operation; this module does not invent authority from an activity JSON `ContextId`. Legacy API availability remains intact through April 2027.

Interruption after a successful stage leaves that original state inspectable. A fresh preview reports `PendingClassification` with the already confirmed type and operation. Continuing is a new explicit action over that exact choice and unchanged source; it does not automatically replay an unknown write. A changed source or choice refuses. An acknowledged classification can be reviewed for undo. On reopening, preview returns `ClassifiedDefinition` from the SAME actual canonical bridge; the UI does not reconstruct a destination identity. Undo restores the exact unclassified Den before-state only while the classified definition is unchanged and no continuing canonical conversation, run or memory exists. It never deletes history. Both preview and commit check this restriction. A restrictive policy over the **same** Den store and original Home policy repeats the checks under the actual Den writer lease and immediately before publication, so a previously reserved conversation prevents undo. Reads in that callback use the original policy and do not acquire the writer lease; never call Home.Open or Den ownership observation inside it.

[SQLite source](LegacySavedAgentSqliteSource.cs) · [Controller](LegacyAgentMigrationController.cs) · [Tests](Tests)
