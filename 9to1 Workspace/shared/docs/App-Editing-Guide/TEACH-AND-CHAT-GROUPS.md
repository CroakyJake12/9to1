# Conversation groups and learning sessions

A group organizes related conversations. Keep the active conversation, membership and scope distinct from the group’s presentation. A learning session also needs the selected Space, material and current conversation context. Reuse durable references when a user changes view.

Use the same Chat controller for ordinary sending and visible response state. An uploaded or linked resource does not authorize reading every file in its directory. Permission changes, a missing file or a retired conversation need a clear outcome.

Check switching between two groups, reopening a conversation and returning to an existing learning session. A temporary conversation stays within Chat and follows its own retention rules.

## Durable reference files

`ContainerResourceRepository` validates, hashes and copies an accepted file into:

```text
%APPDATA%\Haven\container-resources\<container-id-N>\<generated-stored-name>
```

The root changes with `HAVEN_DATA_DIR`. Metadata lives in `container_resources`. A unique `(container_id, sha256)` index deduplicates the same content inside one group.

Accepted input currently covers text/code/Markdown/JSON/CSV, PDF, DOCX and images. The repository enforces type and size limits before copying, writes through a temporary file, and removes a failed partial copy. Text and DOCX contents can be added to the group system context within a bounded character budget; image files are attached only when the selected model path supports images. PDF metadata is retained but PDF text extraction is not performed by this repository.

Do not read a reference from its original source path after import. `StoredName` must remain a filename, and `GetStoredPath` confines reads to the group's data directory.

## Tool boundary

Chat Group context is shared model context, not a workspace. A group must not receive:

- project file read/write/search tools;
- Git actions;
- build/test commands;
- arbitrary command execution;
- Studio project intelligence.

Those require Do/Studio plus an existing selected root and the matching permission. Add a tool-policy regression test whenever group context is threaded through a new model request.

## Retained conversation scopes

The compatibility repository distinguishes General Chat, a Chat Group identified by its container, a Teach quick chat without a lesson, and a Teach lesson identified by subject and lesson. Use `ConversationScope.GeneralChat`, `ForChatGroup(id)`, `TeachQuickChat` and `ForTeachLesson(subjectId, lessonId)` as appropriate. Obtain recent rows with `GetRecentInScopeAsync` so older group or lesson conversations are not dropped by filtering a mode-wide page in memory.

[Chat surface](../../../Spaces/UI/Chat/ChatSpace.cui) · [Controller](../../../Spaces/Source/Chat/ChatSpaceController.cs) · [Spaces and learning interactions](../../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
