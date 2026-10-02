const fs = require('fs');
const vm = require('vm');
const assert = require('assert');
let revision = 0, refreshes = 0;
const elements = { stopSwapsButton: {}, swapOperationsStatus: {} };
const context = vm.createContext({
    API_BASE: '/api/Treasury', console,
    document: { getElementById: id => elements[id], addEventListener() {} },
    fetch: async () => ({ ok: true, json: async () => ({ active: 0, stopping: false, balanceRevision: revision }) }),
    refreshData: async () => { refreshes++; }, setInterval() {}
});
vm.runInContext(fs.readFileSync('wwwroot/swaps.js', 'utf8'), context);
(async () => {
    await context.refreshSwapOperations();
    assert.equal(refreshes, 0);
    revision++;
    await context.refreshSwapOperations();
    assert.equal(refreshes, 1, 'A saved RPC snapshot refreshes the table even if a row swap finishes between polls');
    await context.refreshSwapOperations();
    assert.equal(refreshes, 1, 'Unchanged snapshots do not reload the table repeatedly');
    console.log('PASS: Treasury table refresh follows saved RPC revisions');
})().catch(error => { console.error(error); process.exitCode = 1; });
