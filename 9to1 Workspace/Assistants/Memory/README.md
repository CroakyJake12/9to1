# Assistant and Specialist memory access

A Specialist definition, a conversation and a memory source have separate identities and permission scopes. The existing memory module uses Home’s current actor and Den, Spaces membership and the configured Knowledge Library and retrieval providers. The Specialist’s policy for web, general knowledge and memory must remain independently controllable.

For a read, validate the active session and definition revision, Home receipt and protected store identity before obtaining content. Narrow the query to the admitted source and membership. A disabled source returns a declined result before data callbacks. For a write, preserve the original owner, revision and journal acknowledgement.

Test denied and retired sessions, repeated delivery, a removed membership and a different Den. A successful lookup in a fixture cannot establish access to an owner’s live memory or a cloud resource.

## Admission and commit

For scoped access, pair the original conversation membership source with Home’s `OpenWithinOriginalSourceAsync` observation. The imported `assistant.memory` receipt and exact source store, Den, namespace and definition identify an authorized read. A matching legacy agent GUID alone cannot authorize the row.

The management controller’s read produces an opaque permitted view. Prepare records the bounded title, summary and operation, and Commit accepts the exact issued preview. Revalidate the current actor, revisions and Home decision on that path. Keep an acknowledged refusal distinct from a completed memory write.

[Memory contracts](Core) · [Focused tests](Tests) · [Specialist and memory rules](../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
