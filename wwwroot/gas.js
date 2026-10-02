function getGasBoostPercent() {
    const input = document.getElementById('gasBoostInput');
    if (!input.reportValidity()) throw new Error('Enter a valid Gas +% between 0 and 1000');
    return Number(input.value);
}

function saveGasBoost() {
    const percent = getGasBoostPercent();
    try { localStorage.setItem('z3nbank-gas-boost', String(percent)); } catch {}
}

document.addEventListener('DOMContentLoaded', () => {
    try {
        const saved = localStorage.getItem('z3nbank-gas-boost');
        if (saved !== null && saved.trim() !== '') {
            const percent = Number(saved);
            if (Number.isFinite(percent) && percent >= 0 && percent <= 1000 && /^\d+(\.\d{1,2})?$/.test(saved))
                document.getElementById('gasBoostInput').value = saved;
        }
    } catch {}
});
