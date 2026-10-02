const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const elements = new Map(), dialogs = []; let refreshed = false;
const context = vm.createContext({ API_BASE: '/api/treasury', console,
    document: { getElementById: id => { if (!elements.has(id)) elements.set(id, {}); return elements.get(id); } },
    refreshData: async () => { refreshed = true; }, Swal: { fire: async data => dialogs.push(data) },
    fetch: async () => ({ ok: true, json: async () => ({ running: false, progress: {
        processed: 69, updated: 68, failed: 1, unprocessed: 31, stopped: true,
        failures: [{ accountId: 69, stage: 'Save balance snapshot', code: '57P03', reason: 'PostgreSQL восстанавливается. Повторите после готовности БД.', details: 'the database system is in recovery mode' }]
    } }) }) });
vm.runInContext(fs.readFileSync('wwwroot/app.js', 'utf8').match(/async function monitorBalanceUpdate[\s\S]*?\n}/)[0], context);
(async () => {
    await vm.runInContext('monitorBalanceUpdate()', context);
    assert(!refreshed, 'Stopped database update preserves displayed data instead of triggering another failed DB read');
    assert(elements.get('balanceUpdateStatus').textContent.includes('31 not attempted'));
    assert(dialogs[0].text.includes('57P03') && dialogs[0].text.includes('Save balance snapshot') && dialogs[0].text.includes('69') && dialogs[0].text.includes('PostgreSQL восстанавливается'));
    console.log('PASS: update report shows reason, SQLSTATE, stage, failed accounts and untouched count without replacing it with another database error');
})().catch(error => { console.error(error); process.exitCode = 1; });
