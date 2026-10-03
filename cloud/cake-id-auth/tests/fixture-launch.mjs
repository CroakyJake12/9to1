import { spawn } from "node:child_process";
import { openSync, closeSync } from "node:fs";

// The caller owns this exclusively created private journal; the controlled child never inherits it.
export function launchFixtureCustodian(custodianPath, command, cwd, receiptPath) {
  const journal = openSync(receiptPath, "wx", 0o600);
  try {
    return spawn("python3", [custodianPath, "--receipt-fd=4", ...command], {
      cwd, detached: true, stdio: ["pipe", "pipe", "pipe", "pipe", journal],
    });
  } finally { closeSync(journal); }
}
