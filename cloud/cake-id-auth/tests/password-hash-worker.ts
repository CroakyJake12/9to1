import { hashPassword, verifyPassword } from "better-auth/crypto";

const syntheticPassword = "Synthetic-Workerd-Password-9!";

export default {
  async fetch(request: Request): Promise<Response> {
    if (new URL(request.url).pathname !== "/__test/password-hash") return new Response("Not found", { status: 404 });

    const hashStartedAt = performance.now();
    const hash = await hashPassword(syntheticPassword);
    const hashDurationMs = performance.now() - hashStartedAt;
    const [salt, derivedKey] = hash.split(":");
    const verifyStartedAt = performance.now();
    const correctPasswordAccepted = await verifyPassword({ hash, password: syntheticPassword });
    const wrongPasswordRejected = !(await verifyPassword({ hash, password: "Different-synthetic-password" }));
    const verifyDurationMs = performance.now() - verifyStartedAt;

    return Response.json({
      correctPasswordAccepted,
      wrongPasswordRejected,
      saltBytes: salt.length / 2,
      derivedKeyBytes: derivedKey.length / 2,
      hashDurationMs,
      verifyDurationMs,
    });
  },
};
