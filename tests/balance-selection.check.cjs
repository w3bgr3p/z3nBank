const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const requests = [], dialogs = [];
const selected = new Set([3, 8]);
let changeSelection = false;
const context = vm.createContext({
    selectedTreasuryAccounts: selected, API_BASE: '/api/treasury', console,
    document: { getElementById: () => ({ value: '100' }) },
    Swal: { fire: async options => {
        dialogs.push(options); if (changeSelection) selected.clear(); return { isConfirmed: true };
    } },
    fetch: async url => { requests.push(url); return { ok: true, json: async () => ({ message: 'Update started' }) }; },
    monitorBalanceUpdate: async () => {}
});
vm.runInContext(fs.readFileSync('wwwroot/app.js', 'utf8').match(/async function updateBalances[\s\S]*?\n}/)[0], context);
(async () => {
    changeSelection = true;
    await vm.runInContext('updateBalances()', context);
    assert(requests[0].endsWith('&accountIds=3&accountIds=8'), 'The confirmed selection is frozen before the request');
    assert(dialogs[0].text.includes('2 selected accounts'));
    changeSelection = false;
    await vm.runInContext('updateBalances()', context);
    assert(!requests[1].includes('accountIds'), 'No selection retains the all-account request');
    assert(dialogs[2].text.includes('all accounts up to Max ID 100'));
    console.log('PASS: Treasury Update Balances submits the selected IDs, freezes confirmation scope and falls back to all accounts only for empty selection');
})().catch(error => { console.error(error); process.exitCode = 1; });
