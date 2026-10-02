export const AUTH_BASE_PATH = "/api/auth";

// Canonical shared CAKE/9to1 account API scopes recorded in the Astra integration contract.
export const CAKE_SCOPES = [
  "cake:account:read",
  "cake:profile:read",
  "cake:profile:write",
  "cake:sessions:read",
  "cake:sessions:revoke",
] as const;

export type CakeScope = (typeof CAKE_SCOPES)[number];

export const AUTHORIZATION_SCOPES = [
  "openid",
  "profile",
  "email",
  "offline_access",
  ...CAKE_SCOPES,
] as const;

export const LOGIN_LIMITS = {
  maximumAttempts: 8,
  windowSeconds: 15 * 60,
  lockSeconds: 30 * 60,
} as const;

export const ACCESS_TOKEN_SECONDS = 5 * 60;
