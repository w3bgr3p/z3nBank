const { chromium } = require('playwright');
const http = require('node:http'), fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const server = http.createServer((req, res) => {
    const file = path.join(process.cwd(), 'wwwroot', req.url === '/' ? 'index.html' : req.url.split('?')[0]);
    if (!fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); return res.end(); }
    res.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'text/html');
    res.end(fs.readFileSync(file));
});
(async () => {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = `http://127.0.0.1:${server.address().port}`;
    console.log('PIN browser checks URL: ' + origin);
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        let unlocked = false, executed = 0, submissions = 0;
        const correctPin = 'верный-PIN-é-🔐';
        await page.route('**/api/**', route => {
            const name = new URL(route.request().url()).pathname.toLowerCase();
            if (name.endsWith('/db-status')) return route.fulfill({ json: { connected: true } });
            if (name.endsWith('/keyboard-status')) return route.fulfill({ json: { capsLock: false } });
            if (name.endsWith('/data') || name.endsWith('/chains') || name.endsWith('/logs')) return route.fulfill({ json: [] });
            if (name.endsWith('/pin')) {
                submissions++;
                unlocked = Buffer.from(route.request().postDataJSON().pin, 'base64').toString('utf8') === correctPin;
                return route.fulfill({ status: unlocked ? 200 : 400, json: unlocked ? { success: true } : { code: 'invalid_pin', error: 'Incorrect PIN. Try again.' } });
            }
            if (name.endsWith('/test-execute')) {
                if (unlocked) { executed++; return route.fulfill({ status: 204 }); }
                return route.fulfill({ status: 400, json: { code: 'pin_required', error: 'Enter wallet PIN to continue', accountIds: [1] } });
            }
            return route.fulfill({ json: { active: 0, running: false, positions: [], accounts: [], errors: [], results: [] } });
        });
        await page.goto(origin, { waitUntil: 'networkidle' });
        await page.evaluate(() => { void setPin(); });
        const input = page.locator('input.swal2-input[type="password"]');
        await input.fill('неверный-PIN'); await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-validation-message').filter({ hasText: 'Incorrect PIN' }).waitFor();
        assert.equal(await input.inputValue(), 'неверный-PIN');
        assert.equal(submissions, 1); assert.equal(executed, 0);
        await input.fill(correctPin); await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-popup').waitFor({ state: 'hidden' });
        unlocked = false;
        await page.evaluate(() => {
            document.getElementById('defiDetail').showModal();
            document.getElementById('defiPreview').showModal();
            window.pinOperation = walletFetch('/api/treasury/test-execute', { method: 'POST', body: 'same-plan' }).then(r => r.status).catch(e => e.pinCancelled ? 'cancelled' : e.message);
        });
        await input.waitFor();
        assert.equal(await page.locator('#defiPreview .swal2-popup').count(), 1);
        await input.fill('wrong'); await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-validation-message').filter({ hasText: 'Incorrect PIN' }).waitFor();
        assert.equal(executed, 0);
        await input.fill(correctPin); await page.locator('.swal2-confirm').click();
        assert.equal(await page.evaluate(() => window.pinOperation), 204); assert.equal(executed, 1);
        unlocked = false;
        await page.evaluate(() => {
            window.pinOperation = walletFetch('/api/treasury/test-execute', { method: 'POST' }).catch(e => e.pinCancelled ? 'cancelled' : e.message);
        });
        await page.locator('.swal2-cancel').click();
        assert.equal(await page.evaluate(() => window.pinOperation), 'cancelled'); assert.equal(executed, 1);
        assert.deepEqual(errors, []);
        console.log('PASS: browser PIN retry, readable error, focus and clicks with BOTH DeFi dialogs open, continuation and cancellation');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(() => server.close());
