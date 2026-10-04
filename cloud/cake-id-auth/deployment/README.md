This package prepares the new isolated `cake-id-release-validation` Worker. It never targets the existing email-only `cake` Worker. The proposed issuer, API resource and initial allowed web origin are exactly `https://cake-id-release-validation.jcbailey008.workers.dev`. No OAuth client or callback is provisioned by this package.

Compile locally from `cloud/cake-id-auth`:

```
npm ci
npm run typecheck
node tests/email-delivery.mjs
node tests/deployment-config.mjs
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg node_modules/.bin/wrangler deploy --dry-run --config deployment/wrangler.build.json --outdir /workspace/team-c/evidence/c2/hosted-artifact --metafile /workspace/team-c/evidence/c2/hosted-artifact/esbuild-meta.json
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg node_modules/.bin/wrangler deploy --dry-run --config deployment/wrangler.rollback-build.json --outdir /workspace/team-c/evidence/c2/hosted-rollback-artifact
```

Only the root operator creates resources or deploys. Account plan/spend authorization must be resolved first; existing resource GET success does not establish free-plan eligibility. The D1 inventory is empty. After actual isolated D1 creation, supply its receipt with `account_id`, `database_name` and `database_id` to `node deployment/prepare.mjs RECEIPT.json deployment/wrangler.prepared.json`. The generator refuses missing or invalid IDs, another database name and overwrite. This is structural validation; the operator must independently bind the values to the actual provider receipt. Generated configs remain private and untracked.

Bindings are `DB` (actual isolated D1 UUID) and required secrets `AUTH_SECRET` (at least32 characters) and `LOGIN_LIMITER_KEY` (at least32 characters). Generate independent secrets outside Git/chat. Apply both pinned migrations using the prepared config and record actual migration receipts before exposing the runtime. Reserved `croakyjake` owner remains NULL. Never deploy `src/local.ts`, test admin promotion, verification shortcuts or email outbox routes. Admin/client provisioning is a separate canonical authority gate; no production bootstrap is added.

Mail is deliberately disabled in the initial generated configuration. Registration and recovery delivery fail closed. To configure the optional actual Cloudflare Email Service binding, the operator must first establish service/plan authorization, approved sender domain readiness and sender identity. Set `EMAIL_MODE=cloudflare-email-service`, explicit plain `EMAIL_FROM`, exact lower-case `EMAIL_DOMAIN`, and a `send_email` binding named `EMAIL` restricted with `allowed_sender_addresses` to that sender. No sender/domain is selected here. Neither known zone currently has an enabled sending subdomain. Pre-onboarding verified-recipient restrictions do not provide general public-account delivery. Delivery awaits the provider and propagates failures; production never falls back to local capture. Transport-double tests exercise our adapter only, not provider acceptance.

For a new Worker, first upload the compiled closed503 baseline and record its actual version ID; the rollback build config is compiler-only and disables public exposure. The operator must bind the same reviewed isolated Worker/account before upload. Record the identity version/deployment receipt and preserve the closed baseline version ID. Roll back traffic to that actual recorded version if validation fails; never use an existing `cake` version. Cloudflare rollback does not undo D1 schema/data or secrets. Preserve D1 and any created identity data; establish the applicable D1 recovery point before migration. Resource deletion is not rollback.

Real HTTPS acceptance still requires provider creation/deployment receipts, secret readiness, actual migrations, issuer discovery/JWKS/token/account/session checks, approved mail delivery and approved canonical administrator/client authority. Turnstile enforcement does not exist in this maintained source. The stricter .NET RSA/auth_revision contract remains unresolved; compiling this browser-compatible EdDSA issuer does not establish native integration.
