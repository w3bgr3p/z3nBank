// LOGS VARIABLES
const SERVER = window.location.origin;
let logsAutoRefresh = true;
let logsAutoRefreshInterval = null;

// ===== LOGS FUNCTIONS =====
async function refreshLogs() {
    const level = document.getElementById('logsLevelFilter').value;
    const machine = document.getElementById('logsMachineFilter').value;
    const project = document.getElementById('logsProjectFilter').value;
    const limit = document.getElementById('logsLimitInput').value;

    let url = `${SERVER}/api/treasury/logs?limit=${limit}`;
    if (level) url += `&level=${level}`;
    if (machine) url += `&machine=${encodeURIComponent(machine)}`;
    if (project) url += `&project=${encodeURIComponent(project)}`;

    try {
        const response = await fetch(url);
        const logs = await response.json();
        renderLogs(logs);
        await updateLogsFilters();
    } catch (e) {
        console.error('Logs refresh failed:', e);
    }
}

function formatSession(session) {
    const sessionNum = parseInt(session);
    if (isNaN(sessionNum) || sessionNum < 1000000000) return `${session}`;

    const timestamp = sessionNum > 9999999999 ? sessionNum : sessionNum * 1000;
    const date = new Date(timestamp);
    if (isNaN(date.getTime())) return `${session}`;

    const now = new Date();
    const diff = now - date;

    if (diff < 86400000 && diff > 0) {
        return `${date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}`;
    }
    return `${date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} ${date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}`;
}

function renderLogs(logs) {
    const container = document.getElementById('logsContainer');
    container.innerHTML = logs.reverse().map(log => {const extraStr = log.extra ? JSON.stringify(log.extra) : '';
        return `
                <div class="log-entry ${log.level}">
                    <div class="log-header">
                        <span class="timestamp">${new Date(log.timestamp).toLocaleTimeString()}</span>
                        <span class="level ${log.level}" onclick="setLogsFilter('logsLevelFilter', '${log.level}')">${log.level}</span>
                        <span class="session" title="${log.session}">${formatSession(log.session)}</span>
                        
                        <span class="account" onclick="setLogsFilter('logsAccountFilter', '${log.account}')">${log.account}</span>
                        <span style="color: #6e7681; font-size: 10px;">${extraStr}</span>
                    </div>
                    <div class="message" onclick="copyToClipboard(this.innerText, this)" title="Click to copy">${escapeHtml(log.message)}</div>
                </div>
            `;
    }).join('');
}

async function updateLogsFilters() {
    try {
        const response = await fetch(`${SERVER}/stats`);
        const stats = await response.json();

        const mFilter = document.getElementById('logsMachineFilter');
        const pFilter = document.getElementById('logsProjectFilter');

        const curM = mFilter.value;
        const curP = pFilter.value;

        if (stats.byMachine) {
            mFilter.innerHTML = '<option value="">Machines</option>' +
                Object.keys(stats.byMachine).map(m => `<option value="${m}" ${m === curM ? 'selected' : ''}>${m}</option>`).join('');
        }
        if (stats.byProject) {
            pFilter.innerHTML = '<option value="">Projects</option>' +
                Object.keys(stats.byProject).map(p => `<option value="${p}" ${p === curP ? 'selected' : ''}>${p}</option>`).join('');
        }
    } catch (e) {
        console.error('Stats failed:', e);
    }
}

function toggleLogsAutoRefresh() {
    logsAutoRefresh = !logsAutoRefresh;
    document.getElementById('logsAutoStatus').textContent = logsAutoRefresh ? 'ON' : 'OFF';
    if (logsAutoRefresh) {
        logsAutoRefreshInterval = setInterval(refreshLogs, 3000);
    } else {
        clearInterval(logsAutoRefreshInterval);
    }
}



async function clearLogsServer(silent = false) {
    // 1. Добавляем открывающую скобку после условия
    if (!silent) {
        const result = await Swal.fire({
            title: 'Clear Logs',
            text: `This operation will clear all logs`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, delete it!',
            background: '#161b22',
            color: '#c9d1d9',
            confirmButtonColor: '#3085d6',
            cancelButtonColor: '#d33'
        });

        if (!result.isConfirmed) {
            return;
        }
    } // Эта скобка теперь закрывает блок "if (!silent)"

    try {
        await fetch(`${SERVER}/api/treasury/clear`, { method: 'POST' });
        refreshLogs();

        Swal.fire({
            icon: 'success',
            title: '✅ Logs Cleared', // Исправил опечатку "Claered"
            text: '',
            background: '#161b22',
            color: '#c9d1d9',
            timer: 2000,
            showConfirmButton: false
        });

    } catch (e) {
        console.error('Clear failed:', e);
    }
}



function setLogsFilter(filterId, value) {
    const filterElement = document.getElementById(filterId);
    if (filterElement) {
        filterElement.value = value;
        if (filterElement.value !== value) {
            const opt = document.createElement('option');
            opt.value = value;
            opt.innerHTML = value;
            filterElement.appendChild(opt);
            filterElement.value = value;
        }
        refreshLogs();
    }
}

function copyToClipboard(text, element) {
    if (!text) return;
    navigator.clipboard.writeText(text).then(() => {
        const originalBg = element.style.background;
        element.style.background = "rgba(63, 185, 80, 0.2)";
        setTimeout(() => { element.style.background = originalBg; }, 200);
    });
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}