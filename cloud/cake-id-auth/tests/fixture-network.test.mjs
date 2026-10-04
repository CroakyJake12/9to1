import assert from "node:assert/strict";
import { createServer } from "node:net";
import { fixturePort, assertFixturePortFree } from "./fixture-network.mjs";
assert.equal(fixturePort(), 8798);
assert.equal(fixturePort("8799"), 8799);
for (const port of ["0","1023","65536","5096","8799/path","http://127.0.0.1:8799"," 8799","8799\n"])
  assert.throws(() => fixturePort(port), /Fixture port/);
const listener = createServer();
await new Promise(resolve => listener.listen(0, "127.0.0.1", resolve));
const port = listener.address().port;
try { await assert.rejects(assertFixturePortFree(port), error => error.code === "EADDRINUSE"); }
finally { await new Promise(resolve => listener.close(resolve)); }
await assertFixturePortFree(port);
console.log("PASS: default/alternate ports, 8 invalid cases, actual busy loopback refusal and free-port success");
