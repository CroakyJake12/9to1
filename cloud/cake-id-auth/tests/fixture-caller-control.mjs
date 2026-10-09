import { installFixtureRelease } from './fixture-control.mjs';
// Test-only process workload: actual production launch function; no auth/provider emulation.
import { launchFixtureCustodian } from './fixture-launch.mjs';
import { spawn, spawnSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
const [mode, custodian, folder, ready, marker] = process.argv.slice(2);
const python = spawnSync('python3', ['-c', 'import sys; print(sys.executable)'], {encoding:'utf8'}).stdout.trim();
const command = [python, '-c', `import os,time;open(${JSON.stringify(marker)},'w').write(str(os.getpid()));time.sleep(60)`];
const worker = mode === 'baseline-loss'
  ? spawn('python3', [custodian, ...command], { cwd: folder, stdio: ['pipe','pipe','pipe','pipe'] })
  : launchFixtureCustodian(custodian, command, folder, `${folder}/receipt.json`);
let receipt = '';
worker.stdin.on('error', () => {});
worker.stdout.on('data', () => {});
worker.stderr.on('data', () => {});
worker.stdio[3].setEncoding('utf8').on('data', chunk => { receipt += chunk; });
const exited = new Promise((resolve,reject) => {
  worker.once('error', reject);
  worker.once('exit', code => { resolve(code); });
});
if (mode === 'handled') {
  let stopping = false;
  let closeCallerControl;
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    try {
      worker.stdin.end('stop\n');
      if (await exited !== 0) throw new Error('custodian failed');
      const proof = JSON.parse(receipt);
      if (!proof.strictReaped || !proof.originalsDisappeared) throw new Error('strict proof absent');
      writeFileSync(`${folder}/caller-finished.json`, JSON.stringify(proof), {flag:'wx',mode:0o600});
      process.exitCode = 0;
    } catch (error) { console.error(error.message); process.exitCode = 1; }
    finally { closeCallerControl?.(); }
  };
  closeCallerControl = installFixtureRelease(stop);
}
writeFileSync(ready, 'ready', {flag:'wx',mode:0o600});
