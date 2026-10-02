const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const address = '0x00000000000000000000000000000000000000a1';
const q = { symbol: 'Q', chainId: 1, address, valueUSD: 5 };
const eth = { symbol: 'ETH', chainId: 1, address: '0x' + '0'.repeat(40), valueUSD: 10 };
const usdc = { symbol: 'USDC', chainId: 8453, address: '0x' + '2'.repeat(40), valueUSD: 3 };
const treasuryData = [
    { id: 1, chainData: { Ethereum: [q, eth] } },
    { id: 2, chainData: { Base: [{ ...q, chainId: 8453 }, usdc] } }
];
const makeElement = () => ({ value: '', textContent: '', disabled: false,
    append() {}, replaceChildren() {} });
const elements = Object.fromEntries(['clearTokenSelectionButton', 'tokenSelectionInfo', 'swapSelectedTokenButton',
    'clearTreasuryAccountsButton', 'treasuryAccountSelectionInfo',
    'tokenSwapStatus', 'maxIdInput', 'thresholdInput', 'protocolSelect'].map(id => [id, makeElement()]));
elements.maxIdInput.value = '100';
elements.thresholdInput.value = '0.1';
elements.protocolSelect.value = 'Relay';
const cells = [[q], [eth], [{ ...q, chainId: 8453 }]].map(tokens => ({
    dataset: { tokens: JSON.stringify(tokens) }, classes: new Set(),
    classList: { toggle(name, enabled) { enabled ? this.owner.classes.add(name) : this.owner.classes.delete(name); } }
}));
cells.forEach((cell, index) => { cell.classList.owner = cell; cell.dataset.accountId = String(index === 2 ? 2 : 1); });
const choices = ['Q', 'ETH', 'USDC'].map(symbol => ({ dataset: { symbol }, attributes: {}, classes: new Set(),
    setAttribute(k, v) { this.attributes[k] = v; }, classList: { toggle(k, enabled) { enabled ? this.owner.classes.add(k) : this.owner.classes.delete(k); } } }));
choices.forEach(row => row.classList.owner = row);
let confirmed = false;
let refreshed = 0;
const calls = [];
const context = vm.createContext({ treasuryData, API_BASE: '/api/treasury', console, setTimeout,
    Option: class {}, formatUSD: value => `$${value}`, getSelectedChains: () => ['Ethereum', 'Base'], getGasBoostPercent: () => 2,
    refreshData: async () => { refreshed++; },
    document: { getElementById: id => elements[id], querySelectorAll: selector => selector.includes('token-choice') ? choices : selector.includes('treasury-account-id') ? [] : cells,
        createElement: makeElement, addEventListener() {} },
    Swal: { fire: async () => ({ isConfirmed: confirmed }) },
    fetch: async (url, options) => {
        calls.push({ url, body: options.body && JSON.parse(options.body) });
        const data = url.endsWith('/preview') ? { planId: 'plan1', accounts: 2, totalUSD: 10, gasBoostPercent: 2,
            targets: [{ id: 1, chain: 'Ethereum', symbol: 'Q', address, valueUSD: 5 }] }
            : url.endsWith('/execute') ? { jobId: 'plan1' }
            : { jobId: 'plan1', running: false, total: 2, results: [
                { id: 1, succeeded: 1, failed: 0, skipped: 0 }, { id: 2, succeeded: 1, failed: 0, skipped: 0 }] };
        return { ok: true, json: async () => data };
    }
});
vm.runInContext(fs.readFileSync('wwwroot/tokens.js', 'utf8'), context);

(async () => {
    vm.runInContext("selectTreasuryToken('Q')", context);
    assert(!elements.swapSelectedTokenButton.disabled, 'Choosing a token selects all its accounts automatically');
    assert(elements.treasuryAccountSelectionInfo.textContent.includes('2 accounts selected'));
    vm.runInContext('clearTreasuryAccounts()', context);
    assert(elements.swapSelectedTokenButton.disabled, 'No selected accounts cannot start a swap');
    await vm.runInContext('swapSelectedToken()', context);
    assert.equal(calls.length, 0, 'An empty account selection never falls back to all accounts');
    vm.runInContext('toggleTreasuryAccount(1); toggleTreasuryAccount(2)', context);
    assert(cells[0].classes.has('token-match'));
    assert(cells[1].classes.has('token-muted'));
    assert(cells[2].classes.has('token-match'));
    assert(elements.tokenSelectionInfo.textContent.includes('2 accounts'));
    await vm.runInContext('swapSelectedToken()', context);
    assert.equal(calls.length, 1, 'Cancel must never execute a swap');
    assert.equal(calls[0].body.gasBoostPercent, 2, 'Selected gas percentage must reach the immutable execution preview');
    assert.deepEqual(calls[0].body.assets, [{ chainId: 1, address }, { chainId: 8453, address }]);
    assert.deepEqual(calls[0].body.accountIds, [1, 2], 'Explicit account IDs reach the frozen swap preview');
    vm.runInContext('toggleTreasuryAccount(2)', context);
    assert(cells[2].classes.has('token-match'), 'Excluding an account keeps the token presence highlighted');
    assert(elements.treasuryAccountSelectionInfo.textContent.includes('1 accounts selected'));
    assert.equal(vm.runInContext('JSON.stringify(selectedTokenEntries().map(e => e.account.id))', context), '[1]', 'Excluded account is absent from swap entries');
    vm.runInContext("selectTreasuryToken('USDC')", context);
    assert(elements.treasuryAccountSelectionInfo.textContent.includes('1 accounts selected'), 'Adding another token retains explicit account exclusions');
    vm.runInContext('toggleTreasuryAccount(2)', context);
    assert.equal(choices[0].attributes['aria-pressed'], 'true');
    assert.equal(choices[2].attributes['aria-pressed'], 'true');
    await vm.runInContext('swapSelectedToken()', context);
    assert.deepEqual(calls[1].body.assets, [{ chainId: 1, address }, { chainId: 8453, address }, { chainId: 8453, address: usdc.address }]);
    vm.runInContext("selectTreasuryToken('Q')", context);
    assert.equal(choices[0].attributes['aria-pressed'], 'false', 'Second click deselects just that token');
    assert.equal(choices[2].attributes['aria-pressed'], 'true', 'Other selected tokens are retained');
    confirmed = true;
    await vm.runInContext('swapSelectedToken()', context);
    assert.equal(calls.find(c => c.url.endsWith('/execute')).body, 'plan1');
    assert.equal(refreshed, 1);
    vm.runInContext("clearTreasuryTokens(); selectTreasuryToken('ETH')", context);
    assert(elements.swapSelectedTokenButton.disabled, 'Native token cannot be swapped to itself');
    vm.runInContext("clearTreasuryTokens()", context);
    assert(cells.every(c => !c.classes.has('token-match') && !c.classes.has('token-muted')));
    confirmed = false;
    vm.runInContext("selectTreasuryToken('Q'); toggleTreasuryAccount(2)", context);
    await vm.runInContext('swapSelectedToken()', context);
    const restricted = calls.filter(c => c.url.endsWith('/preview')).at(-1).body;
    assert.deepEqual(restricted.accountIds, [1], 'Exclusions restrict the actual server preview');
    assert.deepEqual(restricted.assets, [{ chainId: 1, address }]);
    console.log('PASS: highlighting, exact contract selection, cancellation, confirmed execution, progress and native exclusion');
})().catch(error => { console.error(error); process.exitCode = 1; });
