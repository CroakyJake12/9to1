import assert from "node:assert/strict";
import { finishIntegrationCleanup } from "./integration-lifecycle.mjs";
const original = new Error("original assertion failure");
const drain = new Error("strict original-family drain ambiguous");
let deleted = false;
await assert.rejects(finishIntegrationCleanup(original, async () => {
  await (async () => { throw drain; })();
  deleted = true;
}), error => error instanceof AggregateError && error.errors.length === 2 &&
  error.errors[0] === original && error.errors[1] === drain && error.cause === drain);
assert.equal(deleted, false);
await assert.rejects(finishIntegrationCleanup(undefined, async () => { throw drain; }),
  error => error.errors.length === 1 && error.errors[0] === drain);
await finishIntegrationCleanup(original, async () => { deleted = true; });
assert.equal(deleted, true);
console.log("PASS: original exception identities retained; ambiguous drain retains state; successful cleanup preserved");
