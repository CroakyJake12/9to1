import { oauthProvider } from "@better-auth/oauth-provider";
import { betterAuth, type BetterAuthOptions } from "better-auth";
import { APIError, createAuthMiddleware } from "better-auth/api";
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
  // Private role fields are intentionally omitted from session/user API output.
  // Evaluate privileged administration from the canonical server row instead.
  const isAdministrator = async (user: { id: string } | null | undefined): Promise<boolean> => {
    if (!user?.id) return false;
    const account = await db.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
      .bind(user.id).first();
    return account !== null;
  };

  return {
    appName: "CAKE ID",
    baseURL,
    basePath: AUTH_BASE_PATH,
    secret: required(env.AUTH_SECRET, "AUTH_SECRET"),
    database: db,
    trustedOrigins: [baseURL],
    advanced: {
      database: { generateId: "uuid" },
      ipAddress: { ipAddressHeaders: ["cf-connecting-ip"] },
    },
    session: {
      expiresIn: 60 * 60 * 24 * 30,
      updateAge: 60 * 60 * 24,
      cookieCache: { enabled: false },
    },
    hooks: {
      before: createAuthMiddleware(async (ctx) => {
        if (ctx.path !== "/sign-up/email") return;
        const name = ctx.body?.name;
        const username = ctx.body?.username;
        if (typeof name !== "string" || !name.trim() || name.trim().length > 100
            || typeof username !== "string" || !username.trim()) {
          throw new APIError("BAD_REQUEST", { code: "INVALID_PROFILE", message: "Name and Username are required." });
        }
        ctx.body.name = name.trim();
        ctx.body.username = username.trim();
      }),
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
      // index.ts applies the durable identifier/source D1 policy before auth.
      // Avoid a second process-local login counter with incompatible thresholds.
      customRules: { "/sign-in/email": false, "/sign-in/username": false },
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
        resources: [{ identifier: apiResource, accessTokenTtl: ACCESS_TOKEN_SECONDS, allowedScopes: [...AUTHORIZATION_SCOPES] }],
        resourceSeedMode: "insertOnly",
        enforcePerClientResources: true,
        clientRegistrationDefaultResources: [apiResource],
        clientRegistrationAllowedResources: [],
        allowDynamicClientRegistration: false,
        accessTokenExpiresIn: ACCESS_TOKEN_SECONDS,
        idTokenExpiresIn: ACCESS_TOKEN_SECONDS,
        loginPage: "/sign-in",
        consentPage: "/consent",
        clientPrivileges: ({ user }) => isAdministrator(user),
        resourcePrivileges: ({ user }) => isAdministrator(user),
      }),
    ],
  };
}

export function createAuth(env: Env) {
  return betterAuth(createAuthOptions(env));
}
