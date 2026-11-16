import { DurableObject } from 'cloudflare:workers';
import { FilesProject } from './project.mjs';
import { FILES_ORIGIN, API_PREFIX, SESSION_COOKIE, readCookie, FilesHttpError, demandOwner, errorResponse } from './protocol.mjs';

export class DriveProject extends DurableObject {
  constructor(ctx, env) { super(ctx, env); this.project = new FilesProject(ctx, env); }
  fetch(request) { return this.project.fetch(request); }
}

function secure(response, isApi) {
  const headers = new Headers(response.headers);
  const privateForm = isApi && headers.get('content-type')?.startsWith('text/html');
  headers.set('cache-control', 'private, no-store'); headers.set('x-robots-tag', 'noindex, nofollow');
  // A same-origin form needs its Origin header: no-referrer turns a
  // navigation POST's Origin into null. Keep all other API responses opaque.
  headers.set('x-content-type-options', 'nosniff'); headers.set('referrer-policy', privateForm ? 'same-origin' : 'no-referrer');
  headers.set('x-frame-options', 'DENY'); headers.set('strict-transport-security', 'max-age=31536000');
  headers.set('permissions-policy', 'camera=(), microphone=(), geolocation=()');
  headers.set('content-security-policy', isApi ? "default-src 'none'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'" :
    "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob:; connect-src 'self'; worker-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'");
  headers.set('x-nine-files-version', '2026-11-15.1');
  return new Response(response.body, { status: response.status, headers });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (url.origin !== FILES_ORIGIN) return secure(new Response('Unknown origin', { status: 421 }), true);
      const isApi = url.pathname.startsWith(API_PREFIX + '/');
      if (isApi) {
        // Reject unauthenticated callers before sending them to the shared
        // project object. The object rechecks the actual live account itself.
        if (!/^[\w-]{43}$/.test(readCookie(request, SESSION_COOKIE) ?? '')) return secure(errorResponse(new FilesHttpError(401, 'SignInRequired', 'Sign in to your 9to1 Account.', { notStarted: true })), true);
        return secure(await env.DRIVE_PROJECT.getByName('private-google-drive-v1').fetch(request), true);
      }
      if (!['GET', 'HEAD'].includes(request.method) || !url.pathname.startsWith('/files')) return secure(new Response('Not found', { status: 404 }), true);
      if (url.pathname === '/files') return secure(new Response(null, { status: 308, headers: { location: '/files/' } }), false);
      // Static assets can normalize escaped characters and repeated separators.
      // Do not send aliases of the owner-gated entry document to that router.
      if (!url.pathname.startsWith('/files/') || url.pathname.includes('%') || url.pathname.includes('//')) {
        return secure(new Response('Not found', { status: 404 }), true);
      }
      if (url.pathname === '/files/' || url.pathname === '/files/index.html') {
        try { await demandOwner(request, env); }
        catch (error) {
          if (error.status === 401) return secure(new Response(null, { status: 303, headers: { location: '/auth/sign-in?return=%2Ffiles%2F' } }), false);
          throw error;
        }
        if (url.pathname === '/files/index.html') {
          return secure(new Response(null, { status: 308, headers: { location: '/files/' } }), false);
        }
      }
      if (!env.ASSETS) return secure(new Response('The existing Files app is being prepared.', { status: 503 }), false);
      const asset = new URL(request.url);
      asset.pathname = url.pathname === '/files/' ? '/index.html' : url.pathname.slice('/files'.length);
      return secure(await env.ASSETS.fetch(new Request(asset, request)), false);
    } catch (error) { return secure(errorResponse(error), true); }
  },
};
