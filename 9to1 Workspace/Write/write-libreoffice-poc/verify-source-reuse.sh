#!/usr/bin/env bash
set -euo pipefail
# The caller supplies prebuilt tools, allowing the exact regression in a runtime-only image.
if [[ $# != 2 ]]; then
  echo 'usage: verify-source-reuse.sh <source-document> <new-proof-directory>' >&2
  exit 64
fi
: "${LO_PROGRAM_PATH:?Set the installed LibreOffice program directory}"
: "${SEMANTIC_BINARY:?Set the prebuilt semantic probe}"
: "${ENGINE_BINARY:?Set the prebuilt engine}"
: "${CLIENT_BINARY:?Set the prebuilt client}"
source_document="$(realpath "$1")"
proof_directory="$2"
mkdir "$proof_directory"
proof_directory="$(realpath "$proof_directory")"
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cp -- "$source_document" "$proof_directory/source.odt"
source_document="$proof_directory/source.odt"
source_hash="$(sha256sum "$source_document" | cut -d ' ' -f 1)"
mkdir "$proof_directory/semantic-profile"
timeout 30s "$SEMANTIC_BINARY" "$LO_PROGRAM_PATH" "$proof_directory/semantic-profile" \
  "$source_document" "$proof_directory/semantic.odt"
for session in 1 2; do
  IPC_WORK_DIR="$proof_directory/session-$session" bash "$script_directory/run-ipc-probe.sh" \
    "$source_document" "$proof_directory/session-$session.odt" "$proof_directory"
  test "$(sha256sum "$source_document" | cut -d ' ' -f 1)" = "$source_hash"
  test ! -e "$proof_directory/.~lock.source.odt#"
done
printf '%s\n' 'PASS: same source reused by semantic and two IPC sessions; source hash unchanged and no shared-source native lock created.'
