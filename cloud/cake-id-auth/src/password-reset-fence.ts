import type { GenericEndpointContext } from "@better-auth/core";
import { APIError } from "better-auth/api";
import { deleteSessionCookie } from "better-auth/cookies";
import { stripAccessTokenAuthorizationScheme } from "better-auth/oauth2";
import { decodeJwt } from "jose";
import type { Env } from "./env";

export const CREDENTIAL_GENERATION_CLAIM = "https://9to1.uk/credential_generation";
type CredentialUser = { id: string; credentialGeneration?: unknown };
type Issuance = { accountId: string; generation: number } | { machine: true };

function generation(value: unknown): number {
  const candidate = value ?? 0;
  if (typeof candidate !== "number" || !Number.isSafeInteger(candidate) || candidate < 0)
    throw new APIError("UNAUTHORIZED", { error: "invalid_token" });
  return candidate;
}
// Identical to the pinned provider's configured default (hashed, no prefix or
// custom token formatter). This is only for deleting a rejected minted grant;
// authentication and token verification stay with the maintained provider.
async function storedTokenHash(token: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(token)));
  return btoa(String.fromCharCode(...digest)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

export function createPasswordResetFence(env: Pick<Env, "DB">) {
  // Request-scoped state keyed by the provider AuthContext; no credentials are
  // cached here and separate requests cannot overwrite one another's snapshot.
  const issuance = new WeakMap<object, Issuance>();
  const current = async (accountId: string): Promise<number | null> => {
    const user = await env.DB.prepare("SELECT credentialGeneration FROM user WHERE id = ? LIMIT 1")
      .bind(accountId).first<{ credentialGeneration: number | null }>();
    return user ? generation(user.credentialGeneration) : null;
  };
  const removeIssuedGrant = async (accountId: string, data: Record<string, unknown>) => {
    const statements = [];
    if (typeof data.access_token === "string") statements.push(env.DB.prepare("DELETE FROM oauthAccessToken WHERE userId = ? AND token = ?")
      .bind(accountId, await storedTokenHash(data.access_token)));
    if (typeof data.refresh_token === "string") statements.push(env.DB.prepare("DELETE FROM oauthRefreshToken WHERE userId = ? AND token = ?")
      .bind(accountId, await storedTokenHash(data.refresh_token)));
    if (statements.length) {
      const results = await env.DB.batch(statements);
      if (results.some(result => !result.success)) throw new Error("rejected_grant_cleanup_failed");
    }
  };
  return {
    async reset(accountId: string): Promise<void> {
      // The generation fence and all old session/token rows change together.
      // Resource mutations that check live sessions cannot pass in a gap before
      // the provider's subsequent session-revocation backstop runs.
      const results = await env.DB.batch([
        env.DB.prepare("UPDATE user SET credentialGeneration = COALESCE(credentialGeneration, 0) + 1 WHERE id = ? AND COALESCE(credentialGeneration, 0) BETWEEN 0 AND 9007199254740990 RETURNING credentialGeneration").bind(accountId),
        env.DB.prepare("DELETE FROM oauthAccessToken WHERE userId = ?").bind(accountId),
        env.DB.prepare("DELETE FROM oauthRefreshToken WHERE userId = ?").bind(accountId),
        env.DB.prepare("DELETE FROM session WHERE userId = ?").bind(accountId),
      ]);
      if (results.length !== 4 || results.some(result => !result.success) || results[0].results.length !== 1)
        throw new Error("password_reset_revocation_failed");
    },
    claims(user: CredentialUser | null | undefined): Record<string, unknown> {
      return user ? { [CREDENTIAL_GENERATION_CLAIM]: generation(user.credentialGeneration) } : {};
    },
    async capture(context: object, user: CredentialUser | null | undefined, grantType: string, sessionId?: string): Promise<void> {
      if (!user) {
        if (grantType !== "client_credentials") throw new APIError("BAD_REQUEST", { error: "invalid_grant" });
        issuance.set(context, { machine: true }); return;
      }
      if (grantType === "authorization_code") {
        const active = sessionId && await env.DB.prepare("SELECT 1 FROM session WHERE id = ? AND userId = ? AND julianday(expiresAt) > julianday('now')")
          .bind(sessionId, user.id).first();
        if (!active) throw new APIError("BAD_REQUEST", { error: "invalid_grant" });
      }
      issuance.set(context, { accountId: user.id, generation: generation(user.credentialGeneration) });
    },
    async userinfo(user: CredentialUser, jwt: Record<string, unknown>): Promise<Record<string, never>> {
      // Missing claim means generation zero for credentials issued before this
      // additive change. The first reset invalidates those assertions as well.
      if (await current(user.id) !== generation(jwt[CREDENTIAL_GENERATION_CLAIM]))
        throw new APIError("UNAUTHORIZED", { error: "invalid_token" });
      return {};
    },
    async after(context: GenericEndpointContext): Promise<{ active: boolean } | void> {
      if (context.path === "/sign-in/email" || context.path === "/sign-in/username") {
        const signedIn = context.context.newSession;
        // newSession.user is the provider's pre-password-verification snapshot,
        // not a fresh read that could bless an already-stale password proof.
        if (signedIn && await current(signedIn.user.id) !== generation(signedIn.user.credentialGeneration)) {
          await context.context.internalAdapter.deleteSession(signedIn.session.token);
          deleteSessionCookie(context);
          throw new APIError("UNAUTHORIZED", { code: "SESSION_EXPIRED", message: "Please sign in again." });
        }
      }
      const returned: unknown = "returned" in context.context ? context.context.returned : undefined;
      if (context.path === "/oauth2/introspect" && returned && typeof returned === "object" && "active" in returned && returned.active === true) {
        // The maintained endpoint has already authenticated the introspecting
        // client and verified this token. Decode only that accepted JWT to keep
        // the canonical subject, before the provider's pairwise presentation.
        let claims;
        try { claims = decodeJwt(stripAccessTokenAuthorizationScheme(context.body.token)); }
        catch { return; } // Valid opaque/refresh tokens are checked by stored rows.
        if (typeof claims.sub !== "string") return context.json({ active: false });
        const latest = await current(claims.sub);
        // A client-credentials JWT has the client itself as subject, no user and
        // no credential-generation claim. Account recovery does not revoke it.
        if (latest === null && claims.sub === claims.azp && claims[CREDENTIAL_GENERATION_CLAIM] === undefined) return;
        let issuedGeneration;
        try { issuedGeneration = generation(claims[CREDENTIAL_GENERATION_CLAIM]); }
        catch { return context.json({ active: false }); }
        if (latest !== issuedGeneration) return context.json({ active: false });
        return;
      }
      if (context.path !== "/oauth2/token") return;
      if (!returned || typeof returned !== "object" || !("access_token" in returned) || typeof returned.access_token !== "string") return;
      const snapshot = issuance.get(context.context);
      issuance.delete(context.context);
      if (!snapshot) throw new APIError("BAD_REQUEST", { error: "invalid_grant" });
      if ("machine" in snapshot) return;
      if (await current(snapshot.accountId) !== snapshot.generation) {
        await removeIssuedGrant(snapshot.accountId, returned as Record<string, unknown>);
        throw new APIError("BAD_REQUEST", { error: "invalid_grant" });
      }
    },
  };
}
