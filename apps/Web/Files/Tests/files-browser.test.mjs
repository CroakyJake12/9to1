import assert from "node:assert/strict";
import test from "node:test";
import { createFilesBrowserBridge } from "../files-browser.js";

const parentId = "9a043a89-f518-4c97-9a7c-296fb1a194b7";

test("the bridge uses only fixed same-origin routes and does not follow redirects", async () => {
    const requests = [];
    const files = createFilesBrowserBridge({ fetchImpl: async (path, init) => {
        requests.push({ path, init });
        return Response.json({ items: [] });
    } });
    files.createGroup("owner");
    await files.invoke("owner", "list", "list", JSON.stringify({ parentId, query: "A&B?" }));
    const request = requests[0];
    const url = new URL(request.path, "https://9to1.uk");
    assert.equal(url.pathname, "/api/files/v1/items");
    assert.equal(url.searchParams.get("parentId"), parentId);
    assert.equal(url.searchParams.get("query"), "A&B?");
    assert.equal(request.init.redirect, "error");
    assert.equal(request.init.credentials, "same-origin");
    assert.equal(request.init.cache, "no-store");
    await assert.rejects(files.invoke("owner", "bad", "get", JSON.stringify({ id: "../context" })));
    await assert.rejects(files.invoke("owner", "invalid", "https://example.com/", "{}"));
    assert.equal(requests.length, 1);
});

test("revoke rejects fresh calls and drain waits for the original request to settle", async () => {
    let started;
    const waitingForFetch = new Promise(resolve => { started = resolve; });
    let finish;
    const pending = new Promise(resolve => { finish = resolve; });
    const files = createFilesBrowserBridge({ fetchImpl: (_path, { signal }) => {
        started(signal);
        return pending;
    } });
    files.createGroup("owner");
    const issued = files.invoke("owner", "original", "usage", "null");
    const rejectedIssued = assert.rejects(issued, { name: "AbortError" });
    const signal = await waitingForFetch;
    files.revoke("owner");
    assert.equal(signal.aborted, true);
    await assert.rejects(files.invoke("owner", "fresh", "usage", "null"));
    let drained = false;
    const drain = files.drain("owner").then(() => { drained = true; });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(drained, false);
    finish(Response.json({}));
    await rejectedIssued;
    await drain;
});

test("session loss fences the request group and notifies the host once", async () => {
    let losses = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: async () => Response.json({ code: "PermissionDenied" }, { status: 403 }),
        onSessionLost: () => { losses++; },
    });
    files.createGroup("owner");
    const response = await files.invoke("owner", "operation", "mutate", "{}");
    assert.deepEqual(JSON.parse(response), { status: 403, body: "" });
    assert.equal(losses, 1);
    await assert.rejects(files.invoke("owner", "blocked", "usage", "null"));
    await files.drain("owner");
});

test("the disconnected admission probe does not invalidate another owner context", async () => {
    const files = createFilesBrowserBridge({
        fetchImpl: async () => Response.json({}, { status: 401 }),
        onSessionLost: () => assert.fail("An unregistered probe must not invalidate the host."),
    });
    files.createGroup("probe");
    const response = await files.invoke("probe", "context", "context", "null");
    assert.equal(JSON.parse(response).status, 401);
    files.revoke("probe");
    await files.drain("probe");
});

test("OAuth navigation allows only the expected Google authorization URL", () => {
    const urls = [];
    const scheduled = [];
    const files = createFilesBrowserBridge({ navigate: url => urls.push(url), scheduleNavigation: work => scheduled.push(work) });
    files.createGroup("owner");
    assert.throws(() => files.openGoogleAuthorization("owner", "https://accounts.google.com.example.com/o/oauth2/v2/auth"));
    assert.throws(() => files.openGoogleAuthorization("owner", "https://accounts.google.com/other"));
    assert.equal(scheduled.length, 0);
    const allowed = "https://accounts.google.com/o/oauth2/v2/auth?state=one-use-state";
    files.openGoogleAuthorization("owner", allowed);
    assert.deepEqual(urls, []);
    assert.equal(scheduled.length, 1);
    scheduled[0]();
    assert.deepEqual(urls, [allowed]);
});

test("a scheduled Google navigation can no longer run once its private group is revoked", async () => {
    const scheduled = [];
    const files = createFilesBrowserBridge({
        navigate: () => assert.fail("Revoked authorization must not navigate."),
        scheduleNavigation: work => scheduled.push(work),
    });
    files.createGroup("owner");
    files.openGoogleAuthorization("owner", "https://accounts.google.com/o/oauth2/v2/auth?state=one-use-state");
    files.revoke("owner");
    await files.drain("owner");
    files.createGroup("owner");
    assert.equal(scheduled.length, 1);
    scheduled[0]();
});

test("disconnect preserves the server's typed outcome for local revocation without Google acknowledgement", async () => {
    const files = createFilesBrowserBridge({ fetchImpl: async (url, init) => {
        assert.equal(url, "/api/files/v1/disconnect");
        assert.equal(init.method, "POST");
        return Response.json({ code: "DriveRevocationUnconfirmed" }, { status: 503 });
    } });
    files.createGroup("owner");
    const result = JSON.parse(await files.invoke("owner", "disconnect", "disconnect", "null"));
    assert.equal(result.status, 503);
    assert.equal(JSON.parse(result.body).code, "DriveRevocationUnconfirmed");
});

test("the Files privacy action opens only the local notice and refuses a revoked group", () => {
    const urls = [];
    const files = createFilesBrowserBridge({ openNotice: url => urls.push(url) });
    files.createGroup("owner");
    files.openPrivacyNotice("owner", "https://example.com/");
    files.revoke("owner");
    assert.throws(() => files.openPrivacyNotice("owner"));
    assert.deepEqual(urls, ["/files-privacy/"]);
});
