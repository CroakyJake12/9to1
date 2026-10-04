// An isolated new Worker has no previous identity version to roll back to.
// Upload this closed baseline first and record its actual Cloudflare version ID.
export default {
  async fetch(): Promise<Response> {
    return new Response('CAKE ID release validation is unavailable', {
      status: 503,
      headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' },
    });
  },
};
