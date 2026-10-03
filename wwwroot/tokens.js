const selectedTokenSymbols = new Set();
const selectedTreasuryAccounts = new Set();
const selectedTreasuryChains = new Set();
const excludedTreasuryAccounts = new Set();
let tokenSwapPolling = false;
let tokenSwapPending = false;
const tokenKey = token => `${token.chainId}:${(token.address || '').toLowerCase()}`;
const nativeToken = token => /^0x(?:0{40}|e{40})$/i.test(token.address || '');
const tokenSymbol = token => (token.symbol || '').trim();

function selectedTokenEntries() {
    const chains = new Set(getTreasurySwapChains());
    return treasuryData.filter(a => selectedTreasuryAccounts.has(a.id)).flatMap(account => Object.entries(account.chainData || {}).flatMap(([chain, tokens]) =>
        chains.has(chain) ? tokens.filter(token => !selectedTokenSymbols.size || selectedTokenSymbols.has(tokenSymbol(token))).map(token => ({ account, chain, token })) : []));
}

function getTreasurySwapChains() {
    if (selectedTreasuryChains.size) return [...selectedTreasuryChains];
    const filtered = getSelectedChains();
    return filtered.length ? filtered : [...new Set(treasuryData.flatMap(a => Object.keys(a.chainData || {})))];
}
function toggleTreasuryChain(chain) {
    selectedTreasuryChains.has(chain) ? selectedTreasuryChains.delete(chain) : selectedTreasuryChains.add(chain);
    highlightSelectedToken();
}
function clearTreasuryChains() { selectedTreasuryChains.clear(); highlightSelectedToken(); }

function updateTokenChoices(data) {
    const symbols = [...new Set(data.flatMap(a => Object.values(a.chainData || {}).flat().map(tokenSymbol)))].filter(Boolean).sort();
    for (const symbol of selectedTokenSymbols) if (!symbols.includes(symbol)) selectedTokenSymbols.delete(symbol);
    const accounts = new Set(data.map(a => a.id));
    for (const id of selectedTreasuryAccounts) if (!accounts.has(id)) selectedTreasuryAccounts.delete(id);
    for (const id of excludedTreasuryAccounts) if (!accounts.has(id)) excludedTreasuryAccounts.delete(id);
}

function selectTreasuryToken(symbol) {
    if (!selectedTokenSymbols.size) excludedTreasuryAccounts.clear();
    selectedTokenSymbols.has(symbol) ? selectedTokenSymbols.delete(symbol) : selectedTokenSymbols.add(symbol);
    selectedTreasuryAccounts.clear();
    for (const account of treasuryData) {
        if (!excludedTreasuryAccounts.has(account.id) && Object.values(account.chainData || {}).flat()
            .some(token => selectedTokenSymbols.has(tokenSymbol(token)))) selectedTreasuryAccounts.add(account.id);
    }
    if (!selectedTokenSymbols.size) excludedTreasuryAccounts.clear();
    highlightSelectedToken();
}
function clearTreasuryTokens() { selectedTokenSymbols.clear(); selectedTreasuryAccounts.clear(); excludedTreasuryAccounts.clear(); highlightSelectedToken(); }
function toggleTreasuryAccount(id) {
    if (selectedTreasuryAccounts.has(id)) { selectedTreasuryAccounts.delete(id); excludedTreasuryAccounts.add(id); }
    else { selectedTreasuryAccounts.add(id); excludedTreasuryAccounts.delete(id); }
    highlightSelectedToken();
}
function clearTreasuryAccounts() {
    selectedTreasuryAccounts.clear();
    for (const account of treasuryData) excludedTreasuryAccounts.add(account.id);
    highlightSelectedToken();
}

function highlightSelectedToken() {
    document.querySelectorAll('.treasury-chain-choice').forEach(button => {
        const chosen = selectedTreasuryChains.has(decodeURIComponent(button.dataset.chain));
        button.classList.toggle('selected', chosen); button.setAttribute('aria-pressed', String(chosen));
    });
    document.querySelectorAll('.heatmap-cell[data-tokens]').forEach(cell => {
        cell.classList.toggle('swap-chain-selected', selectedTreasuryChains.has(cell.dataset.chain));
        const matches = selectedTokenSymbols.size &&
            JSON.parse(cell.dataset.tokens || '[]').some(t => selectedTokenSymbols.has(tokenSymbol(t)));
        cell.classList.toggle('token-match', Boolean(matches));
        cell.classList.toggle('token-muted', Boolean(selectedTokenSymbols.size && !matches));
    });
    document.querySelectorAll('.token-choice[data-symbol]').forEach(row => {
        const chosen = selectedTokenSymbols.has(row.dataset.symbol);
        row.classList.toggle('selected', chosen); row.setAttribute('aria-pressed', String(chosen));
    });
    document.querySelectorAll('.treasury-account-id').forEach(button => {
        const chosen = selectedTreasuryAccounts.has(Number(button.dataset.accountId));
        button.classList.toggle('selected', chosen); button.setAttribute('aria-pressed', String(chosen));
    });
    document.getElementById('treasuryAccountSelectionInfo').textContent = `${selectedTreasuryAccounts.size} accounts selected`;
    document.getElementById('clearTreasuryAccountsButton').disabled = !selectedTreasuryAccounts.size;
    const entries = selectedTokenEntries();
    const wallets = new Set(entries.map(e => e.account.id));
    document.getElementById('tokenSelectionInfo').textContent = `${selectedTokenSymbols.size ? [...selectedTokenSymbols].join(', ') : 'All tokens'} · ${wallets.size} accounts · ${entries.length} positions · ${formatUSD(entries.reduce((sum, e) => sum + e.token.valueUSD, 0))}`;
    const scope = document.getElementById('treasuryChainSelectionInfo');
    if (scope) scope.textContent = `Swap networks: ${selectedTreasuryChains.size ? [...selectedTreasuryChains].join(', ') : 'all visible'} · Accounts: ${selectedTreasuryAccounts.size ? [...selectedTreasuryAccounts].map(id => '#' + id).join(', ') : 'select account IDs'}`;
    const clearChains = document.getElementById('clearTreasuryChainsButton'); if (clearChains) clearChains.disabled = !selectedTreasuryChains.size;
    document.getElementById('swapSelectedTokenButton').textContent = selectedTokenSymbols.size ? 'Swap selected tokens → native' : 'Swap all tokens → native';
    document.getElementById('clearTokenSelectionButton').disabled = !selectedTokenSymbols.size;
    document.getElementById('swapSelectedTokenButton').disabled = tokenSwapPending || tokenSwapPolling || !selectedTreasuryAccounts.size || !entries.some(e => !nativeToken(e.token));
}

async function tokenSwapRequest(path, body) {
    const response = await fetch(`${API_BASE}/token-swap/${path}`, {
        method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store'
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
    return data;
}

async function swapSelectedToken() {
    if (tokenSwapPending || tokenSwapPolling) return;
    if (!selectedTreasuryAccounts.size) return;
    tokenSwapPending = true;
    const button = document.getElementById('swapSelectedTokenButton');
    button.disabled = true;
    try {
        const symbols = [...selectedTokenSymbols];
        const excludeStables = !symbols.length && Boolean(document.getElementById('excludeStablesCheckbox')?.checked);
        const assets = [...new Map(selectedTokenEntries().filter(e => !nativeToken(e.token))
            .map(e => [tokenKey(e.token), { chainId: e.token.chainId, address: e.token.address }])).values()];
        const plan = await tokenSwapRequest('preview', {
            maxId: Number(document.getElementById('maxIdInput').value), chains: getTreasurySwapChains(), assets,
            accountIds: [...selectedTreasuryAccounts],
            excludeStables,
            threshold: Number(document.getElementById('thresholdInput').value),
            protocol: document.getElementById('protocolSelect').value,
            gasBoostPercent: getGasBoostPercent()
        });
        if (!plan.targets.length) {
            await Swal.fire({ icon: 'info', title: 'No tokens above Min. USD', text: 'Native tokens are already native and are skipped.' });
            return;
        }
        const content = document.createElement('div');
        const details = document.createElement('div');
        details.className = 'token-swap-preview';
        plan.targets.forEach(target => {
            const row = document.createElement('div');
            row.textContent = `#${target.id} · ${target.chain} · ${target.symbol} · ${formatUSD(target.valueUSD)} · ${target.address}`;
            details.append(row);
        });
        const note = document.createElement('p');
        note.textContent = `${plan.accounts} accounts · ${plan.targets.length} positions · estimated ${formatUSD(plan.totalUSD)}. ` +
            `Swap the full live balance to the native token in each network. Gas +${plan.gasBoostPercent}%; slippage up to 3%. ` +
            'Only the accounts, networks and contracts listed below will be swapped. ' +
            (symbols.length ? 'Exclude Stables does not apply to this explicit token selection.' : excludeStables ? 'Stablecoins are excluded.' : 'Stablecoins are included.');
        content.append(note, details);
        const confirmation = await Swal.fire({ title: `Swap ${symbols.length ? symbols.join(', ') : 'all tokens'} → native`, html: content, width: 850,
            showCancelButton: true, confirmButtonText: 'Start swap', cancelButtonText: 'Cancel' });
        if (!confirmation.isConfirmed) return;
        const job = await tokenSwapRequest('execute', plan.planId);
        await pollTokenSwap(job.jobId);
    } catch (error) {
        await Swal.fire({ icon: 'error', title: 'Token swap', text: error.message });
    } finally { tokenSwapPending = false; highlightSelectedToken(); }
}

async function pollTokenSwap(jobId) {
    tokenSwapPolling = true;
    highlightSelectedToken();
    const statusLabel = document.getElementById('tokenSwapStatus');
    try {
        while (true) {
            const status = await tokenSwapRequest('status');
            if (status.jobId !== jobId) throw new Error('Swap status belongs to another job');
            statusLabel.textContent = `${status.results.length}/${status.total} accounts` +
                (status.currentId ? ` · #${status.currentId}` : '');
            if (!status.running) {
                const success = status.results.reduce((sum, r) => sum + r.succeeded, 0);
                const failed = status.results.reduce((sum, r) => sum + r.failed, 0);
                const skipped = status.results.reduce((sum, r) => sum + r.skipped, 0);
                const errors = status.results.filter(r => r.error || r.refreshError)
                    .map(r => `#${r.id}: ${r.error || `Balance refresh failed: ${r.refreshError}`}`);
                const skipReasons = status.results.flatMap(r => (r.skipReasons || []).map(s =>
                    `#${r.id} · ${s.chain} · ${s.symbol}: ${s.reason}`));
                statusLabel.textContent = `${status.cancelled ? 'Stopped' : 'Done'}: ${success} swapped · ${failed} failed · ${skipped} skipped`;
                await refreshData();
                await Swal.fire({ icon: failed || errors.length || status.cancelled || skipped ? 'warning' : 'success',
                    title: status.cancelled ? 'Token swap stopped' : success === 0 ? 'No tokens swapped' : 'Token swap finished',
                    text: `${statusLabel.textContent}${errors.length ? '\n' + errors.join('\n') : ''}${skipReasons.length ? '\n' + skipReasons.join('\n') : ''}` });
                break;
            }
            await new Promise(resolve => setTimeout(resolve, 2000));
        }
    } catch (error) {
        statusLabel.textContent = 'Status unavailable; the swap may still be running. Reload to reconnect.';
        throw error;
    } finally { tokenSwapPolling = false; highlightSelectedToken(); }
}

document.addEventListener('DOMContentLoaded', async () => {
    try {
        const status = await tokenSwapRequest('status');
        if (status.running) await pollTokenSwap(status.jobId);
    } catch (error) { console.error('Token swap status:', error.message); }
});
