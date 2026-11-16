import { oauthProvider } from "@better-auth/oauth-provider";
import { betterAuth, type BetterAuthOptions } from "better-auth";
import { APIError, createAuthMiddleware } from "better-auth/api";
import { jwt, username } from "better-auth/plugins";
import { AUTH_BASE_PATH, ACCESS_TOKEN_SECONDS, AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";
import { deliverEmail } from "./email";
import { consumeInvitation, invitationUsable, invitationsRequired } from "./invitations";
import { accountPolicyCurrent, checkPolicyAcknowledgement, classifyAccountClient, recordSignupPolicy } from "./public-policy";
import { getCurrentAuthEndpointContext } from "@better-auth/core/context";
import { recordSecurityEvent } from "./security-telemetry";
import { createPasswordResetFence } from "./password-reset-fence";

export function googleCredentials(env: Pick<Env, "GOOGLE_CLIENT_ID" | "GOOGLE_CLIENT_SECRET">): { clientId: string; clientSecret: string } | null {
  const clientId = env.GOOGLE_CLIENT_ID;
  const clientSecret = env.GOOGLE_CLIENT_SECRET;
  // An incomplete configuration never activates a partially configured provider.
  if (typeof clientId !== "string" || !clientId.trim() || typeof clientSecret !== "string" || !clientSecret.trim()) return null;
  return { clientId, clientSecret };
}

export function registrationHeld(env: Env): boolean {
  return env.ACCOUNT_REGISTRATION_HOLD === "true" && invitationsRequired(env);
}

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

export function createAuthOptions(env: Env): BetterAuthOptions {
  const baseURL = required(env.AUTH_BASE_URL, "AUTH_BASE_URL").replace(/\/$/, "");
  const apiResource = required(env.API_RESOURCE, "API_RESOURCE");
  const localOnly = env.APP_MODE === "local-test-only";
  assertSafeServiceUrl(baseURL, "AUTH_BASE_URL", localOnly);
  assertSafeServiceUrl(apiResource, "API_RESOURCE", localOnly);
  const db = env.DB;
  const google = googleCredentials(env);
  const resetFence = createPasswordResetFence(env);
  const currentClientKind = async () => {
    const context = getCurrentAuthEndpointContext();
    const identifiers: unknown[] = [context.query?.client_id, context.body?.client_id].filter(value => value !== undefined && value !== null);
    // At provider hook execution the maintained OAuth hook has verified this
    // signed query; direct and internally resumed authorization share context.
    if (typeof context.body?.oauth_query === "string") {
      const signedIds = new URLSearchParams(context.body.oauth_query).getAll("client_id");
      if (signedIds.length !== 1) throw new APIError("BAD_REQUEST", { error: "invalid_client" });
      identifiers.push(signedIds[0]);
    }
    // Consent sees URL query before the provider replaces it with the signed
    // authorization query. A caller cannot use a legacy URL id to override it.
    if (!identifiers.length || identifiers.some(value => typeof value !== "string" || !value) || new Set(identifiers).size !== 1)
      throw new APIError("BAD_REQUEST", { error: "invalid_client" });
    const clientId = identifiers[0];
    const kind = await classifyAccountClient(env, clientId);
    if (kind === "invalid") throw new APIError("BAD_REQUEST", { error: "invalid_client" });
    return kind;
  };
  // Private role fields are intentionally omitted from session/user API output.
  // Evaluate privileged administration from the canonical server row instead.
  const isAdministrator = async (user: { id: string } | null | undefined): Promise<boolean> => {
    if (!user?.id) return false;
    const account = await db.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
      .bind(user.id).first();
    return account !== null;
  };

  return {
    appName: "9to1 Account",
    logger: { disabled: true },
    baseURL,
    basePath: AUTH_BASE_PATH,
    secret: required(env.AUTH_SECRET, "AUTH_SECRET"),
    database: db,
    trustedOrigins: [baseURL],
    socialProviders: google ? { google: { ...google,
      redirectURI: `${baseURL}${AUTH_BASE_PATH}/callback/google`, prompt: "select_account",
      disableSignUp: invitationsRequired(env) } } : {},
    account: {
      encryptOAuthTokens: true,
      accountLinking: { enabled: true, trustedProviders: [], requireLocalEmailVerified: true,
        allowDifferentEmails: false, updateUserInfoOnLink: false },
    },
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
        if (registrationHeld(env)) {
          recordSecurityEvent("signup_rejected");
          throw new APIError("FORBIDDEN", { code: "account_registration_held", message: "New account registration is temporarily unavailable." });
        }
        if (invitationsRequired(env)) {
          const error = checkPolicyAcknowledgement(env, ctx.body);
          if (error) {
            recordSecurityEvent("signup_rejected");
            throw new APIError(error === "account_policy_unavailable" ? "SERVICE_UNAVAILABLE" : "FORBIDDEN", {
              code: error, message: error === "account_policy_unavailable"
                ? "Invitation redemption is temporarily unavailable while account policies are reviewed."
                : "Confirm an eligible age group and the current account policies before registering." });
          }
          // Give a genuine refusal before the library's account-enumeration
          // response masking. Provisioning still performs the atomic claim.
          if (!await invitationUsable(env, ctx.body?.email, ctx.body?.invitationCode)) {
            recordSecurityEvent("signup_rejected");
            throw new APIError("FORBIDDEN", { code: "invitation_required", message: "Use a valid invitation for this email address, or sign in to an existing account." });
          }
        }
        const name = ctx.body?.name;
        const username = ctx.body?.username;
        if (typeof name !== "string" || !name.trim() || name.trim().length > 100
            || typeof username !== "string" || !username.trim()) {
          throw new APIError("BAD_REQUEST", { code: "INVALID_PROFILE", message: "Name and Username are required." });
        }
        ctx.body.name = name.trim();
        ctx.body.username = username.trim();
      }),
      after: createAuthMiddleware(async context => await resetFence.after(context)),
    },
    disabledPaths: ["/update-user", "/update-username"],
    databaseHooks: { user: { create: { before: async () => {
      if (registrationHeld(env)) {
        recordSecurityEvent("signup_rejected");
        throw new APIError("FORBIDDEN", { code: "account_registration_held", message: "New account registration is temporarily unavailable." });
      }
    }, after: async (user, context) => {
      if (invitationsRequired(env) && context?.path === "/sign-up/email") await recordSignupPolicy(env, user.id, context.body);
    } } } },
    emailAndPassword: {
      enabled: true,
      minPasswordLength: 8,
      maxPasswordLength: 128,
      requireEmailVerification: true,
      autoSignIn: false,
      resetPasswordTokenExpiresIn: 30 * 60,
      revokeSessionsOnPasswordReset: true,
      onPasswordReset: async ({ user }) => { await resetFence.reset(user.id); },
      sendResetPassword: async ({ user, url }) => {
        await deliverEmail(env, {
          to: user.email,
          subject: "Reset your 9to1 Account password",
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
          subject: "Verify your 9to1 Account email",
          text: `Verify your email address: ${url}`,
        });
      },
    },
    user: {
      validateUserInfo: async ({ user, source }, context) => {
        if (source.action === "create-user" && registrationHeld(env)) {
          recordSecurityEvent("signup_rejected");
          return { error: "account_registration_held", errorDescription: "New account registration is temporarily unavailable." };
        }
        if (source.oauth?.providerId === "google" && user.emailVerified !== true)
          return { error: "google_email_not_verified", errorDescription: "Google must verify your email before you can sign in." };
        if (source.action === "create-user" && invitationsRequired(env)) {
          if (source.method !== "email-password" || context?.path !== "/sign-up/email") {
            recordSecurityEvent("signup_rejected");
            return { error: "invitation_required", errorDescription: "Account creation is invite-only. Register using the email address on your invitation." };
          }
          const policyError = checkPolicyAcknowledgement(env, context.body);
          if (policyError) { recordSecurityEvent("signup_rejected"); return { error: policyError, errorDescription: policyError === "account_policy_unavailable"
            ? "Invitation redemption is temporarily unavailable while account policies are reviewed."
            : "Confirm your eligible age group and the current account policies before registering." }; }
          try {
            if (!await consumeInvitation(env, user.email, context.body?.invitationCode)) {
              recordSecurityEvent("signup_rejected");
              return { error: "invitation_required", errorDescription: "Use a valid invitation for this email address, or sign in to an existing account." };
            }
          } catch { recordSecurityEvent("service_error"); return { error: "registration_unavailable", errorDescription: "Account registration is temporarily unavailable." }; }
        }
      },
      additionalFields: {
        pronouns: { type: "string", required: false, input: true },
        jobTitle: { type: "string", required: false, input: true },
        role: { type: "string", required: false, input: false, returned: false, defaultValue: "user" },
        profileRevision: { type: "number", required: false, input: false, returned: false, defaultValue: 1 },
        credentialGeneration: { type: "number", required: true, input: false, returned: false, defaultValue: 0 },
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
        customAccessTokenClaims: ({ user }) => resetFence.claims(user),
        customTokenResponseFields: async info => {
          await resetFence.capture(getCurrentAuthEndpointContext().context, info.user, info.grantType, info.verificationValue?.sessionId);
          return {};
        },
        customUserInfoClaims: ({ user, jwt }) => resetFence.userinfo(user, jwt),
        postLogin: {
          page: "/account/onboarding",
          shouldRedirect: async ({ user }) => await currentClientKind() === "production" && !await accountPolicyCurrent(env, user.id),
          consentReferenceId: async ({ user }) => {
            if (await currentClientKind() === "production" && !await accountPolicyCurrent(env, user.id)) {
              recordSecurityEvent("policy_rejected");
              throw new APIError("FORBIDDEN", { error: "account_setup_required" });
            }
            return undefined;
          },
        },
        clientPrivileges: ({ user }) => isAdministrator(user),
        resourcePrivileges: ({ user }) => isAdministrator(user),
      }),
    ],
  };
}

export function createAuth(env: Env) {
  return betterAuth(createAuthOptions(env));
}
