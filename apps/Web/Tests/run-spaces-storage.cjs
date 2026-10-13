const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const output = process.env.SPACES_STORAGE_EVIDENCE;
const priorModulePath = process.env.SPACES_ROLLBACK_MODULE;
if (!output) throw new Error('SPACES_STORAGE_EVIDENCE must identify an isolated evidence directory.');
fs.mkdirSync(output, { recursive: true });
const server = http.createServer((request, response) => {
  const pathname = new URL(request.url, 'http://127.0.0.1').pathname;
  if (pathname === '/__prior-tasks-indexeddb.js' && priorModulePath) {
    response.writeHead(200, { 'Content-Type': 'text/javascript', 'Cache-Control': 'no-store' }); fs.createReadStream(priorModulePath).pipe(response); return;
  }
  if (pathname === '/') { response.writeHead(200, { 'Content-Type': 'text/html' }); response.end('<title>Real Spaces storage controls</title>'); return; }
  const file = path.resolve(root, '.' + pathname);
  if (!file.startsWith(root + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404); response.end(); return; }
  response.writeHead(200, { 'Content-Type': 'text/javascript', 'Cache-Control': 'no-store' }); fs.createReadStream(file).pipe(response);
});
(async () => {
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  let browser;
  try {
    browser = await chromium.launch({ executablePath: '/usr/bin/chromium', headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage'] });
    const context = await browser.newContext(); const page = await context.newPage();
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    page.on('filechooser', async chooser => { try { if (await page.evaluate(() => globalThis.__productionCancelPicker === true)) return; await chooser.setFiles({ name: 'original-proof.txt', mimeType: 'text/plain', buffer: Buffer.from('Original selected file') }); } catch (error) { if (!String(error).includes('detached') && !String(error).includes('not attached')) throw error; } });
    const results = await page.evaluate(async usePrior => {
      const production = await import('/Spaces/Tests/conversation-production-storage-controls.js');
      const settings = await import('/Spaces/Tests/space-settings-storage-controls.js');
      const conversations = await import('/Spaces/Tests/conversation-storage-controls.js');
      const events = await import('/Spaces/Tests/execution-event-storage-controls.js');
      const prior = usePrior ? (await import('/__prior-tasks-indexeddb.js')).createTaskExecutionModule : null;
      const results = {};
      const describe = error => ({ message: String(error), stack: error?.stack,
        errors: error?.errors ? [...error.errors].map(describe) : undefined });
      for (const [name, run] of [['production', production.runConversationProductionStorageControls], ['settings', () => settings.runSpaceSettingsStorageControls(prior)],
        ['conversations', conversations.runConversationStorageControls], ['events', events.runExecutionEventStorageControls]])
        try { results[name] = { passed: true, controls: await run() }; }
        catch (error) { results[name] = { passed: false, ...describe(error) }; }
      return results;
    }, Boolean(priorModulePath));
    results.browserVersion = browser.version();
    fs.writeFileSync(path.join(output, 'spaces-storage-results.json'), JSON.stringify(results, null, 2));
    console.log(JSON.stringify(results)); await context.close();
    if (['production', 'settings', 'conversations', 'events'].some(name => !results[name].passed)) process.exitCode = 1;
  } finally { if (browser) await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });
