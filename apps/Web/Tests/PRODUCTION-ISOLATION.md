# Production and visual fixture isolation

The visual executable under `Spaces/Tests/Visual` deliberately supplies deterministic records to the real renderer. Its backend, assembly, input file and sample content must not enter the public product bundle. Visual comparisons do not establish account, provider, tool or persistence acceptance.

The existing `ci/run-ordinary-web-publish.py` now runs `verify-production-isolation.cjs` before sealing. It supplies the actual restored `project.assets.json` and evaluated `Compile`, `ProjectReference` and `ReferencePath` items from its ordinary SDK Build. The guard rejects test/visual fixture project or linked-source ownership, scans every public file and gzip/Brotli variant, and records its result in the publication receipt. The existing `verify-publish-receipt.cjs` also scans artifact contents, so internally consistent hashes do not excuse a fixture leak.

The scan recognizes exact fixture ownership markers and a combination of known reference-content fields in UTF-8, UTF-16LE and UTF-16BE. It does not blacklist Qwen or other legitimate model names. Generic production `Visual` directories/assemblies remain allowed. Only the explicitly supplied bundle and build metadata are read; no account/profile database or user record is inspected or changed. Invalid compressed assets fail verification.

Run the reusable controls from the repository root:

```sh
node apps/Web/Tests/production-isolation.test.cjs \
  /absolute/path/to/immutable/wwwroot \
  /absolute/path/to/project.assets.json \
  /absolute/path/to/apps/Web/NineToOne.Web.csproj \
  /absolute/path/to/new-evidence-directory
```

The controls inspect the real supplied artifact and current restored project graph, then create deliberate leaks in temporary directories. They exercise raw/gzip/Brotli UTF-8/UTF-16, copied seed data, a transitive fixture project, a linked fixture source and a matching publication receipt. Temporary controls are removed, and the real artifact/metadata are checked unchanged.

A direct scan can additionally consume the actual SDK evaluated-items JSON:

```sh
node apps/Web/Tests/verify-production-isolation.cjs \
  /absolute/path/to/wwwroot \
  /absolute/path/to/project.assets.json \
  /absolute/path/to/apps/Web/NineToOne.Web.csproj \
  /absolute/path/to/evaluated-build-items.json
```

Without that final argument, the report explicitly states that root evaluated compile/reference items were not checked. Initial local validation exercised the immutable checkpoint35 bundle and restored47-project graph; the new actual evaluated-items integration remains pending its next normal source publication. Controlled negative items are not evidence that the actual SDK stage ran. The restored graph does not recursively evaluate every transitive project's Compile items.

This is a bounded known-fixture regression gate, not a proof against every possible generated or encoded sample and not functional acceptance. Fresh authenticated account, real model response, real tools, reload, isolation and specified cross-device behaviour still require independent operation of the compiled product.
