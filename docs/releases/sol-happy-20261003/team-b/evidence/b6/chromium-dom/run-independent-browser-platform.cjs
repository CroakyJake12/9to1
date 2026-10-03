const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

const root = '/workspace/team-b-worktree/apps/Web';
const output = process.env.B1_BROWSER_EVIDENCE;
if (!output) throw new Error('B1_BROWSER_EVIDENCE must identify the isolated evidence output directory.');
fs.mkdirSync(output, { recursive: true });
const server = http.createServer((request, response) => {
    const pathname = new URL(request.url, 'http://127.0.0.1').pathname;
    const file = path.resolve(root, '.' + pathname);
    if (!file.startsWith(root + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
        response.writeHead(404); response.end(); return;
    }
    const type = file.endsWith('.html') ? 'text/html' : file.endsWith('.js') ? 'text/javascript' : 'application/octet-stream';
    response.writeHead(200, { 'Content-Type': type, 'Cache-Control': 'no-store' });
    fs.createReadStream(file).pipe(response);
});

(async () => {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(18762, '127.0.0.1', resolve); });
    let browser;
    try {
        browser = await chromium.launch({ executablePath: '/usr/bin/chromium', headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage'] });
        const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
        const page = await context.newPage();
        await page.goto('http://127.0.0.1:18762/Tests/browser-platform.browser.html');
        await page.waitForFunction(() => window.browserPlatformResults, { timeout: 10000 });
        const results = await page.evaluate(() => window.browserPlatformResults);
        results.browserVersion = browser.version();
        fs.writeFileSync(path.join(output, 'chromium-dom-results.json'), JSON.stringify(results, null, 2));
        await page.screenshot({ path: path.join(output, 'chromium-dom-tests.png'), fullPage: true });
        await context.tracing.stop({ path: path.join(output, 'chromium-dom-trace.zip') });
        assert.equal(results.discovered, 7); assert.equal(results.executed, 7); assert.equal(results.failed, 0);
        console.log(JSON.stringify(results));

        // Controlled negative test mutates only this isolated response: prove that
        // duplicate event delivery is detected by the same unchanged assertions.
        const faultContext = await browser.newContext();
        await faultContext.route('**/wwwroot/browser-platform.js', async route => {
            const source = fs.readFileSync(path.join(root, 'wwwroot/browser-platform.js'), 'utf8');
            assert(source.includes('if (fragment === lastFragment) return;'));
            await route.fulfill({ contentType: 'text/javascript', body: source.replace('if (fragment === lastFragment) return;', '') });
        });
        const faultPage = await faultContext.newPage();
        await faultPage.goto('http://127.0.0.1:18762/Tests/browser-platform.browser.html');
        await faultPage.waitForFunction(() => window.browserPlatformResults, { timeout: 10000 });
        const negative = await faultPage.evaluate(() => window.browserPlatformResults);
        fs.writeFileSync(path.join(output, 'chromium-dom-negative-results.json'), JSON.stringify(negative, null, 2));
        assert(negative.records.some(record => record.id === 'B1-DOM-03-real-back-forward-dedup' && record.outcome === 'FAIL'), 'Detector did not reject duplicate event regression.');
        console.log('PASS controlled negative test: duplicate-event mutation rejected by real-history assertions.');
        await faultContext.close();
        await context.close();
    } finally {
        if (browser) await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
