export interface LocalEmailCapture {
  id: string;
  recipient: string;
  subject: string;
  body: string;
  createdAt: number;
}

export type Env = Omit<Cloudflare.Env, "AUTH_BASE_URL" | "API_RESOURCE"> & {
  AUTH_BASE_URL: string;
  AUTH_SECRET: string;
  API_RESOURCE: string;
  LOGIN_LIMITER_KEY: string;
  LOCAL_TEST_KEY?: string;
  EMAIL?: SendEmail;
  EMAIL_MODE?: string;
  EMAIL_FROM?: string;
  EMAIL_DOMAIN?: string;
  GOOGLE_CLIENT_ID?: string;
  GOOGLE_CLIENT_SECRET?: string;
  TURNSTILE_SITE_KEY?: string;
  TURNSTILE_SECRET_KEY?: string;
  PUBLIC_TERMS_VERSION?: string;
  PUBLIC_PRIVACY_VERSION?: string;
  ACCOUNT_REGISTRATION_HOLD?: string;
};
