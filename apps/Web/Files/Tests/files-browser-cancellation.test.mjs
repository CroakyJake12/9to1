import assert from "node:assert/strict";
import test from "node:test";
import { setImmediate as turn } from "node:timers/promises";
import { createFilesBrowserBridge } from "../files-browser.js";

const jsonHeaders = { "Content-Type": "application/json" };
const outcome = promise => promise.then(value => ({ value }), error => ({ error }));
const close = async files => { files.revokeAll(); await files.drainAll(); };

for (const status of [401, 403]) {
    for (const action of ["usage", "mutate", "context"]) {
        test(`cancelled ${action} cannot invalidate its owner from a late ${status}`, async t => {
            const response = Promise.withResolvers();
            const entered = Promise.withResolvers();
            let losses = 0;
            let cancelled = false;
            let calls = 0;
            const files = createFilesBrowserBridge({
                fetchImpl: async () => {
                    if (++calls === 1) { entered.resolve(); return response.promise; }
                    return Response.json({ stillCurrent: true });
                },
                onSessionLost: () => { losses++; },
            });
            t.after(() => close(files));
            files.createGroup("owner");
            files.enroll("owner");
            const original = outcome(files.invoke("owner", "old", action, "{}"));
            await entered.promise;
            files.cancel("owner", "old");
            response.resolve(new Response(new ReadableStream({
                cancel() { cancelled = true; },
            }), { status, headers: jsonHeaders }));
            const result = await original;
            assert.equal(result.error?.name, "AbortError");
            assert.equal(cancelled, true, "the obsolete response is still drained");
            assert.equal(losses, 0, "only a current denial may invalidate the session");
            const next = JSON.parse(await files.invoke("owner", "new", "usage", "null"));
            assert.equal(next.status, 200);
        });
    }
}

test("a late successful response is cancelled without reading private JSON", async t => {
    const reply = Promise.withResolvers();
    const entered = Promise.withResolvers();
    let pulls = 0;
    let cancellations = 0;
    const files = createFilesBrowserBridge({ fetchImpl: () => { entered.resolve(); return reply.promise; } });
    t.after(() => close(files));
    files.createGroup("owner");
    const request = outcome(files.invoke("owner", "old", "usage", "null"));
    await entered.promise;
    files.cancel("owner", "old");
    reply.resolve(new Response(new ReadableStream({
        pull(controller) {
            pulls++;
            controller.enqueue(new TextEncoder().encode('{"private":"obsolete"}'));
            controller.close();
        },
        cancel() { cancellations++; },
    }, { highWaterMark: 0 }), { headers: jsonHeaders }));
    assert.equal((await request).error?.name, "AbortError");
    assert.equal(pulls, 0, "cancelled work must not start reading its response");
    assert.equal(cancellations, 1);
});

test("cancelling during JSON streaming cancels the reader and releases its lock", async t => {
    const reading = Promise.withResolvers();
    let streamController;
    let cancelled = false;
    const body = new ReadableStream({
        start(controller) { streamController = controller; },
        pull() { reading.resolve(); },
        cancel() { cancelled = true; },
    }, { highWaterMark: 0 });
    const files = createFilesBrowserBridge({ fetchImpl: async () => new Response(body, { headers: jsonHeaders }) });
    files.createGroup("owner");
    const result = outcome(files.invoke("owner", "stream", "usage", "null"));
    t.after(async () => {
        if (!cancelled) streamController.close(); // Also clean up the failing original implementation.
        await result;
        await close(files);
    });
    await reading.promise;
    files.cancel("owner", "stream");
    await turn();
    assert.equal(cancelled, true, "abort must cancel a pending body read, not wait for more JSON");
    assert.equal((await result).error?.name, "AbortError");
    assert.equal(body.locked, false);
});

test("group drain joins the actual asynchronous reader cancellation", async t => {
    const reading = Promise.withResolvers();
    const cancellation = Promise.withResolvers();
    let streamController;
    let cancelled = false;
    const body = new ReadableStream({
        start(controller) { streamController = controller; },
        pull() { reading.resolve(); },
        cancel() { cancelled = true; return cancellation.promise; },
    }, { highWaterMark: 0 });
    const files = createFilesBrowserBridge({ fetchImpl: async () => new Response(body, { headers: jsonHeaders }) });
    files.createGroup("owner");
    const result = outcome(files.invoke("owner", "stream", "usage", "null"));
    t.after(async () => {
        cancellation.resolve();
        if (!cancelled) streamController.close();
        await result;
        await close(files);
    });
    await reading.promise;
    files.revoke("owner");
    let drained = false;
    const draining = files.drain("owner").then(() => { drained = true; });
    await turn();
    assert.equal(cancelled, true);
    assert.equal(drained, false, "the cancellation acknowledgement is still held");
    cancellation.resolve();
    assert.equal((await result).error?.name, "AbortError");
    await draining;
    assert.equal(drained, true);
    assert.equal(body.locked, false);
});

test("a current denial still invalidates the owner while another read is cancelled", async t => {
    const files = createFilesBrowserBridge({
        fetchImpl: async () => Response.json({}, { status: 401 }),
        onSessionLost: () => { losses++; },
    });
    let losses = 0;
    t.after(() => close(files));
    files.createGroup("owner");
    files.enroll("owner");
    const cancelled = outcome(files.invoke("owner", "old", "usage", "null"));
    files.cancel("owner", "old");
    assert.equal((await cancelled).error?.name, "AbortError");
    const denied = JSON.parse(await files.invoke("owner", "current", "context", "null"));
    assert.deepEqual(denied, { status: 401, body: "" });
    assert.equal(losses, 1);
    await assert.rejects(files.invoke("owner", "blocked", "usage", "null"));
});

for (const [label, bytes, expected] of [
    ["oversized", new Uint8Array(1024 * 1024 + 1), /oversized response/],
    ["invalid UTF-8", Uint8Array.of(0xff), /encoded data/],
]) {
    test(`${label} JSON still fails and drains the response`, async t => {
        let cancelled = false;
        let sent = false;
        const body = new ReadableStream({
            pull(controller) {
                if (!sent) { sent = true; controller.enqueue(bytes); }
            },
            cancel() { cancelled = true; },
        }, { highWaterMark: 0 });
        const files = createFilesBrowserBridge({ fetchImpl: async () => new Response(body, { headers: jsonHeaders }) });
        t.after(() => close(files));
        files.createGroup("owner");
        await assert.rejects(files.invoke("owner", "invalid", "usage", "null"), expected);
        assert.equal(cancelled, true);
        assert.equal(body.locked, false);
    });
}

test("split UTF-8 and a completed read preserve the exact response body", async t => {
    const text = '{"name":"caf\u00e9 \ud83d\udcc1"}';
    const bytes = new TextEncoder().encode(text);
    const body = new ReadableStream({
        start(controller) {
            for (const byte of bytes) controller.enqueue(Uint8Array.of(byte));
            controller.close();
        },
    });
    const files = createFilesBrowserBridge({ fetchImpl: async () => new Response(body, { headers: jsonHeaders }) });
    t.after(() => close(files));
    files.createGroup("owner");
    const response = JSON.parse(await files.invoke("owner", "valid", "usage", "null"));
    assert.deepEqual(response, { status: 200, body: text });
    assert.equal(body.locked, false);
    files.cancel("owner", "valid"); // Settled requests no longer own an abort listener.
});
