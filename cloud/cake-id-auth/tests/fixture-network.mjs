import { createServer } from "node:net";
export function fixturePort(portText = "8798") {
  if (!/^[1-9][0-9]{3,4}$/.test(portText) || Number(portText) < 1024 || Number(portText) > 65535 || Number(portText) === 5096) {
    throw new Error("Fixture port must be an unprivileged loopback port distinct from the client page; no state created");
  }
  return Number(portText);
}
export async function assertFixturePortFree(port) {
  await new Promise((resolve, reject) => {
    const probe = createServer();
    probe.once("error", reject);
    probe.listen({ host: "127.0.0.1", port, exclusive: true }, () => probe.close(resolve));
  });
}
