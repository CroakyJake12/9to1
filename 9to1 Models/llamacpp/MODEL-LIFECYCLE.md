# llama.cpp model lifecycle manager

`modelctl.py` is the explicit mutation boundary for Haven's local GGUF store. It is intentionally separate from `haven-inference-broker.service`, whose systemd sandbox keeps the model store read-only.

## Operations

- `list [--verify]` reads installed manifests. `--verify` re-hashes each blob and validates its GGUF header.
- `import FILE --id ... --name ... --license ... --origin ...` copies an already-local file into a same-filesystem staging directory, validates it, adopts it by SHA-256 content address, and only then atomically publishes its manifest.
- `import ... --replace` is the explicit update path. It replaces the manifest only after the new blob is durable. The old blob is retained for rollback/explicit later cleanup.
- `delete ID` removes the manifest but retains the blob.
- `delete ID --purge-blob` removes the blob only when no other manifest references that digest.

The manager has no downloader and no HTTP client. Network acquisition belongs to a later, separately consented catalogue/downloader boundary.

## Crash consistency

Import order is **staged blob → validated content-addressed blob → manifest**. A crash can therefore leave an orphan blob, but never intentionally publishes a manifest before its blob exists.

Delete order is **manifest → optional unreferenced blob**. A crash can again leave an orphan blob, but does not intentionally leave a published manifest pointing to a blob deleted by that operation.

Manifest publication uses a temporary file, `fsync`, atomic `os.replace`, and a directory `fsync`. Blob adoption uses a staging file inside the model store so the final rename remains on the same filesystem.

## Concurrency and loaded-model safety

All mutating manager operations take an exclusive store lease. Each model also has a per-model lease under the private Haven runtime directory:

- the inference broker holds a **shared** model lease for the full lifetime of a loaded llama.cpp worker;
- import/update/delete takes an **exclusive** model lease;
- if the model is loaded, mutation fails rather than racing a live mmap/read;
- locks are kernel `flock` leases, so process death automatically releases them.

This is stronger than a check-then-delete health query because the lease spans the actual worker lifetime and closes the time-of-check/time-of-use gap.

## Permissions

Manager-created model-store directories are owner-only (`0700`). GGUF blobs, manifests, and lease files are owner-only (`0600`). Local import rejects symlink sources and only reads regular files.

## Licence boundary

`--license` and `--origin` are mandatory metadata. Importing a technically compatible GGUF does not imply that Haven may redistribute it. The default manifest redistribution state is `review-required` unless explicitly recorded otherwise by the calling workflow.
