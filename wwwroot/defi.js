(() => {
    let state = null, timer, busy = false, message = '', detail = null, confirmationPending = false;
    const selected = new Set(), protocolsSelected = new Set(), el = id => document.getElementById(id);
    const view = document.createElement('section');
    view.id = 'defiPanel'; view.className = 'defi-view'; view.hidden = true;
    view.innerHTML = `<div class="defi-controls">
        <strong>DeFi</strong><label>Max ID <input id="defiMaxId" type="number" min="1" max="10000" value="100"></label>
        <button id="defiScan">Scan accounts</button><button id="defiStop" disabled>Stop scan / check</button>
        <button id="defiStopExit" disabled>Stop withdrawal</button>
        <label>Protocol <select id="defiProtocol"><option value="">All protocols</option></select></label>
        <label>Gas +% <input id="defiGas" type="number" min="0" max="1000" step="0.01" value="20"></label>
        <label>Accounts <input id="defiAccounts" placeholder="1-100, 105"></label>
        <button id="defiSelectRange">Select batch</button><button id="defiSelectAll">Select visible</button>
        <button id="defiSelectNone">Clear</button><button id="defiBatch" disabled>Withdraw selected</button>
        <span id="defiSelected"></span><button id="defiInfoToggle">Info</button></div>
        <div id="defiInfo" hidden>Free Rabby API · DeBank estimates. Values are separate from wallet balances; loans are subtracted, ? means unknown price or an unscanned account.
        Automatic withdrawal supports ERC-4626 vaults, SynFutures V3 Gate deposits on Blast, verified Aave V3 markets, LFJ sJOE on Arbitrum, the verified Blackwing BSC launch vault and SyncSwap classic LP on zkSync Era. SyncSwap returns both tokens with 0.5% minimum-output protection; approval fees are included.
        Other staking, LP and queued exits require adapters. Withdrawals return underlying tokens to the same wallet.</div>
        <div id="defiStatus" role="status"></div><div id="defiExitStatus" role="status"></div>
        <div class="defi-workspace"><div class="defi-grid-wrap"><table class="defi-grid"><thead id="defiGridHead"></thead><tbody id="defiGridBody"></tbody></table></div>
        <aside class="sidebar defi-sidebar"><div class="sidebar-section"><h3>Protocols <span class="token-pick-hint">click to select / clear</span></h3>
        <button id="defiClearProtocols">Clear protocols</button><div class="token-list" id="defiProtocols"></div></div>
        <div class="sidebar-section"><h3>Chains Distribution</h3><div class="chain-stats" id="defiChains"></div></div>
        <div class="sidebar-section"><h3>Portfolio Summary</h3><div id="defiSummary"></div></div></aside></div>`;
    document.body.append(view);
    const modal = document.createElement('dialog'); modal.id = 'defiDetail'; modal.className = 'defi-panel';
    modal.innerHTML = `<div class="defi-heading"><strong id="defiDetailTitle"></strong><button id="defiClose">Close</button></div>
        <div id="defiDetailBlocked" role="status" aria-live="polite" hidden></div>
        <div id="defiDetailStatus" role="status"></div><div class="defi-table-wrap"><table><thead><tr>
        <th>Network</th><th>Protocol</th><th>Type</th><th>Asset</th><th>Amount</th><th>Est. USD</th><th>Withdrawal</th>
        </tr></thead><tbody id="defiRows"></tbody></table></div>`;
    document.body.append(modal);
    const preview = document.createElement('dialog'); preview.id = 'defiPreview'; preview.className = 'defi-panel';
    preview.innerHTML = `<div class="defi-heading"><strong id="defiPreviewTitle">Batch withdrawal preview</strong><button id="defiPreviewCancel">Cancel</button></div>
        <p id="defiPreviewSummary"></p><div id="defiPreviewStatus" role="status" aria-live="polite"></div><div class="defi-table-wrap" id="defiPreviewRows"></div>
        <p id="defiPreviewNote">Underlying tokens return to each wallet. Transactions run sequentially; an unknown transaction outcome stops the queue.</p>
        <button id="defiPreviewConfirm">Confirm withdrawal</button>`;
    document.body.append(preview);
    // Keep the existing log drawer available above either tab.
    const logs = el('logsPanel'); if (logs) document.body.append(logs);
    async function api(path, body) {
        const response = await fetch('/api/Treasury/defi/' + path, body === undefined ? {} : {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
        return data;
    }
    window.setDefiOpen = open => {
        view.hidden = !open; document.body.classList.toggle('defi-active', open);
        el('defiToggle').setAttribute('aria-selected', String(open));
        el('treasuryTab').setAttribute('aria-selected', String(!open));
        const name = document.querySelector('.page-name'); if (name) name.textContent = open ? 'DeFi' : 'Treasury';
        if (open) {
            el('defiMaxId').value = el('maxIdInput').value;
            el('defiGas').value = String(window.getGasBoostPercent()); poll();
        } else { clearTimeout(timer); modal.close(); if (!confirmationPending) preview.close(); }
    };
    for (const dialog of [modal, preview]) dialog.addEventListener('click', e => {
        if (e.target !== dialog) return;
        if (dialog === preview && confirmationPending) return;
        const r = dialog.getBoundingClientRect();
        if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) dialog.close();
    });
    preview.addEventListener('cancel', event => { if (confirmationPending) event.preventDefault(); });
    el('defiClose').onclick = () => modal.close();
    el('defiInfoToggle').onclick = () => { el('defiInfo').hidden = !el('defiInfo').hidden; };
    const filtered = () => (state?.positions || []).filter(p => protocolsSelected.size ? protocolsSelected.has(p.protocol) : !el('defiProtocol').value || p.protocol === el('defiProtocol').value);
    const net = rows => rows.reduce((sum, p) => sum + (p.valueUsd == null ? 0 : Number(p.valueUsd) * (p.type === 'loan' ? -1 : 1)), 0);
    function amountLabel(rows) {
        const known = rows.some(p => p.valueUsd != null), unknown = rows.some(p => p.valueUsd == null), value = net(rows);
        if (!known && unknown) return '?';
        const label = value !== 0 && Math.abs(value) < .01 ? (value < 0 ? '−<$0.01' : '<$0.01') : '$' + value.toFixed(2);
        return label + (unknown ? ' ?' : '');
    }
    function cell(row, value, tag = 'td') {
        const item = document.createElement(tag); item.textContent = String(value); row.append(item); return item;
    }
    const active = () => busy || state?.running || state?.preparing || state?.exitRunning;
    function withdrawalBlockReason() {
        if (state?.running) return `Withdrawal checks are paused while scanning: ${state.processed}/${state.total} accounts. Wait for the scan to finish or use Stop scan / check.`;
        if (state?.preparing) return `Withdrawal checks are paused while another check is running: ${state.prepareDone || 0}/${state.prepareTotal || 0}.`;
        if (state?.exitRunning) return 'Withdrawal checks are paused while a withdrawal is running. Wait for it to finish.';
        if (busy) return 'Withdrawal check or confirmation is in progress. Finish or cancel it first.';
        if (state?.needsRescan) return 'Positions may have changed after a transaction. Scan accounts again before checking another withdrawal.';
        return '';
    }
    function buttons() {
        el('defiScan').disabled = active(); el('defiStop').disabled = !state?.running && !state?.preparing;
        el('defiStopExit').disabled = !state?.exitRunning;
        el('defiBatch').disabled = active() || state?.needsRescan || !el('defiProtocol').value || !selected.size;
        el('defiBatch').title = protocolsSelected.size > 1 ? 'Choose one protocol for a batch withdrawal' : '';
        el('defiSelected').textContent = `${selected.size} selected`;
        const reason = withdrawalBlockReason();
        el('defiDetailBlocked').textContent = reason; el('defiDetailBlocked').hidden = !reason;
        el('defiRows').querySelectorAll('button').forEach(b => { b.disabled = !!reason; b.title = reason; });
    }
    function accounts() {
        const rows = filtered(), found = new Map();
        for (const p of rows) found.set(p.accountId, { id: p.accountId, address: p.wallet, status: 'scanned' });
        for (const a of state?.accounts || []) {
            if ((!protocolsSelected.size && !el('defiProtocol').value) || found.has(a.id) || a.status !== 'scanned') found.set(a.id, a);
        }
        return [...found.values()].sort((a, b) => a.id - b.id);
    }
    function heat(row, rows, account, chain) {
        const td = cell(row, ''), button = document.createElement('button');
        const value = net(rows);
        button.className = 'heatmap-cell defi-heat ' + (!rows.length || !rows.some(p => p.valueUsd != null) ? 'empty' : value < 0 ? 'negative' : getValueLevel(value));
        button.textContent = rows.length ? amountLabel(rows) : account.status === 'scanned' ? '—' : '?';
        button.title = `${rows.length} position assets${account.status !== 'scanned' ? ' · ' + account.status : ''}`;
        button.onclick = () => showDetails(account.id, chain); td.append(button);
    }
    function render() {
        if (!state) return;
        const protocol = el('defiProtocol'), chosen = protocol.value;
        const names = [...new Set(state.positions.map(p => p.protocol))].sort();
        for (const name of protocolsSelected) if (!names.includes(name)) protocolsSelected.delete(name);
        protocol.replaceChildren();
        for (const name of ['', ...names]) {
            const option = document.createElement('option'); option.value = name; option.textContent = name || 'All protocols'; protocol.append(option);
        }
        protocol.value = names.includes(chosen) ? chosen : '';
        const rows = filtered(), chains = [...new Set(rows.map(p => p.chain))].sort(), head = document.createElement('tr');
        cell(head, 'ID', 'th'); cell(head, 'Wallet', 'th');
        for (const chain of chains) cell(head, chain, 'th'); cell(head, 'Net est. USD', 'th');
        el('defiGridHead').replaceChildren(head); el('defiGridBody').replaceChildren();
        for (const account of accounts()) {
            const row = document.createElement('tr'), id = document.createElement('button');
            id.className = 'account-id-button' + (selected.has(account.id) ? ' selected' : ''); id.textContent = account.id;
            id.setAttribute('aria-label', `Select account ${account.id}`); id.setAttribute('aria-pressed', String(selected.has(account.id)));
            id.onclick = () => { selected.has(account.id) ? selected.delete(account.id) : selected.add(account.id); render(); };
            cell(row, '').append(id); const address = cell(row, account.address); address.className = 'defi-wallet';
            address.title = account.address; address.onclick = () => showDetails(account.id);
            const positions = rows.filter(p => p.accountId === account.id);
            for (const chain of chains) heat(row, positions.filter(p => p.chain === chain), account, chain);
            heat(row, positions, account); el('defiGridBody').append(row);
        }
        el('defiStatus').textContent = `${state.running ? 'Scanning' : state.cancelled ? 'Stopped' : 'Scan'}: ${state.processed}/${state.total} accounts · ${state.positions.length} position assets · ${state.errors.length} errors`;
        if (!state.total) el('defiStatus').textContent = 'Not scanned. Scan accounts to load free Rabby data.';
        if (state.updatedAt) el('defiStatus').textContent += ` · Updated: ${new Date(state.updatedAt).toLocaleString()}`;
        if (state.needsRescan) el('defiStatus').textContent += ' · Positions may have changed — scan again';
        if (state.preparing) el('defiStatus').textContent += ` · Checking withdrawals ${state.prepareDone}/${state.prepareTotal}`;
        el('defiExitStatus').textContent = message || (state.exitRunning ? `Withdrawing · account #${state.batchAccount || '…'} · ${state.batchResults?.length || 0}/${state.batchTotal || 1} · ${state.exitResult || ''}` : state.exitResult || '');
        renderSidebar();
        if (detail && modal.open) renderDetails(); buttons();
    }
    function renderSidebar() {
        const node = (tag, cls, text) => { const n = document.createElement(tag); n.className = cls; if (text !== undefined) n.textContent = text; return n; };
        const group = (rows, key) => {
            const groups = new Map();
            for (const p of rows) { if (!groups.has(p[key])) groups.set(p[key], []); groups.get(p[key]).push(p); }
            return [...groups].sort((a, b) => net(b[1]) - net(a[1]));
        };
        const all = state.positions, groups = group(all, 'protocol'), total = groups.reduce((sum, [, rows]) => sum + Math.max(0, net(rows)), 0);
        el('defiProtocols').replaceChildren();
        for (const [name, rows] of groups) {
            const chosen = protocolsSelected.has(name) || (!protocolsSelected.size && el('defiProtocol').value === name);
            const choice = node('button', 'token-choice' + (chosen ? ' selected' : '')); choice.type = 'button';
            choice.setAttribute('aria-pressed', String(chosen)); choice.title = name + ' · Share of known positive protocol net values; unknown prices are excluded';
            choice.onclick = () => {
                protocolsSelected.has(name) ? protocolsSelected.delete(name) : protocolsSelected.add(name);
                el('defiProtocol').value = protocolsSelected.size === 1 ? [...protocolsSelected][0] : '';
                detail = null; modal.close(); render();
            };
            const row = node('div', 'token-row'), info = node('div', 'token-info'), value = node('div', 'token-value');
            const percent = total > 0 ? Math.max(0, net(rows)) / total * 100 : 0;
            info.append(node('span', 'token-symbol', name), node('span', 'token-accounts', `${new Set(rows.map(p => p.accountId)).size} accounts`));
            value.append(node('span', 'token-usd', amountLabel(rows)), node('span', 'token-percent', `${percent.toFixed(1)}%`)); row.append(info, value);
            const bar = node('div', 'progress-bar'), fill = node('div', 'progress-fill'); fill.style.width = `${Math.min(100, percent)}%`;
            bar.append(fill); choice.append(row, bar); el('defiProtocols').append(choice);
        }
        el('defiClearProtocols').disabled = !protocolsSelected.size && !el('defiProtocol').value;
        el('defiChains').replaceChildren();
        for (const [name, rows] of group(filtered(), 'chain')) {
            const row = node('div', 'chain-stat-row');
            row.append(node('span', 'chain-stat-name', name.toUpperCase()), node('span', 'chain-stat-value', amountLabel(rows))); el('defiChains').append(row);
        }
        const rows = filtered();
        el('defiSummary').textContent = `Net est. USD: ${rows.length ? amountLabel(rows) : '$0.00'} · ${new Set(rows.map(p => p.protocol)).size} protocols · ${new Set(rows.map(p => p.chain)).size} chains · ${new Set(rows.map(p => p.accountId)).size} accounts`;
    }
    function showDetails(accountId, chain) {
        message = ''; detail = { accountId, chain }; renderDetails(); if (!modal.open) modal.showModal();
    }
    function renderDetails() {
        el('defiDetailTitle').textContent = `Account #${detail.accountId}${detail.chain ? ' · ' + detail.chain : ''}${el('defiProtocol').value ? ' · ' + el('defiProtocol').value : ''}`;
        el('defiDetailStatus').textContent = message || state.exitResult || '';
        el('defiRows').replaceChildren();
        for (const p of filtered().filter(p => p.accountId === detail.accountId && (!detail.chain || p.chain === detail.chain))) {
            const row = document.createElement('tr');
            for (const value of [p.chain, p.protocol, p.type, p.symbol, p.amount, p.valueUsd == null ? '?' : money(p.valueUsd)]) cell(row, value);
            row.children[5].title = p.priceSource ? `Estimated value · ${p.priceSource}` : p.valueUsd == null ? 'Price unavailable; not a zero balance' : 'Provider estimate';
            row.title = `Wallet: ${p.wallet}\nContract: ${p.vaultAddress || 'unknown'}\nGroup: ${p.groupId}`;
            const action = cell(row, '');
            if (!p.withdrawalReason && ['deposit', 'staked', 'locked', 'reward'].includes(p.type) && /^0x[\da-f]{40}$/i.test(p.vaultAddress || '') && /^0x[\da-f]{40}$/i.test(p.assetAddress || '')) {
                const button = document.createElement('button'); button.textContent = p.type === 'reward' ? p.protocol === 'LayerBank' ? 'Check unlocked rewards' : 'Check claim' : 'Check withdrawal'; button.title = withdrawalBlockReason(); button.disabled = !!button.title;
                button.onclick = () => withdraw(p); action.append(button);
            } else {
                action.textContent = 'Unavailable · details'; action.title = p.withdrawalReason || 'No automatic adapter for this position';
                action.tabIndex = 0; action.setAttribute('role', 'button'); action.style.cursor = 'help';
                action.onclick = () => { message = action.title; el('defiDetailStatus').textContent = message; };
                action.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); action.onclick(); } };
            }
            el('defiRows').append(row);
        }
    }
    async function poll() {
        clearTimeout(timer);
        try { state = await api('status'); render(); } catch (e) { el('defiStatus').textContent = e.message; }
        if (!view.hidden) timer = setTimeout(poll, 2000);
    }
    function gas() {
        const value = Number(el('defiGas').value);
        if (el('defiGas').value === '' || !Number.isFinite(value) || value < 0 || value > 1000 || Math.abs(value * 100 - Math.round(value * 100)) > 1e-7) throw new Error('Gas percentage must be 0–1000, with at most two decimals.');
        return value;
    }
    function parseBatch(text) {
        const ids = new Set();
        for (const part of text.split(',')) {
            const match = /^\s*(\d+)(?:\s*-\s*(\d+))?\s*$/.exec(part);
            if (!match) throw new Error('Use account IDs or ranges, for example 1-100, 105.');
            const start = Number(match[1]), end = Number(match[2] || match[1]);
            if (start < 1 || end > 10000 || end < start) throw new Error('Account IDs must be between 1 and 10000.');
            for (let id = start; id <= end; id++) ids.add(id);
        }
        return [...ids];
    }
    el('defiProtocol').onchange = () => { protocolsSelected.clear(); if (el('defiProtocol').value) protocolsSelected.add(el('defiProtocol').value); detail = null; modal.close(); render(); };
    el('defiClearProtocols').onclick = () => { protocolsSelected.clear(); el('defiProtocol').value = ''; detail = null; modal.close(); render(); };
    el('defiSelectRange').onclick = () => {
        try { const ids = parseBatch(el('defiAccounts').value); selected.clear(); ids.forEach(id => selected.add(id)); message = ''; render(); }
        catch (e) { message = e.message; el('defiExitStatus').textContent = message; }
    };
    el('defiSelectAll').onclick = () => { accounts().forEach(a => selected.add(a.id)); render(); };
    el('defiSelectNone').onclick = () => { selected.clear(); render(); };
    el('defiScan').onclick = async () => {
        busy = true; message = ''; buttons();
        try { await api('scan', { maxId: Number(el('defiMaxId').value) }); await poll(); }
        catch (e) { message = e.message; }
        finally { busy = false; if (state) render(); }
    };
    el('defiStop').onclick = async () => { try { await api('stop', {}); await poll(); } catch (e) { message = e.message; render(); } };
    el('defiStopExit').onclick = async () => {
        try {
            const response = await fetch('/api/Treasury/swaps/stop', { method: 'POST' });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            message = 'Stopping. Any broadcast transaction continues on-chain.';
        } catch (e) { message = e.message; }
        render();
    };
    const quoteAmount = q => {
        if (q.outputs?.length) return q.outputs.map(o => quoteAmount({ ...o, outputs: null })).join(' + ');
        const digits = q.amountRaw.padStart(q.decimals + 1, '0');
        return (q.decimals ? digits.slice(0, -q.decimals) + '.' + digits.slice(-q.decimals) : digits) + ' ' + q.symbol;
    };
    async function withdraw(position) {
        busy = true; message = 'Checking withdrawal and fee…'; render();
        try {
            const plan = await api('withdraw/preview', { accountId: position.accountId, positionId: position.id, gasBoostPercent: gas() });
            const q = plan.quote;
            if (await confirmBatch({ targets: [{ position, quote: q }], skipped: [], accounts: 1,
                totalUsd: q.valueUsd, feeUsd: q.feeUsd, gasBoostPercent: gas() }, true, () => api('withdraw/execute', plan.planId))) {
                message = ''; await poll();
            } else message = 'Withdrawal cancelled before signing.';
        } catch (e) { message = e.message; }
        finally { busy = false; render(); }
    }
    const money = value => '$' + Number(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 8 });
    function confirmBatch(plan, single = false, execute) {
        preview.className = single ? 'defi-panel defi-confirm' : 'defi-panel';
        el('defiPreviewTitle').textContent = single ? 'Confirm withdrawal' : 'Batch withdrawal preview';
        el('defiPreviewNote').textContent = single ? 'Underlying tokens return to the wallet shown above.' :
            'Underlying tokens return to each wallet. Transactions run sequentially; an unknown transaction outcome stops the queue.';
        el('defiPreviewSummary').textContent = `${plan.accounts} accounts · ${plan.targets.length} withdrawals · output $${Number(plan.totalUsd).toFixed(4)} · fees $${Number(plan.feeUsd).toFixed(4)} · gas +${plan.gasBoostPercent}% · ${plan.skipped.length} skipped`;
        el('defiPreviewRows').replaceChildren();
        if (single) {
            const { position: p, quote: q } = plan.targets[0];
            if (p.type === 'reward') {
                el('defiPreviewTitle').textContent = p.protocol === 'LayerBank' ? 'Confirm unlocked reward withdrawal' : 'Confirm reward claim';
                el('defiPreviewNote').textContent = p.protocol === 'LayerBank' ? 'Only unlocked LAB.s returns to this wallet. Unclaimed rewards are not locked; no early-exit penalty is accepted.' : 'Rewards return to this wallet. The JOE stake is retained.';
            }
            el('defiPreviewSummary').textContent = `Account #${p.accountId} · ${p.chain} · ${p.protocol}`;
            for (const [label, value] of [['Wallet', p.wallet], ['Receive', quoteAmount(q)], ['Output value', money(q.valueUsd)],
                ['Total network fee', money(q.feeUsd)], ...(q.l1FeeUsd ? [[p.chain === 'op' ? 'L1 / operator reserve' : 'Included L1 fee reserve', money(q.l1FeeUsd)]] : []), ['Gas boost', `+${plan.gasBoostPercent}%`]]) {
                const row = document.createElement('div'); row.className = 'defi-quote-field';
                const name = document.createElement('span'), amount = document.createElement('strong');
                name.textContent = label; amount.textContent = value; row.append(name, amount); el('defiPreviewRows').append(row);
            }
            if (q.approval || q.outputs?.length) {
                const note = document.createElement('p');
                note.textContent = (q.approval ? 'LP approval required: approval itself spends gas. Total fee includes approval and withdrawal gas reserve. The withdrawal is simulated again after approval. ' : '') +
                    (q.outputs?.length ? 'Shown token amounts are protected minimum outputs.' : '');
                el('defiPreviewRows').append(note);
            }
        }
        for (const target of single ? [] : plan.targets) {
            const line = document.createElement('div');
            line.textContent = `#${target.position.accountId} · ${target.position.chain} · ${target.position.protocol} · ${quoteAmount(target.quote)} · fee $${Number(target.quote.feeUsd).toFixed(4)}${target.quote.approval ? ' · LP approval + withdrawal gas reserve; approval spends gas, followed by withdrawal simulation' : ''}${target.quote.outputs?.length ? ' · protected minimum outputs' : ''}`;
            el('defiPreviewRows').append(line);
        }
        for (const skipped of plan.skipped) {
            const line = document.createElement('div'); line.className = 'defi-skipped';
            line.textContent = `#${skipped.accountId} · ${skipped.chain} · Skipped: ${skipped.reason}`; el('defiPreviewRows').append(line);
        }
        el('defiPreviewConfirm').disabled = !plan.targets.length;
        el('defiPreviewCancel').disabled = false; el('defiPreviewStatus').textContent = '';
        return new Promise(resolve => {
            let accepted = false;
            preview.addEventListener('close', () => resolve(accepted), { once: true });
            el('defiPreviewCancel').onclick = () => preview.close();
            el('defiPreviewConfirm').onclick = async () => {
                if (!plan.targets.length || confirmationPending) return;
                confirmationPending = true;
                el('defiPreviewConfirm').disabled = true; el('defiPreviewCancel').disabled = true;
                el('defiPreviewStatus').textContent = 'Starting withdrawal…';
                try { await execute(); accepted = true; preview.close(); }
                catch (error) { el('defiPreviewStatus').textContent = `Could not start withdrawal: ${error.message}`; }
                finally {
                    confirmationPending = false;
                    el('defiPreviewConfirm').disabled = false; el('defiPreviewCancel').disabled = false;
                }
            };
            preview.showModal();
        });
    }
    el('defiBatch').onclick = async () => {
        busy = true; message = 'Checking selected accounts…'; buttons();
        try {
            const plan = await api('batch/preview', { protocol: el('defiProtocol').value, accountIds: [...selected], gasBoostPercent: gas() });
            if (await confirmBatch(plan, false, () => api('batch/execute', plan.planId))) { message = ''; await poll(); }
            else message = 'Batch cancelled before signing.';
        } catch (e) { message = e.message; }
        finally { busy = false; render(); }
    };
})();
