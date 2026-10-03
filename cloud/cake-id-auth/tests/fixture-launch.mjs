import { spawn } from "node:child_process";
import { openSync, closeSync } from "node:fs";

// Exclusive private journal; injected operations are used only by isolated lifecycle controls.
export function launchFixtureCustodian(custodianPath, command, cwd, receiptPath, operations = {}) {
  const journal = openSync(receiptPath, "wx", 0o600);
  let worker, spawnFailure, closeFailure;
  try {
    worker = (operations.spawn ?? spawn)("python3", [custodianPath, "--receipt-fd=4", ...command], {
      cwd, detached: true, stdio: ["pipe", "pipe", "pipe", "pipe", journal],
    });
  } catch (error) { spawnFailure = error; }
  try { (operations.close ?? closeSync)(journal); }
  catch (error) { closeFailure = error; }
  if (worker) {
    // Never throw away an already spawned owned handle; caller installs control before raising setup failure.
    worker.fixtureLaunchError = closeFailure;
    return worker;
  }
  if (spawnFailure && closeFailure) throw new AggregateError([spawnFailure, closeFailure],
    "Original spawn and journal-close failures retained", { cause: spawnFailure });
  if (spawnFailure) throw spawnFailure;
  throw closeFailure ?? new Error("Custodian spawn returned no owned handle");
}
