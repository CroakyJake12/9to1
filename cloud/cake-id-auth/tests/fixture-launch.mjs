import { spawn } from "node:child_process";
import { openSync, closeSync } from "node:fs";
import { fileURLToPath } from "node:url";

const nodeInjectionVariables = new Set(["NODE_OPTIONS", "NODE_PATH", "NODE_REPL_EXTERNAL_MODULE"]);

export function stripNodeInjectionEnvironment(environment) {
  return Object.fromEntries(Object.entries(environment)
    .filter(([name]) => !nodeInjectionVariables.has(name.toUpperCase())));
}

// Exclusive private journal; injected operations are used only by isolated lifecycle controls.
export function launchFixtureCustodian(custodianPath, command, cwd, receiptPath, operations = {}) {
  const journal = openSync(receiptPath, "wx", 0o600);
  let worker, spawnFailure, closeFailure;
  try {
    const windowsDirectChild = process.platform === "win32";
    const executable = windowsDirectChild ? process.execPath : "python3";
    const args = windowsDirectChild
      ? [fileURLToPath(new URL("./windows-fixture-custodian.mjs", import.meta.url)), "--receipt-fd=4", ...command]
      : [custodianPath, "--receipt-fd=4", ...command];
    const env = windowsDirectChild ? stripNodeInjectionEnvironment(operations.env ?? process.env) : undefined;
    worker = (operations.spawn ?? spawn)(executable, args, {
      cwd, detached: !windowsDirectChild, windowsHide: windowsDirectChild, env,
      stdio: ["pipe", "pipe", "pipe", "pipe", journal],
    });
    worker.fixtureCustodyMode = windowsDirectChild ? "owned-direct-child" : "linux-process-tree";
  } catch (error) { spawnFailure = error; }
  try { (operations.close ?? closeSync)(journal); }
  catch (error) { closeFailure = error; }
  if (worker) {
    // Never throw away an already spawned owned handle; caller installs control before raising setup failure.
    worker.fixtureLaunchError = closeFailure;
    return worker;
  }
  if (closeFailure) {
    const error = new AggregateError(spawnFailure ? [spawnFailure, closeFailure] : [closeFailure],
      "Original spawn and journal-close failures retained", { cause: spawnFailure ?? closeFailure });
    error.fixtureJournalCloseError = closeFailure;
    throw error;
  }
  if (spawnFailure) throw spawnFailure;
  throw closeFailure ?? new Error("Custodian spawn returned no owned handle");
}
