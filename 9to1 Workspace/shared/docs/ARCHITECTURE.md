# Architecture of the shared native application

Start with the state you intend to change. Core records describe identities. Application services expose use cases. Infrastructure implements durable stores and external adapters. Desktop composes those services with CUI and the supported native host integrations. Browser state has its own owner and lifetime.

A persistent UI action needs the identity and authority for the current profile, object and actor. A window or tab can disappear while an action is in flight. Join the original work through its owner and keep a confirmed write distinct from a later failure to display it.

Use a storage or provider integration test for the durable action, then inspect the same route through Haven. An in-memory presentation check cannot establish disk recovery or account access.

[Service ownership](architecture/system-map.md) · [Persistence](architecture/state-and-persistence.md) · [Host composition](../src/Haven.Desktop/App.UnifiedWorkspaceHost.cs) · [Editing boundaries](App-Editing-Guide/ARCHITECTURE.md)
