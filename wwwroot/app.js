const API_BASE = window.location.origin + '/api/treasury';

let treasuryData = [];
let autoRefresh = false;
let autoRefreshInterval = null;
let selectedChain = '';
let dbConfigured = false;
let savedDbConfig = null;

function fillSavedDbConfig() {
    if (!savedDbConfig?.type) return;
    const fields = { 'swal-db-type': 'type', 'swal-sqlite-path': 'sqlitePath', 'swal-host': 'host',
        'swal-port': 'port', 'swal-db': 'database', 'swal-user': 'user' };
    for (const [id, key] of Object.entries(fields)) {
        const field = document.getElementById(id);
        if (field && savedDbConfig[key] != null) field.value = savedDbConfig[key];
    }
    document.getElementById('swal-db-type').dispatchEvent(new Event('change', { bubbles: true }));
    if (savedDbConfig.passwordSaved) document.getElementById('swal-pass').placeholder = 'Saved password (leave blank to keep)';
}

async function submitDbConfig(config) {
    try {
        config = { ...config, useSavedPassword: config.type === 'postgres' && !config.password && Boolean(savedDbConfig?.passwordSaved) };
        const response = await fetch(`${API_BASE}/db-config`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(config)
        });
        const result = await response.json();
        if (!response.ok || !result.success) throw new Error(result.error || 'Не удалось подключиться к БД.');
        dbConfigured = true;
        savedDbConfig = { ...config, password: undefined, passwordSaved: config.type === 'postgres' && Boolean(config.password || config.useSavedPassword) };
        return config;
    } catch (error) {
        Swal.showValidationMessage(error.message);
        return false;
    }
}



async function checkDbAndInit() {
    try {
        const response = await fetch(`${API_BASE}/db-status`);
        const status = await response.json();

        dbConfigured = status.connected;
        savedDbConfig = status.config || null;

        if (!dbConfigured) {
            await showDbConfigDialog(status.error);
            // УДАЛИЛ initDashboard(), так как её нет в коде. 
            // showDbConfigDialog и так в конце вызывает testAPI()
        } else {
            testAPI(); // Это верный вызов, он у тебя есть в коде
        }
    } catch (error) {
        console.error('Failed to check database status:', error);
    }
}

async function showDbConfigDialog(startupError) {

    let serverPath = "./";
    try {
        const infoRes = await fetch(`${API_BASE}/info`);
        const info = await infoRes.json();
        serverPath = info.baseDirectory;
    } catch (e) { console.error("Could not fetch server info"); }

    const { value: formValues } = await Swal.fire({
        title: '⚠️ Database Not Configured',
        html: `
            <p style="margin-bottom: 20px;">Please configure database</p>
            
            <select id="swal-db-type" class="swal2-select">
                <option value="">Select database type</option>
                <option value="sqlite">SQLite</option>
                <option value="postgres">PostgreSQL</option>
            </select>
            
            <div id="sqlite-fields" style="text-align: left; margin-top: 15px;">
                <div style="font-size: 0.8em; color: #8b949e; margin-bottom: 8px; background: #0d1117; padding: 10px; border-radius: 6px; border: 1px solid #30363d;">
                    <strong>Root path:</strong><br>
                    <code style="color: #58a6ff; word-break: break-all;">${serverPath}</code>
                </div>
                <input id="swal-sqlite-path" class="swal2-input" 
                       placeholder="database.db" value="database.db" style="margin-top: 5px;">
                <p style="font-size: 0.75em; color: #8b949e; margin-top: 5px;">
                    * Если указать только имя, файл создастся в папке Root.
                </p>
            </div>
            
            <div id="postgres-fields" style="display: none;">
                <input id="swal-host" class="swal2-input" 
                       placeholder="Host" value="localhost">
                <input id="swal-port" class="swal2-input" 
                       placeholder="Port" value="5432">
                <input id="swal-db" class="swal2-input" 
                       placeholder="Database" value="postgres">
                <input id="swal-user" class="swal2-input" 
                       placeholder="Username" value="postgres">
                <input id="swal-pass" class="swal2-input" 
                       placeholder="Password" type="password">
            </div>
        `,
        background: '#161b22',
        color: '#c9d1d9',
        allowEscapeKey: false,
        allowOutsideClick: false,
        confirmButtonText: 'Connect',

        didOpen: () => {
            const dbTypeSelect = document.getElementById('swal-db-type');
            const sqliteFields = document.getElementById('sqlite-fields');
            const postgresFields = document.getElementById('postgres-fields');

            dbTypeSelect.addEventListener('change', (e) => {
                sqliteFields.style.display = 'none';
                postgresFields.style.display = 'none';

                if (e.target.value === 'sqlite') {
                    sqliteFields.style.display = 'block';
                } else if (e.target.value === 'postgres') {
                    postgresFields.style.display = 'block';
                }
            });
            fillSavedDbConfig();
            if (startupError) Swal.showValidationMessage(startupError);
        },

        showLoaderOnConfirm: true,
        preConfirm: async () => {
            const dbType = document.getElementById('swal-db-type').value;
            if (!dbType) {
                Swal.showValidationMessage('Select database type');
                return false;
            }

            if (dbType === 'sqlite') {
                return await submitDbConfig({
                    type: 'sqlite',
                    sqlitePath: document.getElementById('swal-sqlite-path').value
                });
            } else {
                return await submitDbConfig({
                    type: 'postgres',
                    host: document.getElementById('swal-host').value,
                    port: document.getElementById('swal-port').value,
                    database: document.getElementById('swal-db').value,
                    user: document.getElementById('swal-user').value,
                    password: document.getElementById('swal-pass').value
                });
            }
        }
    });

    if (!formValues) return;

    try {
        dbConfigured = true;
        Swal.fire({
            icon: 'success',
            title: 'Connected!',
            background: '#161b22',
            color: '#c9d1d9',
            timer: 1500,
            showConfirmButton: false
        });

        testAPI();
    } catch (error) {
        Swal.fire({
            icon: 'error',
            title: 'Error',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
    }
}


async function testAPI() {
    try {
        console.log('Testing API connection...');
        const response = await fetch(`${API_BASE}/test`);
        const result = await response.json();
        console.log('API test result:', result);

        if (result.status === 'OK') {
            console.log('API is working, loading data...');
            refreshData();
        }
    } catch (error) {
        console.error('API test failed:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Connection Failed',
            text: 'Cannot connect to API. Make sure the server is running.',
            background: '#161b22',
            color: '#c9d1d9'
        });
    }
}

async function refreshData() {
    showLoading(true);
    const maxId = document.getElementById('maxIdInput').value;

    try {
        console.log('Fetching data from:', `${API_BASE}/data?maxId=${maxId}`);
        const chains = encodeURIComponent(getSelectedChains().join(','));
        const response = await fetch(`${API_BASE}/data?maxId=${maxId}&chains=${chains}`, { cache: 'no-store' });

        console.log('Response status:', response.status);

        if (response.status === 503) {
            const error = await response.json();
            if (error.needsConfiguration) {
                dbConfigured = false;
                showLoading(false);
                await showDbConfigDialog();
                return;
            }
        }

        if (!response.ok) {
            const errorText = await response.text();
            console.error('API error response:', errorText);
            throw new Error(`API returned ${response.status}: ${errorText}`);
        }

        const data = await response.json();
        console.log('Received data:', data.length, 'accounts');
        console.log('Sample account:', data[0]);

        treasuryData = data;

        await updateStats();
        await updateChainFilter();
        renderHeatmap(treasuryData);

        console.log('✅ Data loaded successfully');
    } catch (error) {
        console.error('❌ Failed to load data:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Failed to Load Data',
            html: `<p>${error.message}</p><p style="font-size: 0.9em; color: #8b949e; margin-top: 10px;">Check console (F12) for details.</p>`,
            background: '#161b22',
            color: '#c9d1d9'
        });
    } finally {
        showLoading(false);
    }
}

async function updateStats(data = treasuryData) {
    // Statistics and cells must use the same snapshot, including chain filters.
    document.getElementById('totalAccounts').textContent = data.length;
    document.getElementById('activeAccounts').textContent = data.filter(a => Object.keys(a.chainData || {}).length > 0).length;
    document.getElementById('totalChains').textContent = getAllChains(data).length;
    document.getElementById('totalValue').textContent = formatUSD(calculateTotalValue(data));
}

async function updateChainFilter() {
    try {
        const response = await fetch(`${API_BASE}/chains?_t=${Date.now()}`);
        const chains = await response.json();

        if (!response.ok) throw new Error(`Chains returned ${response.status}`);
        const selected = getSelectedChains();
        const destination = document.getElementById('destinationChainSelect').value;
        updateChainCheckboxes(chains);
        document.querySelectorAll('.chain-checkbox').forEach(cb => { cb.checked = selected.includes(cb.value); });
        if (chains.includes(destination)) document.getElementById('destinationChainSelect').value = destination;
    } catch (error) {
        console.error('Failed to update chain filter:', error);
    }
}

function filterByChain() {
    selectedChain = document.getElementById('chainFilter').value;
    renderHeatmap(treasuryData);
}

function getAllChains(data) {
    const chains = new Set();
    data.forEach(account => {
        Object.keys(account.chainData || {}).forEach(chain => chains.add(chain));
    });
    return Array.from(chains).sort();
}

function calculateChainTotal(tokens) {
    return tokens.reduce((sum, token) => sum + (token.valueUSD || 0), 0);
}

function calculateTotalValue(data) {
    let total = 0;
    data.forEach(account => {
        Object.values(account.chainData || {}).forEach(tokens => {
            total += calculateChainTotal(tokens);
        });
    });
    return total;
}

function formatUSD(value) {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: 'USD',
        minimumFractionDigits: 2,
        maximumFractionDigits: 4
    }).format(value);
}

function formatAmount(amountRaw, decimals) {
    // Проверяем, что оба значения существуют и являются числами
    if (amountRaw === undefined || decimals === undefined) {
        return "0.000000";
    }

    const amount = parseFloat(amountRaw) / Math.pow(10, decimals);

    // Проверка на случай, если результат деления все равно NaN
    return isNaN(amount) ? "0.000000" : amount.toFixed(6);
}

function formatCompactUSD(value) {
    if (value === null || value === undefined || isNaN(value)) return '$0';
    if (value >= 1000) return (value / 1000).toFixed(1) + 'k';
    if (value >= 100) return value.toFixed(0);
    if (value >= 10) return value.toFixed(1);
    if (value >= 1) return value.toFixed(2);
    return value.toFixed(3);
}

function getValueLevel(value, maxValue) {
    if (value === 0) return 'empty';

    // Абсолютные пороги в USD
    if (value >= 100) return 'level-4';  // $100+
    if (value >= 10) return 'level-3';   // $10-100
    if (value >= 1) return 'level-2';    // $1-10
    return 'level-1';                    // $0-1
}

// Добавить эту функцию в начало файла или перед функциями swap/bridge
function getSettings() {
    return {
        protocol: document.getElementById('protocolSelect').value,
        gasBoostPercent: getGasBoostPercent(),
        threshold: parseFloat(document.getElementById('thresholdInput').value) || 0.1,
        excludeStables: document.getElementById('excludeStablesCheckbox').checked
    };
}


async function swapAllToNative(accountId, button) {
    const selectedChains = getTreasurySwapChains();

    const result = await Swal.fire({
        title: 'Confirm Swap',
        text: `Account #${accountId} · Networks: ${selectedChains.join(', ')}. Swap all eligible tokens to the native token in these networks?`,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Yes, swap it!',
        background: '#161b22',
        color: '#c9d1d9',
        confirmButtonColor: '#3085d6',
        cancelButtonColor: '#d33'
    });

    if (!result.isConfirmed) return;

    const settings = getSettings();
    const chainsParam = selectedChains.length > 0 ? selectedChains.join(',') : '';
    button.disabled = true;
    button.textContent = '⏳';

    try {
        console.log(`🚀 Starting swap-all for account ${accountId}`);

        const response = await fetch(`${API_BASE}/swap-chains`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                id: accountId,
                chains: selectedChains,
                protocol: settings.protocol,
                gasBoostPercent: settings.gasBoostPercent,
                destination: null,
                threshold: settings.threshold,
                excludeStables: settings.excludeStables
            })
        });


        //const response = await fetch(`${API_BASE}/swap-chains?id=${accountId}&chains=${chainsParam}`, { method: 'POST' });

        if (!response.ok)
        {
            const error = response.status === 204 ? {} : await response.json();
            throw new Error(error.error || `HTTP ${response.status}`);
        }
        console.log('swap-all started successfully');
        Swal.fire({
            icon: 'success',
            title: '✅ Swap Started',
            text: 'Swap operation initiated successfully',
            background: '#161b22',
            color: '#c9d1d9',
            timer: 2000,
            showConfirmButton: false
        });
    }
    catch (error)
    {
        console.error('❌ Swap failed:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Swap Failed',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
        button.disabled = false;
        button.textContent = ' ';
    }
}
async function bridgeToChain(accountId) {
    const destination = document.getElementById('destinationChainSelect').value;
    const settings = getSettings();
    if (!destination) {
        Swal.fire({
            icon: 'warning',
            title: '⚠️ Select Chain',
            text: 'Please select destination chain',
            background: '#161b22',
            color: '#c9d1d9'
        });
        return;
    }

    const result = await Swal.fire({
        title: 'Confirm Bridge',
        text: `Bridge all native tokens from account #${accountId} to ${destination}?`,
        icon: 'question',
        showCancelButton: true,
        confirmButtonText: 'Let\'s go!',
        background: '#161b22',
        color: '#c9d1d9'
    });

    if (!result.isConfirmed) return;

    const selectedChains = getSelectedChains();
    const chainsParam = selectedChains.length > 0 ? selectedChains.join(',') : '';
    const button = event.target;
    button.disabled = true;
    button.textContent = '⏳';

    try {
        console.log(`🌉 Starting bridge for account ${accountId} to ${destination}`);
        const response = await fetch(`${API_BASE}/bridge-chains`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                id: accountId,
                chains: selectedChains,
                destination: destination,
                protocol: settings.protocol,
                gasBoostPercent: settings.gasBoostPercent,
                threshold: settings.threshold,
                excludeStables: settings.excludeStables
            })
        });
        if (!response.ok) {
            // Только если НЕ 204, пытаемся парсить JSON
            const error = response.status === 204 ? {} : await response.json();
            throw new Error(error.error || `HTTP ${response.status}`);
        }
        Swal.fire({
            icon: 'success',
            title: '✅ Bridge Started',
            text: 'Bridge operation initiated successfully',
            background: '#161b22',
            color: '#c9d1d9',
            timer: 2000,
            showConfirmButton: false
        });


    } catch (error) {
        console.error('❌ Bridge failed:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Bridge Failed',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
    } finally {
        button.disabled = false;
        button.textContent = ' ';
    }
}

function renderHeatmap(data) {
    updateTokenChoices(data);
    const chains = getAllChains(data);
    const filteredChains = selectedChain ? [selectedChain] : chains;

    // Calculate max value per chain for color scaling
    const maxValuePerChain = {};
    filteredChains.forEach(chain => {        let max = 0;
        data.forEach(account => {
            const tokens = (account.chainData || {})[chain];
            if (tokens) {
                const total = calculateChainTotal(tokens);
                if (total > max) max = total;
            }
        });
        maxValuePerChain[chain] = max;
    });

    let html = '<table><thead><tr>';
    html += '<th class="id-col">ID</th>';
    html += '<th class="address-col">Address</th>';

    filteredChains.forEach(chain => {
        const labels = { Ethereum: 'ETH', Arbitrum: 'ARB', Base: 'BASE', 'OP Mainnet': 'OP',
            Polygon: 'POL', Avalanche: 'AVAX', Gnosis: 'GNO', HyperEVM: 'HYPE', Blast: 'BLAST',
            Unichain: 'UNI', Mantle: 'MNT', zkSync: 'ZK', Linea: 'LINEA', Scroll: 'SCR' };
        html += `<th title="${chain}"><button type="button" class="treasury-chain-choice" data-chain="${encodeURIComponent(chain)}" aria-pressed="false" onclick="toggleTreasuryChain(decodeURIComponent(this.dataset.chain))" title="Click to select / deselect this network for swaps"><span class="chain-label">${labels[chain] || chain.slice(0, 4).toUpperCase()}</span></button></th>`;
    });

    html += '<th style="background: #1c2128; border-left: 2px solid #30363d;">TOTAL</th>';
    html += '<th style="background: #1c2128; border-left: 2px solid #30363d;">SWAP</th>';
    html += '<th style="background: #1c2128;">BRIDGE</th>';
    html += '</tr></thead><tbody>';

    data.forEach(account => {
        html += '<tr>';
        html += `<td class="id-cell"><button type="button" class="account-id-button treasury-account-id" data-account-id="${account.id}"
            aria-pressed="false" aria-label="Select account ${account.id}" onclick="toggleTreasuryAccount(${account.id})">${account.id}</button></td>`;
        html += `<td class="address-cell" title="${account.address}">
            <a href="https://debank.com/profile/${account.address}" target="_blank" class="address-link">
                ${account.address}
            </a>
        </td>`;

        filteredChains.forEach(chain => {
            const tokens = (account.chainData || {})[chain];
            const hasBalance = tokens && tokens.length > 0;
            const chainTotal = hasBalance ? calculateChainTotal(tokens) : 0;
            const level = getValueLevel(chainTotal, maxValuePerChain[chain]);
            const displayValue = chainTotal >= 1 ? formatCompactUSD(chainTotal) : '';
            const debankUrl = `https://debank.com/profile/${account.address}`;
            html += '<td><div class="cell-wrapper">';
            html += `<div class="heatmap-cell ${level}" 
                         data-account-id="${account.id}"
                         data-address="${account.address}"
                         data-chain="${chain}"
                         data-tokens='${hasBalance ? JSON.stringify(tokens) : '[]'}'>`;
            if (displayValue) {
                html += `<span class="cell-value">${displayValue}</span>`;
            }
            html += '</div></div></td>';
        });

        const accountTotal = Object.values(account.chainData || {})
            .reduce((sum, tokens) => {
                const val = calculateChainTotal(tokens);
                return sum + (isNaN(val) ? 0 : val);
            }, 0);
        const totalLevel = getValueLevel(accountTotal, 1000);
        const totalDisplay = accountTotal >= 1 ? formatCompactUSD(accountTotal) : '';

        html += `<td style="background: #0d1117; border-left: 2px solid #30363d;">
            <div class="cell-wrapper">
                <div class="heatmap-cell ${totalLevel}">
                    <span class="cell-value" style="font-weight: 700;">${totalDisplay}</span>
                </div>
            </div>
        </td>`;

        // ACTION column with swap button
        html += `<td style="background: #0d1117; border-left: 2px solid #30363d; padding: 2px;">
            <button class="swap-btn" onclick="swapAllToNative(${account.id}, this)" title="Swap all tokens for account #${account.id} in selected swap networks (or all visible networks)">
                ⥄
            </button>
        </td>`;

        html += `<td style="background: #0d1117; border-left: 2px solid #30363d; padding: 2px;">
            <button class="bridge-btn" onclick="bridgeToChain(${account.id})" title="bridge all native to particular chain">
                ⤼
            </button>
        </td>`;
        
        html += '</tr>';
    });

    html += '</tbody></table>';

    document.getElementById('tableContainer').innerHTML = html;
    attachTooltipListeners();
    highlightSelectedToken();
    updateSidebar(data);
}

function attachTooltipListeners() {
    const tooltip = document.getElementById('tooltip');
    const cells = document.querySelectorAll('.heatmap-cell');

    cells.forEach(cell => {
        cell.addEventListener('mouseenter', (e) => {
            const target = e.target.classList.contains('heatmap-cell') ? e.target : e.target.closest('.heatmap-cell');
            if (!target) return;

            const tokens = JSON.parse(target.dataset.tokens || '[]');
            if (!tokens || tokens.length === 0) return;

            showTooltip(
                target.dataset.accountId,
                target.dataset.address,
                target.dataset.chain,
                tokens,
                e
            );
        });

        cell.addEventListener('mouseleave', () => {
            tooltip.classList.remove('show');
        });

        cell.addEventListener('mousemove', (e) => {
            updateTooltipPosition(e);
        });
    });
}

function showTooltip(accountId, address, chain, tokens, event) {
    const tooltip = document.getElementById('tooltip');
    const header = document.getElementById('tooltipHeader');
    const addressDiv = document.getElementById('tooltipAddress');
    const content = document.getElementById('tooltipContent');

    const total = calculateChainTotal(tokens);

    header.textContent = `#${accountId} - ${chain}`;
    addressDiv.textContent = address;

    let tokensHtml = '';
    tokens.forEach(token => {
        const amount = formatAmount(token.amountRaw, token.decimals);
        tokensHtml += `
            <div class="token-item">
                <div class="token-left">
                    <span class="token-symbol">${token.symbol}</span>
                    <span class="token-amount">${amount}</span>
                </div>
                <span class="token-value">${formatUSD(token.valueUSD)}</span>
            </div>
        `;
    });

    tokensHtml += `<div class="total-value">Total: ${formatUSD(total)}</div>`;
    content.innerHTML = tokensHtml;

    updateTooltipPosition(event);
    tooltip.classList.add('show');
}

function updateTooltipPosition(event) {
    const tooltip = document.getElementById('tooltip');
    const offset = 12;

    let x = event.clientX + offset;
    let y = event.clientY + offset;

    const rect = tooltip.getBoundingClientRect();
    if (x + rect.width > window.innerWidth) {
        x = event.clientX - rect.width - offset;
    }
    if (y + rect.height > window.innerHeight) {
        y = event.clientY - rect.height - offset;
    }

    tooltip.style.left = x + 'px';
    tooltip.style.top = y + 'px';
}

function toggleAutoRefresh() {
    autoRefresh = !autoRefresh;
    document.getElementById('autoStatus').textContent = autoRefresh ? 'ON' : 'OFF';

    if (autoRefresh) {
        autoRefreshInterval = setInterval(refreshData, 5000);
    } else {
        clearInterval(autoRefreshInterval);
    }
}

async function updateBalances() {
    const maxId = document.getElementById('maxIdInput').value;
    const accountIds = [...selectedTreasuryAccounts].sort((a, b) => a - b);
    const result = await Swal.fire({
        icon: 'question',
        title: 'Start Balance Update?',
        text: accountIds.length ? `Update balances for ${accountIds.length} selected accounts?` : `Update all accounts up to Max ID ${maxId}?`,
        background: '#161b22',
        color: '#c9d1d9',
        showCancelButton: true,
        confirmButtonText: 'Start',
        cancelButtonText: 'Cancel'
    });

    if (!result.isConfirmed) return;

    try {
        const selectedQuery = accountIds.map(id => `&accountIds=${id}`).join('');
        const response = await fetch(`${API_BASE}/update?maxId=${maxId}&minValue=0.001${selectedQuery}`, {
            method: 'POST'
        });
        const result = await response.json();
        if (!response.ok) throw new Error(result.error || `API returned ${response.status}`);
        Swal.fire({
            icon: 'success',
            title: '✅ Balance Update',
            text: result.message,
            background: '#161b22',
            color: '#c9d1d9',
            timer: 3000
        });

        await monitorBalanceUpdate();
    } catch (error) {
        console.error('Failed to update balances:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Update Failed',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
    }
}

async function monitorBalanceUpdate() {
    const button = document.getElementById('updateBalancesButton');
    const label = document.getElementById('balanceUpdateStatus');
    button.disabled = true;
    try {
        while (true) {
            const response = await fetch(`${API_BASE}/update-status`, { cache: 'no-store' });
            if (!response.ok) throw new Error(`Update status returned ${response.status}`);
            const status = await response.json();
            const progress = status.progress || { updated: 0, failed: 0, processed: 0 };
            label.textContent = `${status.running ? 'Updating' : progress.stopped ? 'Stopped' : 'Finished'}: ${progress.processed} checked, ${progress.updated} updated, ${progress.failed} failed` +
                (progress.stopped ? `, ${progress.unprocessed} not attempted` : '');
            if (!status.running) {
                if (!progress.stopped) await refreshData();
                if (status.error || progress.failed > 0) {
                    const failures = progress.failures || [], groups = new Map();
                    for (const failure of failures) {
                        const key = `${failure.stage} · ${failure.code} · ${failure.reason}`;
                        const group = groups.get(key) || { failure, ids: [] }; group.ids.push(failure.accountId); groups.set(key, group);
                    }
                    const reasons = [...groups.values()].map(({ failure, ids }) =>
                        `Accounts: ${ids.join(', ')}\nStage: ${failure.stage}\n${failure.reason}\n${failure.code}: ${failure.details}`);
                    await Swal.fire({
                        icon: 'warning', title: 'Balance update incomplete',
                        text: [status.error, `${progress.updated} updated; ${progress.failed} failed; ${progress.unprocessed || 0} not attempted. Previous balances kept for failed accounts.`, ...reasons].filter(Boolean).join('\n\n'),
                        background: '#161b22', color: '#c9d1d9'
                    });
                }
                break;
            }
            await new Promise(resolve => setTimeout(resolve, 2000));
        }
    } catch (error) {
        label.textContent = 'Update status unavailable; check logs';
        throw error;
    } finally {
        button.disabled = false;
    }
}

function showLoading(show) {
    const overlay = document.getElementById('loadingOverlay');
    if (show) {
        overlay.classList.remove('hidden');
    } else {
        overlay.classList.add('hidden');
    }
}

function updateSidebar(data) {
    updateTopTokens(data);
    updateChainStats(data);
    updatePortfolioSummary(data);
}

function updateTopTokens(data) {
    const tokenAggregation = {};
    let totalValue = 0;

    // Агрегируем все токены
    data.forEach(account => {
        Object.values(account.chainData || {}).forEach(tokens => {
            tokens.forEach(token => {
                const key = tokenSymbol(token);
                if (!key) return;
                if (!tokenAggregation[key]) {
                    tokenAggregation[key] = {
                        symbol: key,
                        totalValue: 0,
                        accounts: new Set()
                    };
                }
                tokenAggregation[key].totalValue += token.valueUSD;
                tokenAggregation[key].accounts.add(account.id);
                totalValue += token.valueUSD;
            });
        });
    });

    // Сортируем по стоимости
    const sortedTokens = Object.values(tokenAggregation)
        .sort((a, b) => b.totalValue - a.totalValue);

    const list = document.getElementById('topTokens'); list.replaceChildren();
    for (const token of sortedTokens) {
        const percent = totalValue > 0 ? ((token.totalValue / totalValue) * 100).toFixed(1) : '0.0';
        const choice = document.createElement('button'); choice.type = 'button'; choice.className = 'token-choice';
        choice.dataset.symbol = token.symbol; choice.onclick = () => selectTreasuryToken(token.symbol);
        const make = (tag, className, text) => {
            const node = document.createElement(tag); node.className = className;
            if (text !== undefined) node.textContent = text; return node;
        };
        const row = make('div', 'token-row'), info = make('div', 'token-info'), value = make('div', 'token-value');
        info.append(make('span', 'token-symbol', token.symbol), make('span', 'token-accounts', `${token.accounts.size} accounts`));
        value.append(make('span', 'token-usd', formatUSD(token.totalValue)), make('span', 'token-percent', `${percent}%`));
        row.append(info, value);
        const bar = make('div', 'progress-bar'), fill = make('div', 'progress-fill'); fill.style.width = `${percent}%`;
        bar.append(fill); choice.append(row, bar); list.append(choice);
    }
    highlightSelectedToken();
}

function updateChainStats(data) {
    const chainAggregation = {};

    data.forEach(account => {
        Object.entries(account.chainData || {}).forEach(([chain, tokens]) => {
            if (!chainAggregation[chain]) {
                chainAggregation[chain] = {
                    name: chain,
                    totalValue: 0,
                    accounts: 0
                };
            }
            chainAggregation[chain].totalValue += calculateChainTotal(tokens);
            chainAggregation[chain].accounts++;
        });
    });

    const sortedChains = Object.values(chainAggregation)
        .sort((a, b) => b.totalValue - a.totalValue)
        .slice(0, 10);

    const html = sortedChains.map(chain => {
        const iconUrl = `https://raw.githubusercontent.com/lifinance/types/refs/heads/main/src/assets/icons/chains/${chain.name.toLowerCase()}.svg`;
        return `
            <div class="chain-stat-row">
                <div class="chain-stat-left">
                    <img src="${iconUrl}" class="chain-stat-icon" onerror="this.style.display='none';">
                    <span class="chain-stat-name">${chain.name}</span>
                </div>
                <span class="chain-stat-value">${formatUSD(chain.totalValue)}</span>
            </div>
        `;
    }).join('');

    document.getElementById('chainStats').innerHTML = html;
}

function updatePortfolioSummary(data) {
    const totalValue = calculateTotalValue(data);
    const uniqueTokens = new Set();
    const uniqueChains = new Set();

    data.forEach(account => {
        Object.entries(account.chainData || {}).forEach(([chain, tokens]) => {
            uniqueChains.add(chain);
            tokens.forEach(token => uniqueTokens.add(token.symbol));
        });
    });

    const html = `
        <div style="display: flex; flex-direction: column; gap: 6px;">
            <div style="display: flex; justify-content: space-between;">
                <span>Total Value:</span>
                <span style="color: #ffd700; font-weight: 600;">${formatUSD(totalValue)}</span>
            </div>
            <div style="display: flex; justify-content: space-between;">
                <span>Unique Tokens:</span>
                <span style="color: #58a6ff;">${uniqueTokens.size}</span>
            </div>
            <div style="display: flex; justify-content: space-between;">
                <span>Unique Chains:</span>
                <span style="color: #58a6ff;">${uniqueChains.size}</span>
            </div>
            <div style="display: flex; justify-content: space-between;">
                <span>Active Accounts:</span>
                <span style="color: #58a6ff;">${data.filter(a => Object.keys(a.chainData || {}).length > 0).length}</span>
            </div>
        </div>
    `;

    document.getElementById('portfolioSummary').innerHTML = html;
}

document.addEventListener('DOMContentLoaded', () => {
    console.log('App initialized');
    console.log('API_BASE:', API_BASE);
    if (!sessionStorage.getItem('logsAutoCleared')) {
        if (typeof clearLogsServer === 'function') {
            console.log('First run in this session: clearing logs...');
            clearLogsServer(true); // silent = true
            sessionStorage.setItem('logsAutoCleared', 'true');
        }
    }
    checkDbAndInit();

    document.addEventListener('keydown', (e) => {
        const isMod = e.ctrlKey || e.metaKey;
        if (!isMod) return;

        if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.code === 'KeyP') { e.preventDefault(); setPin(); }
        if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.code === 'KeyD') { e.preventDefault(); setDb(); }
        if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.code === 'KeyI') { e.preventDefault(); importWallets(); }
        if (e.code === 'KeyH') { e.preventDefault(); showHelp(); }
    });


});
async function setPin() {
    const { value: pin } = await Swal.fire({
        title: 'Enter PIN',
        input: 'password', // Теперь PIN скрыт звездочками
        inputLabel: 'PIN for key decryption',
        inputPlaceholder: 'Enter your PIN',
        background: '#161b22',
        color: '#c9d1d9',
        showCancelButton: true,
        confirmButtonText: 'Set PIN',
        inputAttributes: {
            autocapitalize: 'off',
            autocorrect: 'off'
        }
    });

    if (!pin) return;
    const encodedPin = btoa(pin);
    try {
        console.log('🔐 Setting PIN on server...');

        const response = await fetch(`${API_BASE}/pin`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ pin: encodedPin })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || `HTTP ${response.status}`);
        }

        const result = await response.json();
        console.log('✅ PIN set successfully');
        Swal.fire({
            icon: 'success',
            title: '✅ PIN Saved',
            html: '<p>PIN saved on server until restart</p><p style="font-size: 0.9em; color: #8b949e; margin-top: 10px;">Hotkey: <kbd>Ctrl+Shift+P</kbd></p>',
            background: '#161b22',
            color: '#c9d1d9',
            timer: 3000
        });
    } catch (error) {
        console.error('❌ Failed to set PIN:', error);
        Swal.fire({
            icon: 'error',
            title: '❌ Failed to Set PIN',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
    }
}

async function importWallets() {
    const { value: wallets } = await Swal.fire({
        title: 'Import Wallets',
        input: 'textarea',
        inputLabel: 'Enter keys or seeds',
        inputPlaceholder: 'One per line or comma-separated:\n0x123...\n0x456...',
        background: '#161b22',
        color: '#c9d1d9',
        showCancelButton: true,
        confirmButtonText: 'Import',
        inputValidator: (value) => {
            if (!value) return 'Please enter at least one wallet';
        }
    });

    if (!wallets) return;

    // Обработка: разделение по запятым или переносам строк
    const walletList = wallets
        .split(/[,\n]/)
        .map(w => w.trim())
        .filter(w => w.length > 0);

    try {
        const response = await fetch(`${API_BASE}/import-wallets`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ wallets: walletList })
        });
        // ... обработка ответа
    } catch (error) {
        console.error('❌ Failed:', error);
    }
}

async function setDb() {
    try {
        const response = await fetch(`${API_BASE}/db-config`, { cache: 'no-store' });
        if (response.ok) savedDbConfig = await response.json();
    } catch (error) { console.error('Failed to load database settings:', error); }
    const { value: formValues } = await Swal.fire({
        title: 'Database Settings',
        html: `
            <style>
                .db-fields {
                    transition: all 0.3s ease;
                    overflow: hidden;
                }
                .swal2-input, .swal2-select {
                    margin: 8px 0 !important;
                }
            </style>
            
            <select id="swal-db-type" class="swal2-select">
                <option value="">🗄️ Select database type</option>
                <option value="sqlite">📁 SQLite (Local file)</option>
                <option value="postgres">🐘 PostgreSQL (Server)</option>
            </select>
            
            <div id="sqlite-fields" class="db-fields" style="display: none;">
                <input id="swal-sqlite-path" class="swal2-input" 
                       placeholder="📂 Path to database file" 
                       value="./database.db">
            </div>
            
            <div id="postgres-fields" class="db-fields" style="display: none;">
                <div style="display: flex; gap: 10px;">
                    <input id="swal-host" class="swal2-input" 
                           placeholder="🌐 Host" value="localhost"
                           style="flex: 3;">
                    <input id="swal-port" class="swal2-input" 
                           placeholder="🔌 Port" type="number" value="5432"
                           style="flex: 1;">
                </div>
                <input id="swal-db" class="swal2-input" 
                       placeholder="💾 Database name" value="postgres">
                <input id="swal-user" class="swal2-input" 
                       placeholder="👤 Username" value="postgres">
                <input id="swal-pass" class="swal2-input" 
                       placeholder="🔑 Password" type="password">
            </div>
        `,
        background: '#161b22',
        color: '#c9d1d9',
        width: '500px',
        showCancelButton: true,
        confirmButtonText: '💾 Save',
        cancelButtonText: '❌ Cancel',
        focusConfirm: false,

        didOpen: () => {
            const dbTypeSelect = document.getElementById('swal-db-type');
            const sqliteFields = document.getElementById('sqlite-fields');
            const postgresFields = document.getElementById('postgres-fields');

            dbTypeSelect.addEventListener('change', (e) => {
                const type = e.target.value;

                // Плавное скрытие всех полей
                sqliteFields.style.display = 'none';
                postgresFields.style.display = 'none';

                // Показ нужных полей
                setTimeout(() => {
                    if (type === 'sqlite') {
                        sqliteFields.style.display = 'block';
                    } else if (type === 'postgres') {
                        postgresFields.style.display = 'block';
                    }
                }, 50);
            });
            fillSavedDbConfig();
        },

        showLoaderOnConfirm: true,
        preConfirm: async () => {
            const dbType = document.getElementById('swal-db-type').value;

            if (!dbType) {
                Swal.showValidationMessage('⚠️ Please select database type');
                return false;
            }

            if (dbType === 'sqlite') {
                const path = document.getElementById('swal-sqlite-path').value;
                if (!path) {
                    Swal.showValidationMessage('⚠️ Path is required');
                    return false;
                }
                return await submitDbConfig({ type: 'sqlite', sqlitePath: path });

            } else if (dbType === 'postgres') {
                const config = {
                    type: 'postgres',
                    host: document.getElementById('swal-host').value,
                    port: String(document.getElementById('swal-port').value || '5432'),
                    database: document.getElementById('swal-db').value,
                    user: document.getElementById('swal-user').value,
                    password: document.getElementById('swal-pass').value
                };

                if (!config.host || !config.database || !config.user) {
                    Swal.showValidationMessage('⚠️ Host, Database, and Username are required');
                    return false;
                }

                return await submitDbConfig(config);
            }
        }
    });

    if (!formValues) return;

    // Отправка на сервер
    try {
        Swal.fire({
            icon: 'success',
            title: '✅ Saved!',
            text: `Database: ${formValues.type}`,
            background: '#161b22',
            color: '#c9d1d9',
            timer: 2000
        });

    } catch (error) {
        Swal.fire({
            icon: 'error',
            title: '❌ Error',
            text: error.message,
            background: '#161b22',
            color: '#c9d1d9'
        });
    }
}

function showHelp() {
    Swal.fire({
        title: '⌨️ Keyboard Shortcuts',
        html: `
            <div style="text-align: left; font-family: monospace;">
                <p><strong>Ctrl + Shift + P</strong> - Set PIN</p>
                <p><strong>Ctrl + Shift + I</strong> - Import Wallets</p>
                <p><strong>Ctrl + Shift + D</strong> - DataBase mode</p>
                <p><strong>Ctrl + F5</strong> - Reload Chains </p>
                <p><strong>Ctrl + H</strong> - Show this help</p>
                <p><strong>Esc</strong> - Close dialogs</p>
            </div>
        `,
        background: '#161b22',
        color: '#c9d1d9',
        confirmButtonText: 'Got it!',
        width: '500px',
        // Необязательные параметры:
        showCloseButton: true,  // Кнопка X справа
        showCancelButton: false, // Без кнопки Cancel
        icon: 'info' // Иконка: 'success', 'error', 'warning', 'info', 'question'
    });
}
