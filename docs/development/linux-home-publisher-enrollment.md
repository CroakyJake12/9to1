# Linux Home publisher enrollment

The maintained ARM64 package path is
`.github/workflows/havenos-linux-package.yml` and
`.github/scripts/native16-arm64/build-native-arm64.py`. Its current deb/hash
receipt is not a Home publisher enrollment or a signed Home package catalogue.

The installed Desktop executable now has an explicit administrative enrollment
entry. It runs before GUI initialization and writes the existing canonical
`HomePackageOriginalPublisherEnrollmentRecord` through `HomePackageDatabase`.
Ordinary startup does not invoke it. No elevation, key generation, private-key
read, package activation or service start occurs in this command.

## Required signer inputs

The installer must receive these genuine externally supplied inputs:

- A public DER code-signing certificate, with digital-signature usage, a current
  validity period and an RSA key of 3072–8192 bits. Its SHA256 is the SHA256 of
  the exact DER bytes. The certificate is selected explicitly by the administrator;
  supplying a certificate file does not silently enroll it.
- The independently confirmed certificate SHA256 and exact signed-catalogue-file
  SHA256. Do not calculate a hash from an untrusted download and treat that alone
  as publisher authorization.
- An existing-format installer catalogue, containing signed Home, Root and
  selected package descriptors, wrapped in a signed envelope. All descriptors
  must declare the selected Linux ABI and the same issuer.
- The explicit issuer ID, ABI, selected package IDs and an operation UUID.

The outer envelope uses the existing descriptor envelope fields:
`schemaVersion: 1`, `issuerKeyId`, base64 `payload` and base64 `signature`.
Its payload is the exact UTF8 JSON of the maintained installer `Catalogue` DTO:
`schemaVersion`, `catalogueRevision`, `issuerKeyId`, `homePackageId`,
`rootPackageId`, `mandatoryPackageIds` and `packages`. Each package contains
`signedDescriptorBase64` and `descriptorPayloadBase64`, with the maintained
`HomePackageArtifactDescriptor` payload. Both the outer catalogue and every
descriptor use RSA-PSS/SHA256 with a SHA256-sized salt and the same selected
public certificate. No signer or catalogue is bundled or fabricated by this entry.

## Installer invocation

The canonical state directory must already exist, be root-owned mode0700 and
have root-owned protected ancestors. Root-owned sticky ancestors such as `/tmp`
are accepted only with root-owned existing children. Existing state and lock
files must be root-owned mode0600 regular single-link files. Symlinks and
user-controlled writable ancestors are refused. The command does not adopt or
chmod existing state paths.

An administrator may prepare the intended state directory separately:

```sh
sudo install -d -o root -g root -m 0700 /var/lib/9to1/Home
```

Replace every uppercase operand below with its actual reviewed value. Package
IDs come from the signed catalogue; they are not inferred from display names.

```sh
sudo /opt/9to1-preview/Haven --home-enroll-publisher \
  --machine-state /var/lib/9to1/Home/home-core-state.json \
  --publisher-certificate /absolute/path/publisher.cer \
  --publisher-sha256 INDEPENDENTLY_CONFIRMED_CERTIFICATE_SHA256 \
  --signed-catalogue /absolute/path/signed-catalogue.json \
  --catalogue-sha256 INDEPENDENTLY_CONFIRMED_SIGNED_CATALOGUE_FILE_SHA256 \
  --issuer-key-id EXPLICIT_ISSUER_ID \
  --abi ABI_FROM_SIGNED_DESCRIPTORS \
  --operation-id OPERATION_UUID \
  --select HOME_PACKAGE_ID_FROM_SIGNED_CATALOGUE \
  --select ROOT_PACKAGE_ID_FROM_SIGNED_CATALOGUE
```

Repeat `--select` for optional selected packages. All mandatory packages must be
selected. The command verifies all signatures before initializing the machine
profile, uses the actual operating-system profile and observed installer image,
and performs the existing guarded database CAS. Repeating the same publisher,
catalogue and selection returns the retained enrollment without rewriting it.
A different existing enrollment is preserved for explicit owning recovery.
On a failed or lost acknowledgement, retain the original operation UUID and inspect
the canonical record before retrying. A failure does not establish that no profile
or enrollment write occurred. A known commit followed by failed final currentness
has the explicit `PublisherEnrollmentCommitUnconfirmed` error and exit code2;
the recorded operation remains available for recovery.

Success prints a JSON receipt with certificate/catalogue hashes, canonical
registry revision and original enrollment operation ID. It explicitly reports
`installedPeerAdmission: NOT_ESTABLISHED` and `packageActivation: NOT_PERFORMED`.
Retain the public certificate, exact signed input and receipt in the existing
private installer/evidence custody. Never place a private signing key in Git.

## Remaining user workflow

This supplies the initial Linux enrollment producer consumed by
`LinuxHomePackageOriginalArtifactProvider`; it does not complete Home Apps
Install, Update, Repair or Uninstall. Those actions still require the genuine
Linux authenticated Root mutation service, protected package activation and
recorded controlled application launches. A listening Home socket, enrollment
row, valid signature or compiled package does not independently establish native
caller or installed-application authority. Full independent/native acceptance
remains paused; this command has not been run against production state here.
