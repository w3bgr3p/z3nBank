const assert = require('node:assert/strict'), fs = require('node:fs'), vm = require('node:vm');
const source = fs.readFileSync('wwwroot/app.js', 'utf8');
const start = source.indexOf('async function setPin('), end = source.indexOf('async function importWallets(', start);
let requests = [], messages = [], attempts = ['неверный-PIN-🔐', 'correct'], cancel = false;
const dialog = {};
const context = vm.createContext({ API_BASE: '/api/treasury', btoa, TextEncoder, console,
    document: { querySelectorAll: () => [{}, dialog], body: {} },
    Swal: { isLoading: () => false, showValidationMessage: text => messages.push(text),
        fire: async options => {
            assert.equal(options.target, dialog, 'PIN appears inside the open DeFi dialog');
            if (cancel) return { isConfirmed: false };
            for (const pin of attempts) {
                const value = await options.preConfirm(pin);
                if (value) return { isConfirmed: true, value };
            }
            return { isConfirmed: false };
        } },
    fetch: async (url, options) => {
        requests.push({ url, body: options.body });
        if (url.endsWith('/pin')) {
            const good = JSON.parse(options.body).pin === btoa('correct');
            return { ok: good, json: async () => good ? { success: true } : { error: 'Incorrect PIN' } };
        }
        const good = requests.some(r => r.url.endsWith('/pin') && JSON.parse(r.body).pin === btoa('correct'));
        return { ok: good, status: good ? 204 : 400, clone() { return this; },
            json: async () => ({ code: 'pin_required', error: 'Enter wallet PIN', accountIds: [3] }) };
    }
});
vm.runInContext(source.slice(start, end), context);
(async () => {
    const response = await context.walletFetch('/execute', { method: 'POST', body: 'same-plan' });
    assert.equal(response.status, 204);
    assert.deepEqual(messages, ['Incorrect PIN']);
    assert.equal(Buffer.from(JSON.parse(requests[1].body).pin, 'base64').toString('utf8'), 'неверный-PIN-🔐');
    assert.equal(requests.filter(r => r.url === '/execute').length, 2);
    assert(requests.filter(r => r.url === '/execute').every(r => r.body === 'same-plan'));
    assert.deepEqual(JSON.parse(requests[1].body).accountIds, [3]);
    requests = []; cancel = true;
    await assert.rejects(context.walletFetch('/execute', { method: 'POST' }), e => e.pinCancelled === true);
    assert.equal(requests.length, 1, 'Cancelling PIN never retries the operation');
    console.log('PASS: retained wrong-PIN error, retry with same plan, DeFi modal target and safe cancellation');
})().catch(error => { console.error(error); process.exitCode = 1; });
