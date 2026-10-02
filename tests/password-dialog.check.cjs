const { chromium } = require('playwright');
const path = require('node:path'), fs = require('node:fs'), assert = require('node:assert/strict');
const origin = process.argv[2];
if (!origin) throw new Error('Pass the running application URL');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        let connected = false, submissions = 0, capsLock = true;
        await page.route('**/*', async route => {
            const url = new URL(route.request().url()), name = url.pathname.toLowerCase();
            if (url.origin !== origin) return route.continue();
            if (name.startsWith('/api/')) {
                if (name.endsWith('/db-status')) return route.fulfill({ json: { connected } });
                if (name.endsWith('/info')) return route.fulfill({ json: { baseDirectory: process.cwd() } });
                if (name.endsWith('/keyboard-status')) return route.fulfill({ json: { capsLock } });
                if (name.endsWith('/db-config')) {
                    submissions++;
                    const body = route.request().postDataJSON();
                    connected = body.password === 'correct-test-password';
                    return route.fulfill({ status: connected ? 200 : 400, json: connected ? { success: true } : {
                        success: false, code: 'invalid_credentials', error: 'Неверный пароль или имя пользователя PostgreSQL.' } });
                }
                return route.fulfill({ json: { status: 'WAIT', success: true } });
            }
            const file = path.resolve('wwwroot', name === '/' ? 'index.html' : url.pathname.slice(1));
            if (fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file,
                contentType: file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : file.endsWith('.html') ? 'text/html' : undefined });
            return route.continue();
        });
        await page.goto(origin, { waitUntil: 'domcontentloaded' });
        await page.locator('#swal-db-type').selectOption('postgres');
        await page.locator('#swal-pass').focus();
        await page.locator('.caps-lock-hint:not([hidden])').waitFor();
        await page.locator('#swal-pass').fill('wrong-test-password');
        await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-validation-message').filter({ hasText: 'Неверный пароль' }).waitFor();
        assert.equal(await page.locator('#swal-pass').inputValue(), 'wrong-test-password');
        assert.equal(await page.locator('#swal-db-type').inputValue(), 'postgres');
        assert.equal(submissions, 1, 'Failed login stays in the same form without resubmitting');
        await page.locator('#swal-pass').fill('correct-test-password');
        await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-popup').waitFor({ state: 'hidden' });
        assert.equal(submissions, 2, 'Successful retry submits exactly once');
        await page.evaluate(() => { void setDb(); });
        await page.locator('#swal-db-type').selectOption('postgres');
        await page.locator('#swal-pass').fill('bad-settings-password');
        await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-validation-message').filter({ hasText: 'Неверный пароль' }).waitFor();
        assert.equal(await page.locator('#swal-pass').inputValue(), 'bad-settings-password');
        await page.locator('.swal2-cancel').click();
        await page.evaluate(() => { void setPin(); });
        await page.locator('input.swal2-input[type="password"]').focus();
        await page.locator('.caps-lock-hint:not([hidden])').waitFor();
        capsLock = false;
        await page.evaluate(() => {
            const event = new KeyboardEvent('keyup', { key: 'CapsLock', bubbles: true });
            Object.defineProperty(event, 'getModifierState', { value: () => false });
            document.activeElement.dispatchEvent(event);
        });
        assert.equal(await page.locator('.caps-lock-hint:not([hidden])').count(), 0);
        console.log('PASS: real-browser startup/settings errors preserve fields and retry; Caps Lock appears for DB password and PIN and clears when off; no live DB settings changed');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
