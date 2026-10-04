import type { Env } from "./env";

function mailbox(value: string | undefined): string {
  if (!value || !/^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$/.test(value)) {
    throw new Error("CAKE ID email requires an explicit plain mailbox address");
  }
  return value;
}

export async function deliverEmail(env: Env, message: { to: string; subject: string; text: string }): Promise<void> {
  if (env.APP_MODE === "local-test-only" && env.EMAIL_CAPTURE === "true") {
    await env.DB.prepare(
      "INSERT INTO cake_email_outbox (recipient, subject, body, createdAt) VALUES (?, ?, ?, ?)",
    ).bind(message.to.toLowerCase(), message.subject, message.text, Date.now()).run();
    return;
  }
  if (env.EMAIL_MODE !== "cloudflare-email-service" || !env.EMAIL) {
    throw new Error("CAKE ID email delivery is not configured");
  }
  const from = mailbox(env.EMAIL_FROM);
  const domain = env.EMAIL_DOMAIN;
  if (!domain || !/^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$/.test(domain) || !domain.includes(".") || domain.includes("..") || from.split("@")[1].toLowerCase() !== domain) {
    throw new Error("CAKE ID email sender must match the explicitly configured domain");
  }
  const to = mailbox(message.to);
  if (/[\r\n]/.test(message.subject)) throw new Error("CAKE ID email subject must not contain line breaks");
  // Provider rejection remains a delivery failure; never fall back to an outbox.
  await env.EMAIL.send({ from, to, subject: message.subject, text: message.text });
}
