async function refreshSwapOperations() {
    try {
        const response = await fetch(`${API_BASE}/swaps/status`, { cache: 'no-store' });
        if (!response.ok) return;
        const status = await response.json();
        document.getElementById('stopSwapsButton').disabled = status.active === 0 || status.stopping;
        document.getElementById('swapOperationsStatus').textContent = status.active
            ? `${status.stopping ? 'Stopping' : 'Running'}: ${status.active} swap operations` : '';
    } catch (error) { console.error('Swap status:', error.message); }
}

async function stopSwaps() {
    const button = document.getElementById('stopSwapsButton');
    button.disabled = true;
    try {
        const response = await fetch(`${API_BASE}/swaps/stop`, { method: 'POST' });
        if (!response.ok) throw new Error(`Stop failed: HTTP ${response.status}`);
        document.getElementById('swapOperationsStatus').textContent =
            'Stopping. Broadcast transactions continue on-chain; check their hashes in Logs.';
    } catch (error) {
        await Swal.fire({ icon: 'error', title: 'Stop swaps', text: error.message });
    }
    await refreshSwapOperations();
}

document.addEventListener('DOMContentLoaded', () => {
    refreshSwapOperations();
    setInterval(refreshSwapOperations, 1500);
});
