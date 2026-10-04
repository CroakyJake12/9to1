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
};
