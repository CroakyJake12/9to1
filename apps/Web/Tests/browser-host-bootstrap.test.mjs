import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { runInNewContext } from "node:vm";

const host = new URL("../wwwroot/", import.meta.url);
const html = readFileSync(new URL("index.html", host), "utf8");
const source = readFileSync(new URL("host-bootstrap.js", host), "utf8");

function boot(getItem) {
    const marks = [];
    const styles = new Map();
    const events = new Map();
    const module = {};
    const retry = { hidden: true };
    const status = { textContent: "Opening 9to1…", dataset: { code: "Loading" } };
    const elements = {
        "nine-to-one-main": module,
        "browser-retry": retry,
        "browser-status": status,
    };
    let reloads = 0;
    runInNewContext(source, {
        performance: { mark: value => marks.push(value) },
        localStorage: { getItem },
        location: { reload: () => { reloads++; } },
        document: {
            documentElement: { style: { setProperty: (name, value) => styles.set(name, value) } },
            getElementById: id => elements[id] ?? null,
            addEventListener: (type, handler, capture) => events.set(type, { handler, capture }),
        },
    });
    return { marks, styles, events, module, retry, status, get reloads() { return reloads; } };
}

test("entry document uses only same-origin external scripts without inline events", () => {
    assert.doesNotMatch(html, /\son[a-z]+\s*=/i);
    const scripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)];
    assert.equal(scripts.length, 2);
    assert.ok(scripts.every(([, , inline]) => !inline.trim()));
    assert.match(scripts[0][1], /\bsrc="\.\/host-bootstrap\.js"/);
    assert.match(scripts[1][1], /\bid="nine-to-one-main"\s+type="module"\s+src="\.\/main\.js"/);
});

test("runs the host timing mark and restores only a valid cached palette", () => {
    const palette = ["#ffffff", "#0F0f0f", "#111111"];
    const good = boot(key => {
        assert.equal(key, "9to1.cui.host-palette.v1");
        return JSON.stringify(palette);
    });
    assert.deepEqual(good.marks, ["9to1:host"]);
    assert.deepEqual([...good.styles.keys()], ["--cui-boot-background", "--cui-boot-foreground", "--cui-boot-panel"]);
    assert.deepEqual([...good.styles.values()], palette);
    for (const getItem of [() => "{", () => JSON.stringify([["#ffffff"], ...palette.slice(1)]),
        () => { throw new Error("Storage unavailable"); }]) {
        const bad = boot(getItem);
        assert.deepEqual(bad.marks, ["9to1:host"]);
        assert.equal(bad.styles.size, 0);
        assert.ok(bad.events.has("error"));
    }
});

test("only the app module load error shows retry and clicking retry reloads the page", () => {
    const state = boot(() => null);
    const error = state.events.get("error");
    assert.equal(error.capture, true);
    error.handler({ target: {} });
    assert.equal(state.status.dataset.code, "Loading");
    assert.equal(state.retry.hidden, true);
    error.handler({ target: state.module });
    assert.equal(state.status.dataset.code, "BrowserResourceUnavailable");
    assert.equal(state.status.textContent, "9to1 could not load its resources. Check your connection and try again.");
    assert.equal(state.retry.hidden, false);
    const click = state.events.get("click").handler;
    click({ target: {} });
    assert.equal(state.reloads, 0);
    click({ target: state.retry });
    assert.equal(state.reloads, 1);
});
