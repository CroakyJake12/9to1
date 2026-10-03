// Preserve the actual original test failure independently of strict-drain/cleanup failure.
export async function finishIntegrationCleanup(primaryFailure, cleanup) {
  try { await cleanup(); }
  catch (cleanupFailure) {
    throw new AggregateError(primaryFailure === undefined ? [cleanupFailure] : [primaryFailure, cleanupFailure],
      "Integration cleanup failed; original test and cleanup failures retained", { cause: cleanupFailure });
  }
}
