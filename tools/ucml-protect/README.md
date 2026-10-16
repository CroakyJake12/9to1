# Trusted offline UCML envelope utility

Linux only. Build with `dotnet build tools/ucml-protect/NineToOne.Ucml.Protect.csproj -c Release`.
Run `dotnet tools/ucml-protect/bin/Release/net10.0/9to1-ucml-protect.dll --help`.

The executable uses the **actual maintained** LinuxProviderSecretStore and
ProviderSecretUcmlKeyCustody sources, with the actual Haven.Application
interfaces. It does not implement another credential database. Source linking
keeps the CLI independent of the full desktop/Assistant object graph.

Run this utility explicitly as the local operating-system user. Do not expose it
as a model tool, public API, remote service, or substitute for Home authorization.
The local credential owner validates the directory/file owner, private modes,
physical path and retained key custody. The portable `owner` label binds the
document and envelope; it is not an internet authentication scheme.

`init-key` explicitly creates a fresh retained root key and writes only its
opaque identifier to the requested output. Daily derived keys and fresh
per-envelope salts preserve decryption of older envelopes. A missing/corrupt
retained key fails closed. Repeating initialization creates another root key;
it does not recover older envelopes. Keep old keys and backup the whole private
credential directory through an appropriate private backup channel. Do not add
that directory to Git, a memory prompt, or an ordinary migration archive.

`seal` and `unseal` require new output paths and never delete their inputs. Sealed
envelopes authenticate the owner, scope, document ID and revision. They expose
that routing metadata and key identifier, but encrypt the Markdown and full
UCML provenance. The reference file is not a bearer capability or raw key.

The Linux credential owner itself retains a private OS-user-readable key file;
this is not a claim of protection against an attacker controlling that OS user
or unencrypted disk. Sealing an export also does not encrypt the preexisting
canonical SQLite/Den stores, plaintext original, swap, backups or other files.
