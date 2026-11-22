# UCML local file utility

Select this tool to validate, inspect, package or extract an explicit local UCML file. Its commands operate on the files supplied by the caller. Canonical context retrieval and grants belong to the authenticated application owner. Use the dedicated protection utility for retained encryption keys and envelopes. [Protected UCML envelopes](../ucml-protect/README.md)

Build from the repository root with the pinned .NET SDK. On the development PC, use the guarded serial SDK workflow for the build. [PC build procedure](../../docs/development/workflow.md)

```sh
dotnet build tools/ucml/NineToOne.Ucml.Tool.csproj -c Release
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll --help
```

The tool validates, inspects, packages, extracts and selects explicitly chosen
local UCML files. It has no networking, database, automatic directory watcher,
model call, or API credential. It is an owner-run interchange utility; it does
not replace the authenticated canonical memory broker. A file's declared
consent cannot issue runtime permission.

Examples (replace paths with your own):

```sh
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll validate /path/profile.ucml
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll inspect /path/profile.ucml
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll pack /path/body.md --metadata /path/metadata.json --output /path/profile.ucml
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll unpack /path/profile.ucml --output /path/body-copy.md
dotnet tools/ucml/bin/Release/net10.0/9to1-ucml.dll select /path/memory/ucml --query 'Dulche local models' --max-chars 12000 --output /path/context.md
```

Outputs must not already exist and receive owner-only file permissions on Unix.
No command automatically overwrites, uploads, or imports personal context into a
live app. Input is bounded to 2 MiB per file, with tighter codec limits applied
after decoding. Selection is a deterministic lexical ranking, considers only
top-level UCML files, omits expired records, and keeps whole records within the
character budget. It is not semantic/vector retrieval and a character budget is
not an exact model token count. For large packs, keep a compact starter prompt
and select relevant records per task rather than loading everything every turn.

UCML is readable plaintext interchange. Keep all filled personal UCML files,
their sealed copies and accompanying memory Markdown/JSON outside this source
repository. Use a private user-data or export directory. Repository examples
and tests must use fictional data only; `.gitignore` is an additional safeguard,
not permission to store private exports here. The
separate protection API encrypts envelopes; canonical database encryption and
remote deployment have their own explicit requirements in the backend docs.
