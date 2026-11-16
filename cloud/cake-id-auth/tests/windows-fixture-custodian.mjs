import { spawn } from "node:child_process";
import { writeSync } from "node:fs";
import { stripNodeInjectionEnvironment } from "./fixture-launch.mjs";

const [journalOption, executable, ...args] = process.argv.slice(2);
if (journalOption !== "--receipt-fd=4" || executable !== process.execPath ||
    args.length !== 2 || args[0] !== "-e" || args[1] !== "setInterval(()=>{},1000)") {
  throw new Error("Windows test custody accepts only its fixed direct-child lifecycle fixture.");
}

// This helper proves ownership of one direct child for fixture-launch.test.mjs.
// The reusable Workerd/D1 fixture remains Linux-only because this is not Job Object tree custody.
const childEnv = stripNodeInjectionEnvironment(process.env);
const child = spawn(executable, args, { env: childEnv, stdio: "ignore", windowsHide: true });
await new Promise((resolve, reject) => {
  child.once("spawn", resolve);
  child.once("error", reject);
});

let closed;
const childClosed = new Promise(resolve => {
  child.once("close", (code, signal) => { closed = { code, signal }; resolve(); });
});
async function waitForChildClose() {
  const stopped = await Promise.race([
    childClosed.then(() => true),
    new Promise(resolve => { const timer = setTimeout(() => resolve(false), 5000); timer.unref(); }),
  ]);
  return stopped && Boolean(closed);
}

let input = "";
try {
  await new Promise((resolve, reject) => {
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", chunk => {
      input += chunk;
      if (!input.includes("\n")) return;
      const [command, ...extra] = input.split("\n");
      if (command !== "stop" || extra.some(value => value.length > 0)) {
        reject(new Error("Unknown Windows direct-child custody command."));
        return;
      }
      resolve();
    });
    process.stdin.once("end", () => {
      if (!input.includes("\n")) reject(new Error("Windows direct-child stop command was not received."));
    });
  });

  if (child.exitCode === null && child.signalCode === null) child.kill();
  if (!await waitForChildClose()) throw new Error("Owned Windows fixture child did not close.");

  const proof = {
    custody: "owned-direct-child-handle",
    childPid: child.pid,
    directChildExited: true,
    exitCode: closed.code,
    signal: closed.signal,
    processTreeCustody: false,
  };
  const data = Buffer.from(`${JSON.stringify(proof)}\n`);
  writeSync(4, data);
  writeSync(3, data);
} finally {
  if (!closed && child.exitCode === null && child.signalCode === null) child.kill();
  if (!closed) await waitForChildClose();
}
