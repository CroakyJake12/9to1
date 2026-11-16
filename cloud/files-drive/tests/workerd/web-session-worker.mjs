import { DurableObject } from 'cloudflare:workers';

const OWNER_ACCOUNT_ID = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
const WRONG_ACCOUNT_ID = 'b1a85012-55bb-4576-8b68-126ef0ca56bd';
const FILES_ORIGIN = 'https://9to1.uk';

export class WebSessionFixture extends DurableObject {
  async request(path, method, body, origin) {
    if (path !== '/api/account/current' || method !== 'GET' || body !== null || origin !== FILES_ORIGIN) {
      return new Response(JSON.stringify({ code: 'FixtureRequestRefused' }), {
        status: 400, headers: { 'content-type': 'application/json' },
      });
    }
    const mode = this.ctx.id.name?.[0];
    if (mode === 'R') return new Response(null, { status: 401 });
    return new Response(JSON.stringify({
      accountId: mode === 'W' ? WRONG_ACCOUNT_ID : OWNER_ACCOUNT_ID,
      displayName: 'Fixture Files Owner',
    }), { headers: { 'content-type': 'application/json' } });
  }
}

export default {
  async fetch() { return new Response('Session fixture has no public route.', { status: 404 }); },
};
