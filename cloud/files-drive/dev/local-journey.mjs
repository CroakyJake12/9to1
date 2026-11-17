// Development-only interactive journey over the real sealed WebAssembly app,
// the genuine Files Worker and the disposable local Drive fixture, driven
// through actual Chrome native accessibility peers with screenshots.
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createHash, randomBytes } from 'node:crypto';
import { join } from 'node:path';
import { launchChrome, attachPage, evaluate, pollFor } from './cdp.mjs';

const APP = 'https://9to1.uk/files/';
const HELPER = readFileSync(new URL('./files-management-driver.js', import.meta.url), 'utf8');

const has = label => `__nine.has(${JSON.stringify(label)})`;
const enabled = label => `__nine.enabled(${JSON.stringify(label)})`;
const click = label => `__nine.click(${JSON.stringify(label)})`;
const set = (name, value) => `__nine.set(${JSON.stringify(name)}, ${JSON.stringify(value)})`;
const ready = '(() => { const s = __nine.status(); return s.ready === "Ready" && s.hash === "#/app.files"; })()';
const snapshot = '__nine.status().controls.join(",")';

async function until(session, expression, timeoutMs, what) {
  const result = await pollFor(session, expression, timeoutMs);
  if (!(result === true || result?.done === true)) throw new Error('Timeout waiting for ' + what);
  return result;
}

export async function runJourney({ connect, fixture, tlsPort, state, chrome }) {
  const work = join(state, 'journey');
  mkdirSync(work, { recursive: true });
  const shots = join(work, 'screenshots');
  const downloads = join(work, 'downloads');
  const uploadSource = join(work, 'local-upload.txt');
  const uploadBytes = Buffer.concat([Buffer.from('9to1 local Files candidate upload fixture\n'), randomBytes(1024 - 44),
    Buffer.from('-'.repeat(428))]).slice(0, 1500);
  writeFileSync(uploadSource, uploadBytes);
  const uploadSha256 = createHash('sha256').update(uploadBytes).digest('hex');
  const browser = await launchChrome({ chrome, profileDir: join(work, 'chrome-profile'),
    args: [`--host-resolver-rules=MAP 9to1.uk:443 127.0.0.1:${tlsPort}`, '--window-size=1440,2400'] });
  const steps = [];
  let failure = null;
  try {
    await browser.client.send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: downloads,
      eventsEnabled: true });
    const session = await attachPage(browser.client, 'about:blank');
    await session.send('Page.setInterceptFileChooserDialog', { enabled: true });
    const chooserEvents = [];
    let resolveChooser;
    const chooserPromise = new Promise(resolveWait => { resolveChooser = resolveWait; });
    session.on('Page.fileChooserOpened', params => {
      chooserEvents.push(params);
      resolveChooser(params);
    });
    await session.send('Page.addScriptToEvaluateOnNewDocument', { source: HELPER });
    const downloadEvents = [];
    browser.client.on(message => message.method === 'Browser.downloadWillBegin' ||
      message.method === 'Browser.downloadProgress', message => downloadEvents.push(message.params));
    // The owner session cookie is attached locally by the development TLS
    // rules; production enforces the real __Host-cake_session cookie.
    const cookieSet = await session.send('Network.setCookie', { name: '__Host-cake_session',
      value: 'O' + '0'.repeat(42), url: APP, path: '/', secure: true, httpOnly: true, sameSite: 'None' });
    if (cookieSet.success !== true) throw new Error('Owner fixture cookie refused: ' + JSON.stringify(cookieSet));
    await connect();
    await session.send('Page.navigate', { url: APP });
    let loaded = false;
    for (let attempt = 0; attempt < 40 && !loaded; attempt++) {
      await new Promise(resolveWait => setTimeout(resolveWait, 500));
      loaded = await evaluate(session, 'typeof window.__nine === "object"').catch(() => false);
    }
    if (!loaded) throw new Error('Journey helpers never loaded (page or WASM boot failed)');

    const step = async (name, body) => {
      const began = Date.now();
      try {
        const detail = await body();
        steps.push({ name, ok: true, detail: detail ?? null, ms: Date.now() - began });
      } catch (error) {
        const detail = String(error?.message ?? error).slice(0, 400);
        steps.push({ name, ok: false, detail, controls: await evaluate(session, snapshot).catch(() => null),
          ms: Date.now() - began });
        failure = { name, detail };
      }
      const shot = await session.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
      mkdirSync(shots, { recursive: true });
      writeFileSync(join(shots, String(steps.length).padStart(2, '0') + '-' + name + '.png'),
        Buffer.from(shot.data, 'base64'));
      if (failure) throw new Error('Journey stopped at ' + name + ': ' + detail);
    };

    await step('boot-connected-root', async () => {
      await until(session, ready, 60000, 'compiled Files app ready');
      await until(session, `${has('Local Fixture Folder')} && ${has('welcome.txt')} && ${enabled('Disconnect Google Drive')}`,
        30000, 'connected root rows');
      return { controls: await evaluate(session, snapshot) };
    });

    await step('folder-search', async () => {
      await until(session, set('Search names in this folder', 'Fixture'), 15000, 'search text binding');
      await until(session, click('Search folder'), 15000, 'search action');
      await until(session, `(() => { const s = __nine.status();
        return s.controls.includes("Local Fixture Folder") && !s.controls.includes("welcome.txt"); })()`,
      20000, 'search-filtered rows');
      await until(session, click('Clear search'), 15000, 'clear search action');
      await until(session, `(() => { const s = __nine.status();
        return s.controls.includes("Local Fixture Folder") && s.controls.includes("welcome.txt"); })()`,
      20000, 'restored listing after clear');
      return { query: 'Fixture' };
    });

    await step('grid-details-toggle', async () => {
      if (!await evaluate(session, enabled('Details'))) throw new Error('Details toggle unavailable');
      await until(session, click('Details'), 10000, 'switch to details list');
      await until(session, has('welcome.txt') && enabled('Grid'), 10000, 'rows still resolvable in list view');
      await until(session, click('Grid'), 10000, 'switch back to grid');
      await until(session, has('welcome.txt') && enabled('Details'), 10000, 'tiles still resolvable in grid view');
      return { view: 'round-trip' };
    });

    await step('create-folder', async () => {
      await until(session, click('New folder'), 15000, 'new folder action');
      await until(session, set('Folder or item name', 'Journey Folder'), 15000, 'name input accepted');
      if (!await evaluate(session, click('Save'))) throw new Error('Save peer unavailable');
      await until(session, has('Journey Folder'), 30000, 'created folder row');
      return { row: 'Journey Folder' };
    });

    await step('rename-folder', async () => {
      await until(session, click('Journey Folder'), 15000, 'row selection');
      await until(session, click('Rename'), 15000, 'rename editor');
      await until(session, set('Folder or item name', 'Journey Renamed'), 15000, 'renamed draft');
      if (!await evaluate(session, click('Save'))) throw new Error('Save peer unavailable');
      await until(session, `(() => { const s = __nine.status(); return s.controls.includes("Journey Renamed") && !s.controls.includes("Journey Folder"); })()`,
        30000, 'renamed row replaces original');
      return { row: 'Journey Renamed' };
    });

    await step('upload-file', async () => {
      await until(session, click('Upload file…'), 15000, 'upload action');
      // Headless Chrome never surfaces Page.fileChooserOpened for the app's own
      // programmatic picker, so attach the file to that real picker input and
      // let its native change event drive the genuine upload bridge.
      let chooserAt = await Promise.race([chooserPromise, Promise.resolve(null)]);
      let attached = Boolean(chooserAt);
      const deadline = Date.now() + 4000;
      while (!attached && Date.now() < deadline) {
        await new Promise(resolveWait => setTimeout(resolveWait, 150));
        chooserAt = await Promise.race([chooserPromise, Promise.resolve(null)]);
        attached = Boolean(chooserAt);
        if (!attached) attached = await evaluate(session, `(() => {
          const input = document.querySelector('input[type=file]');
          if (!input) return false;
          window.__nineUploadInput = input;
          return true;
        })()`);
      }
      if (!attached) throw new Error('The app file picker never became attachable');
      if (chooserAt) {
        await session.send('DOM.setFileInputFiles', { files: [uploadSource],
          backendNodeId: chooserAt.backendNodeId, frameId: chooserAt.frameId });
      } else {
        const { root } = await session.send('DOM.getDocument', { depth: -1 });
        void root;
        const { nodeId } = await session.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'input[type=file]' });
        if (!nodeId) throw new Error('File picker input disappeared');
        await session.send('DOM.setFileInputFiles', { files: [uploadSource], nodeId });
      }
      await until(session, has('local-upload.txt'), 60000, 'uploaded file row');
      return { viaChooserEvent: Boolean(chooserAt), sha256: uploadSha256 };
    });

    await step('download-file', async () => {
      await until(session, click('local-upload.txt'), 15000, 'upload row selection');
      await until(session, click('Prepare download'), 15000, 'prepare download');
      await until(session, click('Save download'), 30000, 'ticket prepared');
      const deadline = Date.now() + 45000;
      let file = null;
      while (Date.now() < deadline && !file) {
        for (const name of ['local-upload.txt', 'local-upload (1).txt']) {
          try { file = { name, bytes: readFileSync(join(downloads, name)) }; } catch { /* not finished */ }
        }
          if (!file) await new Promise(resolveWait => setTimeout(resolveWait, 250));
      }
      if (!file) throw new Error('No Chrome download completed for the prepared ticket');
      const sha = createHash('sha256').update(file.bytes).digest('hex');
      if (sha !== uploadSha256 || file.bytes.byteLength !== uploadBytes.byteLength) {
        throw new Error('Downloaded bytes differ from fixture upload');
      }
      return { file: file.name, bytes: file.bytes.byteLength };
    });

    await step('export-google-doc', async () => {
      await until(session, click('Fixture doc'), 15000, 'Google-native selection');
      await until(session, click('Prepare PDF export'), 15000, 'prepare export');
      await until(session, click('Save download'), 30000, 'export ticket prepared');
      const deadline = Date.now() + 45000;
      let bytes = null;
      while (Date.now() < deadline && !bytes) {
        for (const name of ['Fixture doc.pdf', 'Fixture doc (1).pdf']) {
          try { bytes = readFileSync(join(downloads, name)); } catch { /* not finished */ }
        }
        if (!bytes) await new Promise(resolveWait => setTimeout(resolveWait, 250));
      }
      if (!bytes || !bytes.subarray(0, 8).toString().includes('%PDF')) {
        throw new Error('No PDF export bytes arrived from the real export route');
      }
      return { bytes: bytes.byteLength };
    });

    await step('move-into-folder', async () => {
      await until(session, click('welcome.txt'), 15000, 'welcome selection');
      await until(session, click('Move…'), 15000, 'move action');
      await until(session, click('Local Fixture Folder'), 15000, 'destination selection');
      await until(session, click('Open selected folder'), 15000, 'open destination');
      await until(session, click('Move here…'), 15000, 'move destination');
      await until(session, click('Move here'), 15000, 'move confirmation');
      // The move destination is the folder now open: both nested rows show.
      await until(session, `(() => { const s = __nine.status(); return s.controls.includes("welcome.txt") && s.controls.includes("readme.txt"); })()`,
        30000, 'moved welcome.txt listed beside readme.txt');
      return { destination: 'Local Fixture Folder' };
    });

    await step('trash-welcome', async () => {
      await until(session, click('welcome.txt'), 20000, 'welcome row in folder');
      await until(session, click('Move to Trash…'), 15000, 'trash action');
      await until(session, click('Move to Trash'), 15000, 'trash confirmation');
      await until(session, `(() => { const s = __nine.status(); return !s.controls.includes("welcome.txt"); })()`,
        30000, 'trashed row removed');
      return { trashed: 'welcome.txt' };
    });

    await step('trash-view-and-restore', async () => {
      await until(session, click('Trash'), 15000, 'trash entry action');
      await until(session, has('welcome.txt'), 20000, 'welcome.txt listed in Trash');
      await until(session, click('welcome.txt'), 15000, 'trash selection');
      await until(session, click('Restore…'), 15000, 'restore action');
      await until(session, click('Restore'), 15000, 'restore confirmation');
      await until(session, `(() => { const s = __nine.status(); return !s.controls.includes("welcome.txt"); })()`,
        20000, 'welcome.txt left the Trash list');
      await until(session, click('Back to files'), 15000, 'exit trash action');
      await until(session, has('readme.txt'), 20000, 'folder contents restored');
      return { restoredTo: 'Local Fixture Folder', driveState:
        fixture.files.get([...fixture.files.values()].find(f => f.name === 'welcome.txt')?.id)?.trashed === false };
    });

    await step('reload-persistence', async () => {
      await session.send('Page.reload');
      await until(session, ready, 60000, 'app ready after reload');
      await until(session, `(() => { const s = __nine.status();
        return s.controls.includes("Journey Renamed") && s.controls.includes("Local Fixture Folder")
          && s.controls.includes("local-upload.txt") && s.controls.includes("Fixture doc")
          && !s.controls.includes("welcome.txt"); })()`,
      30000, 'root restored after reload');
      return { controls: await evaluate(session, snapshot) };
    });

    await step('disconnect-drive', async () => {
      await until(session, click('Disconnect Google Drive'), 15000, 'disconnect action');
      await until(session, enabled('Connect Google Drive'), 30000, 'disconnected state');
      return { revokeCalls: fixture.calls.filter(call => call.path === '/revoke').length };
    });

    await step('reconnect-and-verify', async () => {
      await connect();
      await session.send('Page.reload');
      await until(session, ready, 60000, 'app ready after reconnect');
      await until(session, `(() => { const s = __nine.status();
        return s.controls.includes("Journey Renamed") && s.controls.includes("local-upload.txt"); })()`,
      30000, 'connected rows after reconnect');
      return { context: { connected: true }, tokenCalls:
        fixture.calls.filter(call => call.path === '/token').length };
    });
  } catch (error) {
    failure = failure ?? { name: 'unknown', detail: String(error?.message ?? error).slice(0, 400) };
  } finally {
    await browser.close().catch(() => {});
    rmSync(join(work, 'chrome-profile'), { recursive: true, force: true });
  }
  const nonFixture = fixture.calls.filter(call => !['oauth2.googleapis.com', 'www.googleapis.com'].includes(call.host));
  return {
    status: failure ? 'FAIL' : 'PASS',
    failedStep: failure?.name ?? null, failureDetail: failure?.detail ?? null,
    steps, screenshots: shots, downloads,
    providerCalls: fixture.calls.length, unexpectedProviderHosts: nonFixture,
    runner: 'real sealed WebAssembly + genuine Files Worker + Workerd in Chrome (local fixture)',
    realGoogleDriveAcceptance: 'NOT RUN', realAuthentication: 'NOT RUN', deployment: 'NOT RUN',
  };
}
