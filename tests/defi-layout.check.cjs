// Exercise repository assets in a real browser against the application's existing URL.
// Requests are intercepted inside this test; no server or transaction is started.
const { chromium } = require('playwright');
const path = require('node:path'), assert = require('node:assert/strict');
const origin = process.argv[2]; if (!origin) throw new Error('Pass the running application URL');
const base = { id: 'p1', accountId: 1, wallet: '0x' + '1'.repeat(40), chain: 'eth', protocol: 'Test Vault',
    type: 'staked', symbol: 'Q', amount: '1.25', valueUsd: 25, assetAddress: '0x' + '2'.repeat(40), vaultAddress: '0x' + '3'.repeat(40), groupId: 'g1' };
const status = { positions: [base, { ...base, id: 'p2', chain: 'arb', valueUsd: null },
    { ...base, id: 'p3', accountId: 2, type: 'loan', valueUsd: 5 }, { ...base, id: 'dust', accountId: 3, valueUsd: .005 },
    { ...base, id: 'secondary', accountId: 3, chain: 'arb', protocol: 'Other Vault', valueUsd: null }],
    accounts: [1, 2, 3].map(id => ({ id, address: '0x' + String(id).repeat(40), status: id === 3 ? 'pending' : 'scanned' })),
    total: 3, processed: 2, errors: [], running: false, preparing: false, exitRunning: false, batchResults: [] };
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
        const failures = []; page.on('pageerror', error => failures.push(error.message));
        let nativeDialogs = 0, executionRequests = 0;
        page.on('dialog', async dialog => { nativeDialogs++; await dialog.dismiss(); });
        await page.route('**/*', async route => {
            const url = new URL(route.request().url());
            if (url.origin !== origin) return route.abort();
            if (url.pathname === '/' || url.pathname === '/index.html') return route.fulfill({ path: path.resolve('wwwroot/index.html'), contentType: 'text/html' });
            if (url.pathname === '/defi.js') return route.fulfill({ path: path.resolve('wwwroot/defi.js'), contentType: 'text/javascript' });
            if (url.pathname === '/tokens.js' || url.pathname === '/app.js') return route.fulfill({ path: path.resolve('wwwroot' + url.pathname), contentType: 'text/javascript' });
            if (url.pathname === '/css/bank.css') return route.fulfill({ path: path.resolve('wwwroot/css/bank.css'), contentType: 'text/css' });
            if (url.pathname === '/api/Treasury/defi/status') return route.fulfill({ json: status });
            if (url.pathname === '/api/Treasury/defi/withdraw/preview') return route.fulfill({ json: { planId: 'read-only-test', quote: {
                amountRaw: '1161942500000000', decimals: 18, symbol: 'WETH', valueUsd: 3.1573, feeUsd: .0005249, l1FeeUsd: .00000155 } } });
            if (url.pathname.endsWith('/execute')) executionRequests++;
            if (route.request().method() !== 'GET') return route.fulfill({ status: 409, json: { error: 'Financial execution disabled in layout test' } });
            return route.continue();
        });
        await page.goto(origin, { waitUntil: 'domcontentloaded' });
        await page.locator('#loadingOverlay').evaluate(e => e.style.display = 'none');
        await page.locator('#defiToggle').click();
        await page.locator('#defiGridBody tr').first().waitFor();
        assert.equal(await page.locator('#defiGridBody tr').count(), 3);
        assert.equal(await page.locator('#defiResults').count(), 0, 'DeFi uses the shared log drawer instead of a separate results panel');
        const sidebar = await page.locator('.defi-sidebar').boundingBox(), grid = await page.locator('.defi-grid-wrap').boundingBox();
        assert(sidebar.x > grid.x + grid.width, 'Protocol summary occupies the right sidebar');
        assert((await page.locator('#defiProtocols').innerText()).includes('$20.00 ?'), 'Protocol summary subtracts debt and preserves unknown prices');
        assert((await page.locator('#defiChains').innerText()).includes('ARB') && (await page.locator('#defiChains').innerText()).includes('ETH'));
        const protocolChoice = page.locator('#defiProtocols .token-choice').filter({ hasText: 'Test Vault' });
        await protocolChoice.click();
        assert.equal(await page.locator('#defiProtocols .token-choice[aria-pressed="true"]').count(), 1);
        await page.locator('#defiProtocols .token-choice').filter({ hasText: 'Other Vault' }).click();
        assert.equal(await page.locator('#defiProtocols .token-choice[aria-pressed="true"]').count(), 2, 'Protocols support multiple selection');
        assert.equal(await page.locator('#defiBatch').isEnabled(), false, 'Batch withdrawal requires one exact protocol');
        await page.locator('#defiClearProtocols').click(); await protocolChoice.click();
        await protocolChoice.click();
        assert.equal(await page.locator('#defiProtocols .token-choice[aria-pressed="true"]').count(), 0, 'Repeated click deselects the protocol');
        assert.equal(await page.locator('.main-content').isVisible(), false);
        const dust = page.locator('#defiGridBody tr').nth(2).locator('.defi-heat.level-1').first();
        assert.equal(await dust.count(), 1, 'Sub-cent DeFi values use the dim Treasury level');
        assert.equal(await dust.evaluate(e => getComputedStyle(e).backgroundColor), 'rgb(51, 51, 51)');
        const bounds = await page.locator('#defiPanel').boundingBox();
        assert(bounds.width > 1500 && bounds.height > 650, 'Tab fills the available workspace');
        assert.equal(await page.locator('#defiGridBody input[type="checkbox"]').count(), 0);
        await page.locator('#defiGridBody tr').first().locator('.account-id-button').click();
        assert.equal(await page.locator('#defiGridBody .account-id-button.selected').count(), 1);
        await page.locator('#defiGridBody tr').first().locator('.account-id-button').click();
        assert.equal(await page.locator('#defiGridBody .account-id-button.selected').count(), 0);
        assert.equal(await page.locator('#defiGridBody .defi-heat').first().evaluate(e => getComputedStyle(e).alignItems), 'center');
        await page.locator('#defiGridBody tr').first().locator('.defi-heat').nth(1).click();
        assert.equal(await page.locator('#defiRows tr').count(), 1);
        assert(await page.locator('#defiDetail').isVisible());
        const detailsBounds = await page.locator('#defiDetail').boundingBox();
        assert(Math.abs(detailsBounds.x + detailsBounds.width / 2 - 800) < 10 && Math.abs(detailsBounds.y + detailsBounds.height / 2 - 450) < 10, 'Position operations dialog is centered in both axes');
        await page.locator('#defiRows button').first().click();
        await page.locator('#defiPreview').waitFor({ state: 'visible' });
        const confirmation = await page.locator('#defiPreview').boundingBox();
        assert(confirmation.width < 750 && Math.abs(confirmation.x + confirmation.width / 2 - 800) < 10, 'Confirmation is compact and centered');
        assert(Math.abs(confirmation.y + confirmation.height / 2 - 450) < 10, 'Confirmation is vertically centered');
        assert((await page.locator('#defiPreviewRows').innerText()).includes('$0.00000155'), 'Tiny fee remains visible');
        assert.equal(await page.locator('#defiPreview').evaluate(e => getComputedStyle(e).backgroundColor),
            await page.locator('#defiDetail').evaluate(e => getComputedStyle(e).backgroundColor), 'Confirmation follows the application popup theme');
        await page.screenshot({ path: 'bin/TokenValidation/defi-confirmation.png' });
        await page.locator('#defiPreviewCancel').click();
        assert.equal(nativeDialogs, 0); assert.equal(executionRequests, 0, 'Cancelling never executes a withdrawal');
        await page.locator('#defiClose').click();
        await page.locator('#defiProtocol').selectOption('Test Vault');
        await page.locator('#defiAccounts').fill('1-2'); await page.locator('#defiSelectRange').click();
        assert.equal(await page.locator('#defiGridBody .account-id-button.selected').count(), 2);
        assert.equal(await page.locator('#defiBatch').isEnabled(), true);
        await page.locator('#logsToggle').click(); assert(await page.locator('#logsPanel').isVisible());
        await page.locator('#logsPanel button[aria-label="Close logs"]').click();
        await page.screenshot({ path: 'bin/TokenValidation/defi-layout.png' });
        await page.locator('#treasuryTab').click(); assert(await page.locator('.main-content').isVisible());
        assert.equal(await page.locator('#defiPanel').isVisible(), false);
        await page.evaluate(() => {
            const token = (symbol, valueUSD, address) => ({ symbol, valueUSD, address, chainId: 1, amountRaw: '1', decimals: 0 });
            treasuryData = [{ id: 1, address: '0x' + '1'.repeat(40), chainData: { Ethereum: [token('Q', .005, '0x' + '2'.repeat(40))] } },
                { id: 2, address: '0x' + '3'.repeat(40), chainData: { Ethereum: [token('USDC', 2, '0x' + '4'.repeat(40))] } }];
            renderHeatmap(treasuryData); updateTopTokens(treasuryData);
        });
        assert.equal(await page.locator('#tokenSelect').count(), 0, 'Token dropdown is removed');
        await page.locator('.token-choice[data-symbol="Q"]').click();
        await page.locator('.token-choice[data-symbol="USDC"]').click();
        assert.equal(await page.locator('.treasury-account-id.selected').count(), 2, 'Token selection automatically selects matching accounts');
        assert.equal(await page.locator('#swapSelectedTokenButton').isEnabled(), true);
        await page.locator('.treasury-account-id[data-account-id="2"]').click();
        assert.equal(await page.locator('.treasury-account-id.selected').count(), 1, 'Click excludes only that account');
        assert.equal(await page.locator('.heatmap-cell.token-match').count(), 2, 'Token presence highlights survive account exclusion');
        await page.locator('.treasury-account-id[data-account-id="1"]').click();
        assert.equal(await page.locator('#swapSelectedTokenButton').isEnabled(), false, 'Excluding all accounts disables the swap');
        assert.equal(await page.locator('.heatmap-cell.token-match').count(), 2);
        await page.locator('.treasury-account-id[data-account-id="1"]').click();
        await page.locator('.treasury-account-id[data-account-id="2"]').click();
        assert.equal(await page.locator('.treasury-account-id.selected').count(), 2);
        assert.equal(await page.locator('#swapSelectedTokenButton').isEnabled(), true);
        assert.equal(await page.locator('#topTokens .token-choice[aria-pressed="true"]').count(), 2);
        assert.equal(await page.locator('.heatmap-cell.token-match').count(), 2, 'Both selected tokens highlight their cells');
        await page.locator('.token-choice[data-symbol="Q"]').click();
        assert.equal(await page.locator('#topTokens .token-choice[aria-pressed="true"]').count(), 1, 'Second click deselects one token');
        await page.locator('#clearTokenSelectionButton').click();
        assert.equal(await page.locator('.treasury-account-id.selected').count(), 0, 'Clearing token selection clears its account selection');
        assert.equal(await page.locator('.heatmap-cell.token-muted').count(), 0);
        await page.locator('.treasury-account-id[data-account-id="2"]').click();
        assert.equal(await page.locator('.treasury-account-id.selected').count(), 1);
        assert.equal(failures.filter(e => /defi/i.test(e)).length, 0, failures.join('\n'));
        console.log('PASS: real-browser tab layout, chain details, batch selection, log drawer and Treasury return; no transactions');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
