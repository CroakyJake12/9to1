import assert from 'node:assert/strict';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import { accountScript } from '../src/account.mjs';

function fixture() {
  const signout = Promise.withResolvers();
  const messages = [];
  let channelsClosed = 0;
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, {
      listeners: new Map(), hidden: true,
      addEventListener(type, handler) { this.listeners.set(type, handler); },
      reset() {}, replaceChildren() {},
    });
    return elements.get(id);
  };
  const fetch = (path, options) => {
    if (path === '/api/account/current') return new Promise(() => {});
    assert.equal(path, '/api/account/signout');
    assert.equal(options.method, 'POST');
    return signout.promise;
  };
  runInNewContext(accountScript, {
    URL, fetch, location: { href: 'https://9to1.uk/account' },
    document: { querySelector: element, addEventListener() {} },
    BroadcastChannel: class {
      constructor(name) { assert.equal(name, '9to1-account-session-v1'); }
      postMessage(message) { messages.push(message); }
      close() { channelsClosed++; }
    },
  });
  const button = element('#signout');
  return { signout, messages, element, get closed() { return channelsClosed; },
    click: () => button.listeners.get('click')({ target: button, currentTarget: button }) };
}

function ownerPageFixture(actionPath, actionResponse) {
  const messages = [];
  const actionCalls = [];
  const elements = new Map();
  const sessionsReady = Promise.withResolvers();
  let sessionReads = 0;
  const makeElement = id => ({ id, listeners: new Map(), children: [], hidden: true, textContent: '', value: '', disabled: false,
    addEventListener(type, handler) { this.listeners.set(type, handler); },
    append(...children) { this.children.push(...children); if (id === '#sessions' && sessionReads === 1) sessionsReady.resolve(); },
    replaceChildren(...children) { this.children = [...children]; }, reset() {},
  });
  const element = id => {
    if (!elements.has(id)) {
      const value = makeElement(id);
      if (id === '#profile-form') value.elements = Object.fromEntries(['name', 'username', 'pronouns', 'job'].map(key => [key, { value: '' }]));
      elements.set(id, value);
    }
    return elements.get(id);
  };
  const session = { sessionId: 'fixture-session-0001', accountId: 'fixture-account', deviceName: 'Fixture device', createdAt: '2026-11-16T00:00:00.000Z' };
  const fetch = async (path, options) => {
    if (path === '/api/account/current') return Response.json({ accountId: session.accountId, displayName: 'Fixture owner' });
    if (path === '/api/account/profile') return Response.json({ profile: { accountId: session.accountId, revision: 1, name: 'Fixture owner', username: 'fixture', pronouns: '', job: '' } });
    if (path === '/api/account/theme') return Response.json({ preference: { revision: 1, appearance: 'Bright' } });
    if (path === '/api/account/sessions') {
      sessionReads++;
      return Response.json({ sessions: sessionReads === 1 ? [session] : [] });
    }
    if (path === actionPath) { actionCalls.push({ path, method: options.method }); return actionResponse; }
    throw new Error('Unexpected account fixture request.');
  };
  runInNewContext(accountScript, {
    URL, Response, fetch, confirm: () => true, location: { href: 'https://9to1.uk/account' },
    document: { querySelector: element, createElement: tag => makeElement(tag), addEventListener() {} },
    BroadcastChannel: class {
      constructor(name) { assert.equal(name, '9to1-account-session-v1'); }
      postMessage(message) { messages.push(message); }
      close() {}
    },
  });
  return { element, messages, actionCalls, sessionsReady: sessionsReady.promise };
}

test('successful account sign-out emits a fixed notice only after the server acknowledgement', async () => {
  const f = fixture();
  const completion = f.click();
  assert.deepEqual(f.messages, []);
  f.signout.resolve(new Response(null, { status: 204 }));
  await completion;
  assert.deepEqual(f.messages, ['signed-out']);
  assert.equal(f.closed, 1);
  assert.equal(f.element('#account-status').textContent, 'You have signed out.');
});

test('a failed sign-out preserves account state and never sends a logout notice', async () => {
  const f = fixture();
  f.element('#account-member').hidden = false;
  const completion = f.click();
  f.signout.resolve(Response.json({}, { status: 503 }));
  await completion;
  assert.deepEqual(f.messages, []);
  assert.equal(f.closed, 0);
  assert.equal(f.element('#account-member').hidden, false);
});

test('acknowledged per-session and other-session revocations broadcast only a PII-free recheck notice', async () => {
  for (const [path, method, selectButton] of [
    ['/api/account/sessions/fixture-session-0001', 'DELETE', f => f.element('#sessions').children[0].children[1]],
    ['/api/account/revoke-other-sessions', 'POST', f => f.element('#revoke-others')],
  ]) {
    const action = Promise.withResolvers();
    const f = ownerPageFixture(path, action.promise);
    await f.sessionsReady;
    const button = selectButton(f);
    const event = path.endsWith('revoke-other-sessions') ? { target: button, currentTarget: button } : {};
    const completion = button.listeners.get('click')(event);
    assert.deepEqual(f.messages, [], 'notice waits for the revocation response');
    action.resolve(new Response(null, { status: 204 }));
    await completion;
    assert.deepEqual(f.messages, ['sessions-changed']);
    assert.deepEqual(f.actionCalls, [{ path, method }]);
  }
});

test('failed per-session and other-session revocations never broadcast', async () => {
  for (const [path, selectButton] of [
    ['/api/account/sessions/fixture-session-0001', f => f.element('#sessions').children[0].children[1]],
    ['/api/account/revoke-other-sessions', f => f.element('#revoke-others')],
  ]) {
    const action = Promise.withResolvers();
    const f = ownerPageFixture(path, action.promise);
    await f.sessionsReady;
    const button = selectButton(f);
    const event = path.endsWith('revoke-other-sessions') ? { target: button, currentTarget: button } : {};
    const completion = button.listeners.get('click')(event);
    action.resolve(Response.json({ error: 'revocation_failed' }, { status: 503 }));
    await completion;
    assert.deepEqual(f.messages, []);
  }
});
