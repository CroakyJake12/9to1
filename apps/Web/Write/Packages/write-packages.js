// Browser file brokerage only. Canonical IDs/revisions/documents remain in the repository.
const maximumPackageBytes = 128 * 1024 * 1024;
const maximumImageBytes = 32 * 1024 * 1024;
let pendingPick;

export function cancelPick() { pendingPick?.finish(null); }

function readBase64(file, limit) {
  return file.arrayBuffer().then(buffer => {
    if (buffer.byteLength > limit) throw new Error("This file exceeds the size limit the document can carry.");
    const bytes = new Uint8Array(buffer);
    const chunks = [];
    for (let offset = 0; offset < bytes.length; offset += 32768)
      chunks.push(String.fromCharCode(...bytes.subarray(offset, offset + 32768)));
    return btoa(chunks.join(""));
  });
}

function chooseFile(accept, limit, map) {
  if (pendingPick) throw new Error("A native file chooser is already open.");
  return new Promise((resolve, reject) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = accept;
    input.style.display = "none";
    let settled = false;
    const finish = (value, error) => {
      if (settled) return;
      settled = true;
      input.remove();
      if (pendingPick?.input === input) pendingPick = undefined;
      if (error) reject(error); else resolve(JSON.stringify(value));
    };
    pendingPick = { input, finish };
    input.addEventListener("cancel", () => finish(null), { once: true });
    input.addEventListener("change", async () => {
      try {
        const file = input.files?.[0];
        if (!file) return finish(null);
        if (file.size > limit) throw new Error("This file exceeds the size limit the document can carry.");
        const base64 = await readBase64(file, limit);
        if (settled) return;
        finish(map(file, base64));
      } catch (error) { finish(null, error); }
    }, { once: true });
    document.body.append(input);
    try { input.click(); } catch (error) { finish(null, error); }
  });
}

export function pickPackage() {
  return chooseFile(".9to1w", maximumPackageBytes, (file, base64) => ({ name: file.name, base64 }));
}

export function pickImage() {
  return chooseFile("image/png,image/jpeg,image/webp,image/gif,image/bmp", maximumImageBytes,
    (file, base64) => ({ name: file.name, mediaType: file.type || "application/octet-stream", base64 }));
}

export function downloadPackage(filename, base64) {
  const binary = atob(base64);
  if (binary.length > maximumPackageBytes) throw new Error("This package exceeds the owning codec's size limit.");
  const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.append(link);
  try { link.click(); }
  finally {
    link.remove();
    // Browser consumes the exact Blob bytes asynchronously after activation.
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }
}
