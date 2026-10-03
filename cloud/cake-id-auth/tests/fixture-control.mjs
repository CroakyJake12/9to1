import { createInterface } from 'node:readline';
// Only the current caller's known input/signals: never discover or write another process's FDs.
export function installFixtureRelease(release) {
  const input = createInterface({input:process.stdin,terminal:false});
  const stop = () => release();
  const failed = () => { process.exitCode=1;release(); };
  input.on('line',line=>{if(line==='stop')stop();else if(line.trim())failed();});
  input.once('close',stop);
  input.once('error',failed);
  for(const signal of ['SIGINT','SIGTERM','SIGHUP'])process.once(signal,stop);
  return () => {
    for(const signal of ['SIGINT','SIGTERM','SIGHUP'])process.removeListener(signal,stop);
    input.close();process.stdin.pause();
  };
}
