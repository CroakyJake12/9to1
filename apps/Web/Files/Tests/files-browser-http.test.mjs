// Exercises the production bridge with Node's real fetch and a loopback HTTP
// server. This is transport evidence, not authenticated hosted Files acceptance.
import assert from "node:assert/strict";
import test from "node:test";
import { createServer } from "node:http";
import { once } from "node:events";
import { setImmediate as turn } from "node:timers/promises";
import { createFilesBrowserBridge } from "../files-browser.js";

async function serverFixture(t, handler) {
    const server = createServer(handler);
    server.listen(0, "127.0.0.1");
    await once(server, "listening");
    t.after(() => new Promise((resolve, reject) => {
        server.close(error => error ? reject(error) : resolve());
        server.closeAllConnections();
    }));
    return `http://127.0.0.1:${server.address().port}`;
}

test("real HTTP JSON passes through the original Files bridge", { timeout: 5000 }, async t => {
    const seen = [];
    const origin = await serverFixture(t, (request, response) => {
        seen.push({ url: request.url, accept: request.headers.accept });
        response.writeHead(200, { "Content-Type": "application/json" });
        response.write('{"name":');
        response.end('"caf\u00e9"}');
    });
    const files = createFilesBrowserBridge({ fetchImpl: (path, options) => fetch(new URL(path, origin), options) });
    files.createGroup("owner");
    try {
        const reply = JSON.parse(await files.invoke("owner", "usage", "usage", "null"));
        assert.equal(reply.status, 200);
        assert.deepEqual(JSON.parse(reply.body), { name: "caf\u00e9" });
        assert.deepEqual(seen, [{ url: "/api/files/v1/usage", accept: "application/json" }]);
    } finally { files.revokeAll(); await files.drainAll(); }
});

test("a real HTTP denial still revokes the current private group", { timeout: 5000 }, async t => {
    const origin = await serverFixture(t, (_request, response) => {
        response.writeHead(403, { "Content-Type": "application/json" });
        response.end('{"code":"PermissionDenied"}');
    });
    let losses = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: (path, options) => fetch(new URL(path, origin), options),
        onSessionLost: () => { losses++; },
    });
    files.createGroup("owner");
    try {
        const reply = JSON.parse(await files.invoke("owner", "usage", "usage", "null"));
        assert.deepEqual(reply, { status: 403, body: "" });
        assert.equal(losses, 1);
        await assert.rejects(files.invoke("owner", "blocked", "usage", "null"));
    } finally { files.revokeAll(); await files.drainAll(); }
});

test("cancelling a real HTTP body aborts the request and permits group drain", { timeout: 5000 }, async t => {
    const headersReceived = Promise.withResolvers();
    const disconnected = Promise.withResolvers();
    const origin = await serverFixture(t, (_request, response) => {
        response.on("close", () => disconnected.resolve());
        response.writeHead(200, { "Content-Type": "application/json" });
        response.write('{"private":"'); // Deliberately leave the body open.
    });
    let responseBody;
    let losses = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: async (path, options) => {
            const response = await fetch(new URL(path, origin), options);
            responseBody = response.body;
            headersReceived.resolve();
            return response;
        },
        onSessionLost: () => { losses++; },
    });
    files.createGroup("owner");
    const request = files.invoke("owner", "stream", "usage", "null");
    const rejected = assert.rejects(request, { name: "AbortError" });
    try {
        await headersReceived.promise;
        await turn();
        files.cancel("owner", "stream");
        await rejected;
        assert.equal(responseBody.locked, false);
        assert.equal(losses, 0);
        await disconnected.promise;
    } finally { files.revokeAll(); await files.drainAll(); }
});
