(() => {
    const fields = new Map();
    let enabled = false, timer, request = 0;
    function render() {
        for (const [input, hint] of fields) {
            if (!input.isConnected) { fields.delete(input); continue; }
            hint.hidden = !enabled || document.activeElement !== input;
        }
    }
    function register() {
        for (const input of document.querySelectorAll('input[type="password"]')) {
            if (fields.has(input)) continue;
            const hint = document.createElement('div');
            hint.id = `caps-lock-${fields.size}-${Date.now()}`; hint.className = 'caps-lock-hint';
            hint.textContent = 'Caps Lock включён'; hint.hidden = true;
            hint.setAttribute('role', 'status'); hint.setAttribute('aria-live', 'polite');
            input.insertAdjacentElement('afterend', hint);
            input.setAttribute('aria-describedby', [input.getAttribute('aria-describedby'), hint.id].filter(Boolean).join(' '));
            fields.set(input, hint);
        }
        render();
    }
    async function refresh() {
        const input = document.activeElement, serial = ++request;
        if (!fields.has(input)) return;
        try {
            const response = await fetch('/api/treasury/keyboard-status');
            if (!response.ok) return;
            const state = await response.json();
            if (serial === request && document.activeElement === input) { enabled = !!state.capsLock; render(); }
        } catch (_) { /* Keyboard events still provide state when the server is unavailable. */ }
    }
    for (const name of ['keydown', 'keyup']) document.addEventListener(name, event => {
        if (typeof event.getModifierState !== 'function') return;
        request++; enabled = event.getModifierState('CapsLock'); render();
    }, true);
    document.addEventListener('focusin', () => {
        register(); clearInterval(timer); refresh();
        if (fields.has(document.activeElement)) timer = setInterval(refresh, 1000);
    });
    document.addEventListener('focusout', () => { request++; clearInterval(timer); render(); });
    window.addEventListener('focus', refresh);
    new MutationObserver(register).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['type'] });
    register();
})();
