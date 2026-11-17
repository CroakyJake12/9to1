// Exercises the original exported bridge, not a replica of its group registry.
// Bounded microtask interleavings cover request retirement between two callers
// closing the same view. No wall-clock sleeps decide whether an owner survived.
import assert from "node:assert/strict";
import test from "node:test";
import { setImmediate as turn } from "node:timers/promises";
import { createFilesBrowserBridge } from "../files-browser.js";

const outcome = promise => promise.then(value => ({ value }), error => ({ error }));
const close = async files => { files.revokeAll(); await files.drainAll(); };

async function reopenDuringOverlappingDrains(delay, firstAll, secondAll) {
    const entered = Promise.withResolvers();
    const response = Promise.withResolvers();
    let calls = 0;
    let losses = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: () => {
            if (++calls === 1) { entered.resolve(); return response.promise; }
            return Promise.resolve(Response.json({ reopened: true }));
        },
        onSessionLost: () => { losses++; },
    });
    files.createGroup("owner");
    files.enroll("owner");
    const original = outcome(files.invoke("owner", "request", "usage", "null"));
    let first;
    let second;
    try {
        await entered.promise;
        files.revoke("owner");
        first = firstAll ? files.drainAll() : files.drain("owner");
        response.resolve(Response.json({ obsolete: true }));
        // The tracked request can disappear from the request map before the
        // promise already captured by the first drain has finished settling.
        for (let index = 0; index < delay; index++) await Promise.resolve();
        second = secondAll ? files.drainAll() : files.drain("owner");
        await second;
        files.createGroup("owner");
        await first;
        let reopened;
        try {
            files.enroll("owner");
            const reply = JSON.parse(await files.invoke("owner", "request", "usage", "null"));
            reopened = reply.status === 200 && JSON.parse(reply.body).reopened === true;
        } catch { reopened = false; }
        assert.equal((await original).error?.name, "AbortError");
        assert.equal(losses, 0, "a cancelled old request must not revoke the new session");
        return reopened;
    } finally {
        response.resolve(Response.json({ cleanup: true }));
        await original;
        await Promise.allSettled([first, second]);
        await close(files);
    }
}

for (const [label, firstAll, secondAll] of [
    ["two group drains", false, false],
    ["global drain followed by group drain", true, false],
    ["group drain followed by global drain", false, true],
]) {
    test(`${label} preserve the reopened owner at every tested interleaving`, { timeout: 5000 }, async () => {
        const lostOwners = [];
        for (let delay = 0; delay < 32; delay++) {
            if (!await reopenDuringOverlappingDrains(delay, firstAll, secondAll)) lostOwners.push(delay);
        }
        assert.deepEqual(lostOwners, [], "late cleanup deleted a newly reopened group at these microtask offsets");
    });
}

test("every concurrent drain joins the original streamed cancellation before reuse", { timeout: 5000 }, async t => {
    const reading = Promise.withResolvers();
    const cancellation = Promise.withResolvers();
    let cancellations = 0;
    let calls = 0;
    const body = new ReadableStream({
        pull() { reading.resolve(); },
        cancel() { cancellations++; return cancellation.promise; },
    }, { highWaterMark: 0 });
    const files = createFilesBrowserBridge({
        fetchImpl: async () => ++calls === 1
            ? new Response(body, { headers: { "Content-Type": "application/json" } })
            : Response.json({ current: true }),
    });
    files.createGroup("owner");
    const request = outcome(files.invoke("owner", "stream", "usage", "null"));
    let drains = [];
    t.after(async () => {
        cancellation.resolve();
        await request;
        await Promise.allSettled(drains);
        await close(files);
    });
    await reading.promise;
    files.revoke("owner");
    let completed = 0;
    drains = [files.drain("owner"), files.drain("owner"), files.drainAll()]
        .map(work => work.then(() => { completed++; }));
    files.revoke("owner");
    await turn();
    assert.equal(cancellations, 1, "all close callers own the same response cancellation");
    assert.equal(completed, 0, "no caller may report drainage before cancellation settles");
    assert.throws(() => files.createGroup("owner"), /already exists/);
    cancellation.resolve();
    await Promise.all(drains);
    assert.equal((await request).error?.name, "AbortError");
    assert.equal(completed, 3);
    assert.equal(body.locked, false);
    files.createGroup("owner");
    files.enroll("owner");
    const reopened = JSON.parse(await files.invoke("owner", "stream", "usage", "null"));
    assert.deepEqual(JSON.parse(reopened.body), { current: true });
});

test("a rejected active drain does not poison later close and unrelated groups remain usable", { timeout: 5000 }, async t => {
    const files = createFilesBrowserBridge({ fetchImpl: async () => Response.json({ usable: true }) });
    t.after(() => close(files));
    files.createGroup("owner");
    files.createGroup("other");
    await assert.rejects(files.drain("owner"), /Revoke Files requests/);
    files.revoke("owner");
    await Promise.all([files.drain("owner"), files.drain("owner")]);
    await files.drain("missing");
    files.createGroup("owner");
    for (const id of ["owner", "other"]) {
        files.enroll(id);
        const reply = JSON.parse(await files.invoke(id, "usage", "usage", "null"));
        assert.deepEqual(JSON.parse(reply.body), { usable: true });
    }
});

test("settled successful and failed requests can be closed repeatedly without blocking reopen", { timeout: 5000 }, async t => {
    let calls = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: async () => {
            if (++calls === 2) throw new Error("original transport failure");
            return Response.json({ sequence: calls });
        },
    });
    t.after(() => close(files));
    files.createGroup("owner");
    await files.invoke("owner", "success", "usage", "null");
    await assert.rejects(files.invoke("owner", "failure", "usage", "null"), /original transport failure/);
    files.revokeAll();
    await Promise.all([files.drainAll(), files.drain("owner"), files.drainAll()]);
    files.createGroup("owner");
    const reply = JSON.parse(await files.invoke("owner", "success", "usage", "null"));
    assert.deepEqual(JSON.parse(reply.body), { sequence: 3 });
});
