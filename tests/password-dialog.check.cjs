const { chromium } = require('playwright');
const path = require('node:path'), fs = require('node:fs'), assert = require('node:assert/strict');
const origin = process.argv[2];
if (!origin) throw new Error('Pass the running application URL');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        page.on('pageerror', error => console.error('Browser error:', error.message));
        let connected = false, submissions = 0, capsLock = true;
        let savedConfig = { type: 'postgres', host: 'saved-host', port: '5432', database: 'saved-db', user: 'saved-user', passwordSaved: true };
        let lastSubmission;
        await page.route('**/*', async route => {
            const url = new URL(route.request().url()), name = url.pathname.toLowerCase();
            if (url.origin !== origin) return route.continue();
            if (name.startsWith('/api/')) {
                if (name.endsWith('/db-status')) return route.fulfill({ json: { connected, config: savedConfig,
                    error: connected ? null : 'Saved database connection failed. Please check the password.' } });
                if (name.endsWith('/info')) return route.fulfill({ json: { baseDirectory: process.cwd() } });
                if (name.endsWith('/keyboard-status')) return route.fulfill({ json: { capsLock } });
                if (name.endsWith('/db-config')) {
                    if (route.request().method() === 'GET') return route.fulfill({ json: savedConfig });
                    submissions++;
                    const body = route.request().postDataJSON();
                    lastSubmission = body;
                    const accepted = body.password === 'correct-test-password' || body.useSavedPassword;
                    if (accepted) { connected = true; savedConfig = { ...body, password: undefined, passwordSaved: true }; }
                    return route.fulfill({ status: accepted ? 200 : 400, json: accepted ? { success: true } : {
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
        await page.locator('#swal-db-type').waitFor();
        assert.equal(await page.locator('#swal-db-type').inputValue(), 'postgres');
        assert.equal(await page.locator('#swal-host').inputValue(), 'saved-host');
        await page.locator('.swal2-validation-message').filter({ hasText: 'Saved database connection failed' }).waitFor();
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
        await page.locator('.swal2-popup').waitFor({ state: 'hidden', timeout: 5000 }).catch(async error => {
            throw new Error(error.message + '\nSubmissions: ' + submissions + '\nLast test request: ' + JSON.stringify(lastSubmission) + '\nPopup: ' + await page.locator('.swal2-popup').textContent());
        });
        assert.equal(submissions, 2, 'Successful retry submits exactly once');
        await page.evaluate(() => { void setDb(); });
        await page.locator('#swal-db-type').waitFor();
        assert.equal(await page.locator('#swal-db-type').inputValue(), 'postgres');
        assert.equal(await page.locator('#swal-db').inputValue(), 'saved-db');
        assert.equal(await page.locator('#swal-pass').inputValue(), '');
        await page.locator('#swal-pass').fill('bad-settings-password');
        await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-validation-message').filter({ hasText: 'Неверный пароль' }).waitFor();
        assert.equal(await page.locator('#swal-pass').inputValue(), 'bad-settings-password');
        await page.locator('.swal2-cancel').click();
        await page.evaluate(() => { void setDb(); });
        await page.locator('#swal-pass').waitFor();
        await page.locator('.swal2-confirm').click();
        await page.locator('.swal2-popup').waitFor({ state: 'hidden' });
        assert.equal(lastSubmission.useSavedPassword, true);
        assert.equal(lastSubmission.password, '');
        await page.reload({ waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(500);
        assert.equal(await page.locator('#swal-db-type').count(), 0, 'Connected startup never requests database credentials');
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
        console.log('PASS: saved connection prefill, readable startup error, retained password, connected restart, login retry and Caps Lock; no live DB settings changed');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
