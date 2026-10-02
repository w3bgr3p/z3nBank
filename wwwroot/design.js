function setBankTheme(theme) {
    if (!['dark', 'graphite', 'light', 'hyper'].includes(theme)) theme = 'graphite';
    document.documentElement.dataset.theme = theme;
    try { localStorage.setItem('z3nbank-theme', theme); } catch {}
    const select = document.getElementById('themeSelect');
    if (select) select.value = theme;
}
let bankTheme = 'graphite';
try { bankTheme = localStorage.getItem('z3nbank-theme') || bankTheme; } catch {}
setBankTheme(bankTheme);
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('themeSelect').value = document.documentElement.dataset.theme;
});
function toggleBankLogs() {
    setBankLogsOpen(document.getElementById('logsPanel').hidden);
}
function setBankLogsOpen(open) {
    document.getElementById('logsPanel').hidden = !open;
    document.getElementById('logsToggle').setAttribute('aria-expanded', String(open));
}
document.addEventListener('click', event => {
    if (!event.target.closest('#logsPanel, #logsToggle')) setBankLogsOpen(false);
});
document.addEventListener('keydown', event => {
    if (event.key === 'Escape') setBankLogsOpen(false);
});
