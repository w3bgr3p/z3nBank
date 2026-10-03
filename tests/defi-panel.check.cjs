const assert = require('node:assert/strict'), fs = require('node:fs'), vm = require('node:vm');
const elements = new Map(), events = new WeakMap();
const make = (tag = '') => ({ tagName: tag.toUpperCase(), children: [], style: {}, value: '', hidden: false, open: false, disabled: false, textContent: '',
    append(...items) { this.children.push(...items); }, replaceChildren(...items) { this.children = items; },
    setAttribute() {}, addEventListener(name, fn, options) {
        if (!events.has(this)) events.set(this, []); events.get(this).push({ name, fn, once: options?.once });
    },
    querySelectorAll(selector) { const all = e => e.children.flatMap(c => [c, ...all(c)]); return all(this).filter(e => selector === 'button' ? e.tagName === 'BUTTON' : e.onclick); },
    showModal() { this.open = true; }, close() {
        if (!this.open) return; this.open = false;
        const listeners = events.get(this) || []; events.set(this, listeners.filter(e => !e.once));
        listeners.filter(e => e.name === 'close').forEach(e => e.fn());
    } });
const el = id => { if (!elements.has(id)) elements.set(id, make()); return elements.get(id); };
el('maxIdInput').value = '100'; el('defiGas').value = '2';
const position = { id: 'p1', accountId: 3, wallet: '0x' + '1'.repeat(40), chain: 'eth', protocol: '<script>bad</script>',
    type: 'staked', symbol: 'Q', amount: '1', valueUsd: 10, vaultAddress: '0x' + '2'.repeat(40), assetAddress: '0x' + '3'.repeat(40), groupId: 'pool' };
const quote = { amountRaw: '123456789123456789', decimals: 18, symbol: 'Q', valueUsd: 10, feeUsd: 1 };
let confirmed = false, requests = [], confirmText = '', executeError = '', executeGate = null, current = {
    running: false, processed: 2, total: 3, cancelled: false,
    accounts: [{ id: 3, address: position.wallet, status: 'scanned' }, { id: 4, address: 'pending-wallet', status: 'pending' }],
    positions: [position, { ...position, id: 'loan', type: 'loan', valueUsd: 3 },
        { ...position, id: 'unknown', chain: 'arb', type: 'locked', valueUsd: null }], errors: [], exitRunning: false
};
const context = vm.createContext({ console, setTimeout: () => 1, clearTimeout() {},
    window: { getGasBoostPercent: () => 2, confirm: () => { throw new Error('Native confirm must never be used'); } },
    document: { createElement: make, getElementById: el, querySelector: () => null,
        body: { classList: { toggle() {} }, append(e) { elements.set(e.id, e); } } },
    fetch: async (url, options = {}) => {
        requests.push({ url, body: options.body && JSON.parse(options.body) });
        if (url.endsWith('refresh-account')) {
            current = { ...current, positions: current.positions.map(p => p.accountId === JSON.parse(options.body) ? { ...p, protocolId: 'fresh-protocol', withdrawalReason: null } : p) };
        }
        if (url.endsWith('execute')) {
            if (executeGate) await executeGate;
            if (executeError) return { ok: false, json: async () => ({ error: executeError }) };
        }
        const data = url.endsWith('batch/preview') ? { planId: 'batch-id', targets: [{ position, quote }],
            accounts: 1, totalUsd: 10, feeUsd: 1, gasBoostPercent: 2, skipped: [{ accountId: 4, chain: 'arb', reason: 'Adapter unavailable' }] }
            : url.endsWith('preview') ? { planId: 'preview-id', position, quote } : url.endsWith('status') ? current : {};
        return { ok: true, json: async () => data };
    }
});
vm.runInContext(fs.readFileSync('wwwroot/app.js', 'utf8').match(/function getValueLevel[\s\S]*?\n}/)[0], context);
vm.runInContext(fs.readFileSync('wwwroot/defi.js', 'utf8'), context);
const flush = () => new Promise(resolve => setImmediate(resolve));
const detailButton = () => el('defiRows').children[0].children[6].children[0];
const textOf = e => e.textContent + ' ' + e.children.map(textOf).join(' ');
async function singlePreview(accept = false) {
    const pending = detailButton().onclick(); await flush();
    assert(el('defiPreview').open, 'Single withdrawal uses an application dialog');
    assert.equal(el('defiPreview').className, 'defi-panel defi-confirm');
    confirmText = textOf(el('defiPreviewRows'));
    el(accept ? 'defiPreviewConfirm' : 'defiPreviewCancel').onclick(); await pending;
}
(async () => {
    context.window.setDefiOpen(true); await flush();
    assert.equal(el('defiPanel').hidden, false, 'DeFi is a separate workspace tab');
    assert.equal(el('defiGridBody').children.length, 2, 'Pending accounts remain visible');
    const row = el('defiGridBody').children[0];
    assert.equal(row.children[2].children[0].textContent, '?', 'Unknown prices never become zero');
    assert.equal(row.children[3].children[0].textContent, '$7.00', 'Loans subtract from network totals');
    assert.equal(row.children[4].children[0].textContent, '$7.00 ?', 'Account total preserves unknown valuation');
    row.children[0].children[0].onclick(); assert.equal(el('defiSelected').textContent, '1 selected');
    el('defiGridBody').children[0].children[0].children[0].onclick(); assert.equal(el('defiSelected').textContent, '0 selected');
    row.children[3].children[0].onclick();
    assert(el('defiDetail').open);
    assert.equal(el('defiRows').children.length, 2, 'Cell opens details for exactly that account and network');
    assert.equal(el('defiRows').children[0].children[1].textContent, position.protocol, 'Protocol text is never HTML');
    assert.equal(el('defiRows').children[1].children[6].children.length, 0, 'Loan cannot start a withdrawal');
    current.running = true; current.processed = 77; current.total = 100;
    context.window.setDefiOpen(true); await flush();
    assert(!detailButton().disabled && !el('defiDetailBlocked').hidden);
    assert(el('defiDetailBlocked').textContent.includes('77/100') && el('defiDetailBlocked').textContent.includes('This account is ready'));
    current.accounts[0].status = 'pending'; context.window.setDefiOpen(true); await flush();
    assert(detailButton().disabled && detailButton().title.includes('has not finished scanning'));
    current.accounts[0].status = 'scanned';
    current.running = false; current.needsRescan = true;
    context.window.setDefiOpen(true); await flush();
    assert(detailButton().disabled && el('defiDetailBlocked').textContent.includes('Scan this account again'));
    current.accounts[1].status = 'stale'; current.requiresFullRescan = false;
    context.window.setDefiOpen(true); await flush();
    assert(!detailButton().disabled, 'A transaction on another account does not block this scanned account');
    current.accounts[0].status = 'stale'; context.window.setDefiOpen(true); await flush();
    assert(detailButton().disabled, 'Changed account cannot reuse its stale positions');
    current.accounts[0].status = 'scanned'; current.accounts[1].status = 'pending';
    current.needsRescan = false;
    context.window.setDefiOpen(true); await flush();
    assert(!detailButton().disabled && el('defiDetailBlocked').hidden && !detailButton().title);
    await singlePreview();
    assert(!requests.some(r => r.url.endsWith('execute')), 'Cancelled preview never executes');
    assert(confirmText.includes('0.123456789123456789'), 'Exact token amount survives preview');
    executeError = 'Set wallet PIN first';
    const refused = detailButton().onclick(); await flush();
    await el('defiPreviewConfirm').onclick();
    assert(el('defiPreview').open, 'Rejected execution keeps the preview open');
    assert(el('defiPreviewStatus').textContent.includes(executeError), 'Execution refusal is visible inside the preview');
    executeError = ''; let acceptRequest;
    executeGate = new Promise(resolve => { acceptRequest = resolve; });
    const requestCount = requests.length;
    const starting = el('defiPreviewConfirm').onclick(); await flush();
    assert(el('defiPreview').open && el('defiPreviewConfirm').disabled && el('defiPreviewCancel').disabled);
    await el('defiPreviewConfirm').onclick();
    assert.equal(requests.length, requestCount + 1, 'Repeated confirmation cannot send duplicate execute requests');
    acceptRequest(); await starting; await refused; executeGate = null;
    assert(!el('defiPreview').open, 'Only server acceptance closes the confirmation');
    quote.outputs = [{ amountRaw: '634406', decimals: 6, symbol: 'USDC.e' },
        { amountRaw: '231691997655196', decimals: 18, symbol: 'WETH' }];
    quote.approval = { destination: position.vaultAddress };
    await singlePreview();
    assert(confirmText.includes('0.634406 USDC.e + 0.000231691997655196 WETH'), 'Both LP outputs are visible with exact precision');
    assert(confirmText.includes('Total fee includes every step') && confirmText.includes('checked again before sending'), 'Confirmation explains approval cost and simulation recheck');
    await singlePreview(true);
    assert.equal(requests.find(r => r.url.endsWith('withdraw/execute')).body, 'preview-id');
    await el('defiScan').onclick();
    assert.deepEqual(requests.find(r => r.url.endsWith('/scan')).body, { maxId: 100 });
    el('defiProtocol').value = position.protocol; el('defiProtocol').onchange();
    el('defiAccounts').value = '3-4, 3'; el('defiSelectRange').onclick();
    assert.equal(el('defiSelected').textContent, '2 selected');
    const before = requests.length; const cancelled = el('defiBatch').onclick(); await flush();
    assert(el('defiPreview').open); assert(el('defiPreviewRows').children[1].textContent.includes('Skipped: Adapter unavailable'));
    assert(el('defiPreviewRows').children[0].textContent.includes('USDC.e +') && el('defiPreviewRows').children[0].textContent.includes('approval + withdrawal gas reserve'), 'Batch shows both outputs and approval cost');
    el('defiPreviewCancel').onclick(); await cancelled;
    assert(!requests.slice(before).some(r => r.url.endsWith('execute')));
    const accepted = el('defiBatch').onclick(); await flush(); el('defiPreviewConfirm').onclick(); await accepted;
    assert.equal(requests.find(r => r.url.endsWith('batch/execute')).body, 'batch-id');
    assert.deepEqual(requests.find(r => r.url.endsWith('batch/preview')).body, { protocol: position.protocol, accountIds: [3, 4], gasBoostPercent: 2 });
    el('defiAccounts').value = '4-3'; el('defiSelectRange').onclick(); assert.equal(el('defiSelected').textContent, '2 selected');
    current = { ...current, exitRunning: true }; context.window.setDefiOpen(false); context.window.setDefiOpen(true); await flush();
    assert(el('defiScan').disabled); assert(!el('defiStopExit').disabled); await el('defiStopExit').onclick();
    assert(requests.some(r => r.url === '/api/Treasury/swaps/stop'));
    current = { ...current, exitRunning: false, positions: [{ ...position, withdrawalReason: 'Protocol exit is not ERC-4626' }] };
    context.window.setDefiOpen(false); context.window.setDefiOpen(true); await flush();
    el('defiGridBody').children[0].children[2].children[0].onclick();
    assert.equal(el('defiRows').children[0].children[6].children.length, 0, 'Unsupported contracts never offer an ERC-4626 withdrawal');
    el('defiRows').children[0].children[6].onclick();
    assert.equal(el('defiDetailStatus').textContent, 'Protocol exit is not ERC-4626');
    current = { ...current, actionsVersion: 2 };
    context.window.setDefiOpen(false); context.window.setDefiOpen(true); await flush();
    el('defiGridBody').children[0].children[2].children[0].onclick(); await flush();
    assert.equal(requests.find(r => r.url.endsWith('refresh-account')).body, 3, 'Old cached actions refresh only the opened account');
    assert.equal(current.positions[0].protocolId, 'fresh-protocol');
    assert(detailButton(), 'Refreshed actions replace the old unsupported snapshot without scanning all accounts');
    current = { ...current, positions: [{ ...position, protocol: 'GMX V2', protocolId: 'gmx2', adapterId: 'gmx2_liquidity', pendingWithdrawal: true }] };
    quote.stage = 'cancel'; quote.notice = 'Returns GM tokens after cancellation.';
    context.window.setDefiOpen(false); context.window.setDefiOpen(true); await flush();
    el('defiGridBody').children[0].children[2].children[0].onclick();
    const cancelRequest = el('defiRows').children[0].children[6].children[1];
    assert.equal(cancelRequest.textContent, 'Cancel request');
    const cancellation = cancelRequest.onclick(); await flush();
    assert.equal(el('defiPreviewTitle').textContent, 'Confirm request cancellation');
    assert.equal(requests.filter(r => r.url.endsWith('withdraw/preview')).at(-1).body.action, 'cancel');
    el('defiPreviewCancel').onclick(); await cancellation;
    context.window.setDefiOpen(false); assert(el('defiPanel').hidden);
    console.log('PASS: DeFi tab, net heatmap, unknown values, account/network modal, batch selection, preview, confirmation, cancellation and stop');
})().catch(e => { console.error(e); process.exitCode = 1; });
