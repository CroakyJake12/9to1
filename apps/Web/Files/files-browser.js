const basePath = "/api/files/v1";
const maximumJsonBytes = 1024 * 1024;
const maximumChunkBytes = 8 * 1024 * 1024;
const guidPattern = /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i;
const allowedExports = new Set([
    "application/pdf",
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
]);

async function readJsonText(response) {
    if (!response.body) return "";
    const mediaType = response.headers.get("Content-Type")?.split(";", 1)[0]?.trim().toLowerCase();
    if (mediaType !== "application/json" && !mediaType?.endsWith("+json")) {
        await response.body.cancel();
        if (response.ok) throw new Error("Files returned an unexpected response.");
        return "";
    }
    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8", { fatal: true });
    let bytes = 0;
    let result = "";
    try {
        while (true) {
            const { value, done } = await reader.read();
            if (done) return result + decoder.decode();
            bytes += value.byteLength;
            if (bytes > maximumJsonBytes) throw new Error("Files returned an oversized response.");
            result += decoder.decode(value, { stream: true });
        }
    } catch (error) {
        await reader.cancel().catch(() => {});
        throw error;
    } finally {
        reader.releaseLock();
    }
}

function requestTarget(action, args, group) {
    switch (action) {
        case "context":
        case "usage":
            return { path: "/" + action };
        case "list": {
            const query = new URLSearchParams();
            if (args.parentId != null) query.set("parentId", itemId(args.parentId));
            if (args.query != null) query.set("query", args.query);
            if (args.pageToken != null) query.set("pageToken", args.pageToken);
            if (args.trash === true) query.set("trash", "true");
            return { path: "/items?" + query };
        }
        case "get":
            return { path: "/items/" + itemId(args.id) };
        case "changes": {
            const query = new URLSearchParams({ limit: args.limit });
            if (args.after != null) query.set("after", args.after);
            return { path: "/changes?" + query };
        }
        case "mutate":
            return { path: "/operations", body: JSON.stringify(args), contentType: "application/json" };
        case "connect":
        case "disconnect":
            return { path: "/" + action, body: "{}", contentType: "application/json" };
        case "beginUpload":
            return { path: "/uploads", body: JSON.stringify(args), contentType: "application/json" };
        case "uploadStatus":
            return { path: "/uploads/" + itemId(args.id) };
        case "findUpload":
            return { path: "/uploads?operationId=" + itemId(args.id) };
        case "cancelUpload":
            return { path: "/uploads/" + itemId(args.id) + "/cancel", body: "{}", contentType: "application/json" };
        case "uploadChunk": {
            const file = group.files.get(args.handle);
            if (!file || !Number.isSafeInteger(args.offset) || args.offset < 0 ||
                !Number.isSafeInteger(args.length) || args.length < 1 || args.length > maximumChunkBytes ||
                args.offset + args.length > file.size) throw new Error("Invalid Files upload chunk.");
            const body = file.slice(args.offset, args.offset + args.length);
            return {
                path: "/uploads/" + itemId(args.id) + "/content?offset=" + args.offset,
                method: "PUT",
                body,
                contentType: "application/octet-stream",
                chunkSize: body.size,
            };
        }
        case "prepareDownload":
            itemId(args.itemId?.value);
            if (args.exportMime && !allowedExports.has(args.exportMime)) throw new Error("Unsupported Files export format.");
            return { path: "/downloads", body: JSON.stringify(args), contentType: "application/json" };
        default:
            throw new Error("Files action is unavailable.");
    }
}

function itemId(id) {
    if (typeof id !== "string" || !guidPattern.test(id)) throw new Error("Invalid Files item.");
    return id;
}

function chooseFile(signal, document = globalThis.document) {
    return new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.type = "file";
        input.hidden = true;
        const finish = (file, error) => {
            signal.removeEventListener("abort", abort);
            input.remove();
            if (error) reject(error);
            else resolve(file);
        };
        const abort = () => finish(null, signal.reason);
        input.addEventListener("change", () => finish(input.files?.[0] ?? null), { once: true });
        input.addEventListener("cancel", () => finish(null), { once: true });
        signal.addEventListener("abort", abort, { once: true });
        try {
            signal.throwIfAborted();
            document.body.append(input);
            input.click();
        } catch (error) {
            finish(null, error);
        }
    });
}

function openBrowserTab(url, document = globalThis.document) {
    const link = document.createElement("a");
    link.href = url;
    // The service sets Content-Disposition only after admitting the streamed response.
    link.rel = "noopener noreferrer";
    link.target = "_blank";
    link.referrerPolicy = "no-referrer";
    link.hidden = true;
    document.body.append(link);
    try { link.click(); }
    finally { link.remove(); }
}

export function createFilesBrowserBridge({
    fetchImpl = globalThis.fetch.bind(globalThis),
    navigate = url => globalThis.location.assign(url),
    scheduleNavigation = callback => globalThis.setTimeout(callback, 0),
    pickFile = chooseFile,
    startDownload = openBrowserTab,
    openNotice = openBrowserTab,
    onSessionLost = () => {}
} = {}) {
    const groups = new Map();

    function createGroup(id) {
        if (groups.has(id)) throw new Error("Files request group already exists.");
        groups.set(id, { active: true, enrolled: false, requests: new Map(), files: new Map(), picking: false });
    }

    function enroll(id) {
        const group = groups.get(id);
        if (!group?.active) throw new Error("Files request group is closed.");
        group.enrolled = true;
    }

    function revoke(id) {
        const group = groups.get(id);
        if (!group?.active) return;
        group.active = false;
        group.files.clear();
        for (const entry of group.requests.values()) entry.controller.abort();
    }

    async function drain(id) {
        const group = groups.get(id);
        if (!group) return;
        if (group.active) throw new Error("Revoke Files requests before closing.");
        await Promise.allSettled([...group.requests.values()].map(entry => entry.work));
        groups.delete(id);
    }

    function cancel(groupId, requestId) {
        groups.get(groupId)?.requests.get(requestId)?.controller.abort();
    }

    function invoke(groupId, requestId, action, argumentsJson) {
        const group = groups.get(groupId);
        if (!group?.active || group.requests.has(requestId)) {
            return Promise.reject(new Error("Files request group is closed."));
        }
        const controller = new AbortController();
        // Register the promise before fetch, even if the host calls back into the bridge.
        const work = Promise.resolve().then(async () => {
            controller.signal.throwIfAborted();
            if (action === "pickUpload") {
                if (group.picking || group.files.size !== 0) throw new Error("An upload file is already selected.");
                group.picking = true;
                try {
                    const file = await pickFile(controller.signal);
                    controller.signal.throwIfAborted();
                    if (!file) return JSON.stringify({ status: 200, body: JSON.stringify({ file: null }) });
                    const handle = globalThis.crypto.randomUUID();
                    group.files.set(handle, file);
                    return JSON.stringify({
                        status: 200,
                        body: JSON.stringify({ file: { handle, name: file.name, sizeBytes: file.size,
                            contentType: file.type || "application/octet-stream" } }),
                    });
                } finally {
                    group.picking = false;
                }
            }
            const { path, method, body, contentType, chunkSize } = requestTarget(action, JSON.parse(argumentsJson), group);
            const headers = { Accept: "application/json" };
            if (contentType) headers["Content-Type"] = contentType;
            if (chunkSize != null) headers["x-file-chunk-size"] = String(chunkSize);
            const response = await fetchImpl(basePath + path, {
                method: method ?? (body === undefined ? "GET" : "POST"),
                body,
                headers,
                credentials: "same-origin",
                mode: "same-origin",
                cache: "no-store",
                redirect: "error",
                signal: controller.signal,
            });
            if ((response.status === 401 || response.status === 403) && (action !== "context" || group.enrolled)) {
                if (group.active) {
                    revoke(groupId);
                    // The native reset drains this request. Never await the reset here.
                    queueMicrotask(() => { onSessionLost(); });
                }
                if (response.body) await response.body.cancel();
                return JSON.stringify({ status: response.status, body: "" });
            }
            const responseBody = await readJsonText(response);
            controller.signal.throwIfAborted();
            return JSON.stringify({ status: response.status, body: responseBody });
        });
        const tracked = work.finally(() => group.requests.delete(requestId));
        group.requests.set(requestId, { controller, work: tracked });
        return tracked;
    }

    function openGoogleAuthorization(groupId, value) {
        const group = groups.get(groupId);
        if (!group?.active) throw new Error("Files request group is closed.");
        const url = new URL(value);
        if (url.origin !== "https://accounts.google.com" || url.pathname !== "/o/oauth2/v2/auth" ||
            url.username || url.password || url.hash) {
            throw new Error("Files returned an invalid Google authorization URL.");
        }
        const destination = url.href;
        // Let the native command settle before navigation runs the browser's unsaved-work check.
        scheduleNavigation(() => {
            if (group.active && groups.get(groupId) === group) navigate(destination);
        });
    }

    function releaseUpload(groupId, handle) {
        groups.get(groupId)?.files.delete(handle);
    }

    function openDownload(groupId, transferId, url) {
        if (!groups.get(groupId)?.active) throw new Error("Files request group is closed.");
        if (url !== basePath + "/downloads/" + itemId(transferId) + "/content") throw new Error("Invalid Files download URL.");
        startDownload(url);
    }

    function revokeAll() { for (const id of groups.keys()) revoke(id); }
    async function drainAll() { await Promise.all([...groups.keys()].map(drain)); }
    function openPrivacyNotice(groupId) {
        if (!groups.get(groupId)?.active) throw new Error("Files request group is closed.");
        openNotice("/files-privacy/");
    }

    return { createGroup, enroll, invoke, cancel, revoke, drain, revokeAll, drainAll, openGoogleAuthorization,
        openPrivacyNotice, releaseUpload, openDownload };
}
