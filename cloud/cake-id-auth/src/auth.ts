import { oauthProvider } from "@better-auth/oauth-provider";
import { betterAuth, type BetterAuthOptions } from "better-auth";
import { jwt, username } from "better-auth/plugins";
import { AUTH_BASE_PATH, ACCESS_TOKEN_SECONDS, AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";

function required(value: string | undefined, key: string): string {
  if (!value || value.trim().length < 32 && key.endsWith("SECRET")) {
    throw new Error(`Missing or too-short ${key}`);
  }
  return value;
}

function assertSafeServiceUrl(value: string, key: string, localOnly: boolean): void {
  const url = new URL(value);
  if (url.username || url.password || url.search || url.hash) throw new Error(`${key} must not include credentials, query parameters, or a fragment`);
  const loopback = ["127.0.0.1", "localhost", "[::1]"].includes(url.hostname);
  if (localOnly) {
    if (url.protocol !== "http:" || !loopback) throw new Error(`${key} must use an HTTP loopback URL in local-test-only mode`);
    return;
  }
  if (url.protocol !== "https:") throw new Error(`${key} must use HTTPS outside local-test-only mode`);
  if (key === "AUTH_BASE_URL" && url.pathname !== "/") throw new Error("AUTH_BASE_URL must be an origin without a path");
}

async function deliverEmail(env: Env, message: { to: string; subject: string; text: string }): Promise<void> {
  if (env.APP_MODE === "local-test-only" && env.EMAIL_CAPTURE === "true") {
    await env.DB.prepare(
      "INSERT INTO cake_email_outbox (recipient, subject, body, createdAt) VALUES (?, ?, ?, ?)",
    ).bind(message.to.toLowerCase(), message.subject, message.text, Date.now()).run();
    return;
  }

  throw new Error("CAKE ID email delivery is not configured");
}

export function createAuthOptions(env: Env): BetterAuthOptions {
  const baseURL = required(env.AUTH_BASE_URL, "AUTH_BASE_URL").replace(/\/$/, "");
  const apiResource = required(env.API_RESOURCE, "API_RESOURCE");
  const localOnly = env.APP_MODE === "local-test-only";
  assertSafeServiceUrl(baseURL, "AUTH_BASE_URL", localOnly);
  assertSafeServiceUrl(apiResource, "API_RESOURCE", localOnly);
  const db = env.DB;

  // The authenticated session supplies the canonical ID; private role is never public output.
  async function hasCanonicalAdminRole(user: { id?: unknown } | null | undefined): Promise<boolean> {
    if (!user || typeof user.id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(user.id)) return false;
    const canonical = await db.prepare("SELECT role FROM user WHERE id = ? LIMIT 1").bind(user.id).first<{ role: string | null }>();
    return canonical?.role === "admin";
  }

  return {
    appName: "CAKE ID",
    baseURL,
    basePath: AUTH_BASE_PATH,
    secret: required(env.AUTH_SECRET, "AUTH_SECRET"),
    database: db,
    trustedOrigins: [baseURL],
    advanced: {
      database: { generateId: "uuid" },
    },
    session: {
      expiresIn: 60 * 60 * 24 * 30,
      updateAge: 60 * 60 * 24,
      cookieCache: { enabled: false },
    },
    disabledPaths: ["/update-user", "/update-username"],
    emailAndPassword: {
      enabled: true,
      minPasswordLength: 12,
      maxPasswordLength: 128,
      requireEmailVerification: true,
      autoSignIn: false,
      resetPasswordTokenExpiresIn: 30 * 60,
      sendResetPassword: async ({ user, url }) => {
        await deliverEmail(env, {
          to: user.email,
          subject: "Reset your CAKE ID password",
          text: `Use this single-use link to reset your password: ${url}`,
        });
      },
    },
    emailVerification: {
      sendOnSignUp: true,
      expiresIn: 60 * 60,
      sendVerificationEmail: async ({ user, url }) => {
        await deliverEmail(env, {
          to: user.email,
          subject: "Verify your CAKE ID email",
          text: `Verify your email address: ${url}`,
        });
      },
    },
    user: {
      additionalFields: {
        pronouns: { type: "string", required: false, input: true },
        jobTitle: { type: "string", required: false, input: true },
        role: { type: "string", required: false, input: false, returned: false, defaultValue: "user" },
        profileRevision: { type: "number", required: false, input: false, returned: false, defaultValue: 1 },
      },
    },
    rateLimit: {
      enabled: true,
      window: 60,
      max: 100,
    },
    plugins: [
      username({
        usernameNormalization: (value) => value.toLowerCase(),
        validationOrder: { username: "post-normalization" },
        usernameValidator: async (value) => {
          if (!/^[a-z0-9_]{3,30}$/.test(value)) return false;
          const reservation = await db
            .prepare("SELECT 1 FROM cake_reserved_usernames WHERE username = ? COLLATE NOCASE LIMIT 1")
            .bind(value)
            .first();
          return !reservation;
        },
      }),
      jwt(),
      oauthProvider({
        scopes: [...AUTHORIZATION_SCOPES],
        resources: [{ identifier: apiResource, accessTokenTtl: ACCESS_TOKEN_SECONDS, allowedScopes: [...AUTHORIZATION_SCOPES.filter((scope) => scope.startsWith("cake:"))] }],
        resourceSeedMode: "insertOnly",
        enforcePerClientResources: true,
        clientRegistrationDefaultResources: [apiResource],
        clientRegistrationAllowedResources: [],
        allowDynamicClientRegistration: false,
        accessTokenExpiresIn: ACCESS_TOKEN_SECONDS,
        idTokenExpiresIn: ACCESS_TOKEN_SECONDS,
        loginPage: "/sign-in",
        consentPage: "/consent",
        clientPrivileges: ({ user }) => hasCanonicalAdminRole(user),
        resourcePrivileges: ({ user }) => hasCanonicalAdminRole(user),
      }),
    ],
  };
}

export function createAuth(env: Env) {
  return betterAuth(createAuthOptions(env));
}
