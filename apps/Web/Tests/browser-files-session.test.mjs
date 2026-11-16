import assert from "node:assert/strict";
import test from "node:test";
import { setImmediate as turn } from "node:timers/promises";
import { createFilesSessionMonitor } from "../wwwroot/browser-files-session.js";

function fixture(fetch) {
    const window = new EventTarget();
    const document = new EventTarget();
    const channel = new EventTarget();
    const timeouts = new Map();
    let clock = 0;
    let period;
    let tick;
    let channelClosed = false;
    window.performance = { now: () => clock };
    window.fetch = fetch;
    window.setTimeout = (callback, delay) => {
        assert.equal(delay, 10_000);
        const token = Symbol();
        timeouts.set(token, callback);
        return token;
    };
    window.clearTimeout = token => timeouts.delete(token);
    window.setInterval = (callback, delay) => { tick = callback; period = delay; return 1; };
    window.clearInterval = () => { tick = undefined; };
    window.BroadcastChannel = function (name) {
        assert.equal(name, "9to1-account-session-v1");
        channel.close = () => { channelClosed = true; };
        return channel;
    };
    document.visibilityState = "visible";
    return { window, document, timeouts,
        get period() { return period; }, get closed() { return channelClosed; },
        advance(ms) { clock += ms; }, interval() { tick?.(); },
        focus() { window.dispatchEvent(new Event("focus")); },
        visibility(state) { document.visibilityState = state; document.dispatchEvent(new Event("visibilitychange")); },
        notice(data) { channel.dispatchEvent(new MessageEvent("message", { data })); },
    };
}

test("visible checks use the cookie owner endpoint and deduplicate focus with periodic checking", async () => {
    const calls = [];
    let responseCancelled = false;
    const f = fixture(async (path, options) => {
        calls.push({ path, options });
        const response = new ReadableStream({ cancel() { responseCancelled = true; } });
        return new Response(response, { status: calls.length === 1 ? 200 : 401 });
    });
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    f.focus();
    f.visibility("visible");
    await turn();
    assert.equal(f.period, 30_000);
    assert.equal(calls.length, 1);
    assert.equal(responseCancelled, true);
    assert.equal(calls[0].path, "/api/files/v1/context");
    assert.equal(calls[0].options.credentials, "same-origin");
    assert.equal(calls[0].options.cache, "no-store");
    assert.equal(calls[0].options.redirect, "error");
    f.focus();
    f.advance(30_000);
    f.interval();
    f.focus();
    await turn();
    assert.equal(calls.length, 2);
    assert.equal(losses, 1);
    assert.equal(monitor.invalidated, true);
    assert.equal(f.closed, true);
    await monitor.dispose();
});

test("the exact sign-out notice immediately fences an in-flight check and waits for its drain", async () => {
    let signal;
    const held = Promise.withResolvers();
    const f = fixture((_path, options) => { signal = options.signal; return held.promise; });
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    await turn();
    f.notice({ type: "signed-out", arbitrary: true });
    assert.equal(losses, 0);
    f.notice("signed-out");
    f.notice("signed-out");
    assert.equal(losses, 1);
    assert.equal(signal.aborted, true);
    let drained = false;
    const closing = monitor.dispose().then(() => { drained = true; });
    await turn();
    assert.equal(drained, false);
    held.resolve(new Response(null, { status: 403 }));
    await closing;
    assert.equal(losses, 1);
    assert.equal(f.timeouts.size, 0);
});

test("sessions-changed forces a server recheck, preserving valid sessions and clearing denied ones", async () => {
    const statuses = [200, 200, 403];
    let calls = 0;
    const f = fixture(async () => new Response(null, { status: statuses[calls++] }));
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    await turn();
    f.notice("sessions-changed");
    await turn();
    assert.equal(calls, 2);
    assert.equal(losses, 0, "a valid cookie session survives the notice");
    assert.equal(monitor.invalidated, false);
    f.notice("sessions-changed");
    await turn();
    assert.equal(calls, 3);
    assert.equal(losses, 1, "the Files owner endpoint remains authoritative");
    assert.equal(monitor.invalidated, true);
    await monitor.dispose();
});

test("sessions-changed notices during a pending check abort stale work and coalesce one fresh recheck", async () => {
    const calls = [];
    const f = fixture((_path, { signal }) => {
        const answer = Promise.withResolvers();
        calls.push({ signal, answer });
        return answer.promise;
    });
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    await turn();
    assert.equal(calls.length, 1);
    f.notice("sessions-changed");
    f.notice("sessions-changed");
    assert.equal(calls[0].signal.aborted, true);
    calls[0].answer.resolve(new Response(null, { status: 200 }));
    await turn();
    assert.equal(calls.length, 2, "multiple notices during one request queue only one follow-up");
    assert.equal(losses, 0, "the obsolete response cannot decide session validity");
    calls[1].answer.resolve(new Response(null, { status: 403 }));
    await turn();
    assert.equal(losses, 1);
    assert.equal(monitor.invalidated, true);
    await monitor.dispose();
});

test("a real focus event uses the ordinary debounce and does not abort an active check", async () => {
    const calls = [];
    const f = fixture((_path, { signal }) => {
        const answer = Promise.withResolvers();
        calls.push({ signal, answer });
        return answer.promise;
    });
    const monitor = createFilesSessionMonitor(f.window, f.document, () => {});
    await turn();
    assert.equal(calls.length, 1);
    f.focus();
    assert.equal(calls[0].signal.aborted, false);
    assert.equal(calls.length, 1, "the Event object is not treated as a force flag");
    f.notice("sessions-changed");
    assert.equal(calls[0].signal.aborted, true, "a session-change notice still forces a fresh server check");
    calls[0].answer.resolve(new Response(null, { status: 200 }));
    await turn();
    assert.equal(calls.length, 2);
    const closing = monitor.dispose();
    calls[1].answer.resolve(new Response(null, { status: 200 }));
    await closing;
});

test("hiding aborts the current check and a rapid return waits before one fresh check", async () => {
    const calls = [];
    const f = fixture((_path, { signal }) => {
        const answer = Promise.withResolvers();
        calls.push({ signal, answer });
        return answer.promise;
    });
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    await turn();
    f.visibility("hidden");
    assert.equal(calls[0].signal.aborted, true);
    f.interval();
    f.visibility("visible");
    f.focus();
    assert.equal(calls.length, 1);
    calls[0].answer.resolve(new Response(null, { status: 401 }));
    await turn();
    assert.equal(losses, 0);
    assert.equal(calls.length, 2);
    const dispose = monitor.dispose();
    assert.equal(calls[1].signal.aborted, true);
    calls[1].answer.resolve(new Response(null, { status: 200 }));
    await dispose;
    f.advance(30_000);
    f.focus();
    f.interval();
    f.notice("signed-out");
    assert.equal(calls.length, 2);
    assert.equal(losses, 0);
});

test("when BroadcastChannel is unavailable a visible server denial still fences the view", async () => {
    let calls = 0;
    const f = fixture(async () => new Response(null, { status: ++calls === 1 ? 503 : 403 }));
    f.window.BroadcastChannel = undefined;
    let losses = 0;
    const monitor = createFilesSessionMonitor(f.window, f.document, () => { losses++; });
    await turn();
    assert.equal(losses, 0);
    f.advance(30_000);
    f.interval();
    await turn();
    assert.equal(losses, 1);
    await monitor.dispose();
});
