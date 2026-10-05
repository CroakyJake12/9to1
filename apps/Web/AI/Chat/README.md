This subtree is excluded from the browser host and remains unregistered.

`ChatSpaceBrowserReadAdapter` requires the actual owning `IChatSpaceBackend`, loads the existing embedded `ChatSpace.cui`, and calls the actual `ChatSpaceController.InitializeAsync`. It exposes existing owner state without replacement conversation or model records. It enables no markup mutations or inference, registers no route or controls, and supplies no default backend, persistence or authority.

The element-name inventory reports missing registrations against the real `CuiControlRegistry`. Registering a name alone does not establish behavior. The existing Chat renderer test substitutes empty Panels for specialized controls; that test cannot establish a working transcript, composer, selection, keyboard or compact layout. Repeated owner records still require the real item-binding/control implementation listed in `CONTROL-RUNTIME-REQUIREMENTS.json`.

The host must use a fresh adapter for each actor/store context, cancel/dispose old private reads, and perform actual owning authorization. Owner error status is retained; reads and supplied backend side effects are not inferred as successful durable writes. Temporary local Save, placement/transfer, trusted model execution and graph publication await the exact owner contracts in `../OWNER-CONTRACT-REQUEST.json`.

Compilation, CUI rendering, browser behavior and provider/durable acceptance are NOT-RUN. This source is a bounded owner-consumer proposal, not an installed Chat surface.
