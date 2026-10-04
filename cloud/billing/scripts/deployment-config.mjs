// Preparation only: no provider calls, resource creation, or secret values.
export function deploymentConfig(input) {
  const keys = ['accountId','workerName','databaseId','databaseName','stripeMode','signatureToleranceSeconds','maxWebhookBytes','cpuMs','workersDev'];
  if (!input || typeof input !== 'object' || Array.isArray(input) || Object.keys(input).some(k => !keys.includes(k)) || keys.some(k => !Object.hasOwn(input,k))) throw new TypeError('Explicit deployment inputs required');
  for (const k of ['accountId','workerName','databaseId','databaseName']) if (typeof input[k] !== 'string') throw new TypeError('String provider identities required');
  if (!/^[a-f0-9]{32}$/.test(input.accountId) || !/^[a-z][a-z0-9-]{0,62}$/.test(input.workerName) || !/^[a-zA-Z0-9_-]{1,64}$/.test(input.databaseName) || !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(input.databaseId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(input.databaseId)) throw new TypeError('Verified provider identities required');
  if (!['test','live'].includes(input.stripeMode) || typeof input.workersDev !== 'boolean') throw new TypeError('Explicit mode and publication choice required');
  for (const k of ['signatureToleranceSeconds','maxWebhookBytes','cpuMs']) if (!Number.isSafeInteger(input[k]) || input[k] <= 0) throw new TypeError('Positive integer bounds required');
  if (input.cpuMs > 300000) throw new TypeError('CPU limit exceeds documented Paid maximum');
  return {
    name: input.workerName, account_id: input.accountId, main: 'worker.mjs',
    compatibility_date: '2026-10-01', workers_dev: input.workersDev,
    limits: {cpu_ms: input.cpuMs}, observability: {enabled:false},
    vars: {STRIPE_MODE:input.stripeMode,SIGNATURE_TOLERANCE_SECONDS:input.signatureToleranceSeconds,MAX_WEBHOOK_BYTES:input.maxWebhookBytes},
    secrets: {required:['STRIPE_WEBHOOK_SECRET']},
    d1_databases: [{binding:'BILLING_DB',database_id:input.databaseId,database_name:input.databaseName,migrations_dir:'schema'}]
  };
}
