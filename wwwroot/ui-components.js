// Показать/скрыть список
function toggleMultiselect() {
    const el = document.getElementById('chainMultiselect');
    el.classList.toggle('active');
    console.log('Multiselect toggled, classes:', el.className); // Для отладки
}

// Закрывать список, если кликнули вне его
window.onclick = function(event) {
    if (!event.target.closest('#chainMultiselect')) {
        document.getElementById('chainMultiselect').classList.remove('active');
    }
}

// Функция для получения массива выбранных значений
function getSelectedChains() {
    // Берем только те, у которых есть класс chain-checkbox
    const checkboxes = document.querySelectorAll('.chain-checkbox:checked');
    return Array.from(checkboxes).map(cb => cb.value);
}

// Функция инициализации (вызови её, когда получишь список сетей с сервера)
function updateChainCheckboxes(chains) {
    const container = document.getElementById('chainCheckboxes');

    // Генерируем HTML: сначала "Select All", потом основные чейны
    const selectAllHtml = `
    <label class="select-all-label" style="border-bottom: 1px solid #30363d; margin-bottom: 5px; padding-bottom: 5px;">
        <input type="checkbox" id="selectAllChains" onchange="toggleAllChains(this)">
        <strong>Select All</strong>
    </label>
`;

    const chainsHtml = chains.map(chain => `
    <label>
        <input type="checkbox" class="chain-checkbox" value="${chain}" onchange="onChainSelectionChange()">
        ${chain}
    </label>
`).join('');

    container.innerHTML = selectAllHtml + chainsHtml;

    // ДОБАВЛЕНО: Заполняем destination dropdown тем же списком
    const destinationSelect = document.getElementById('destinationChainSelect');
    if (destinationSelect) {
        destinationSelect.innerHTML = chains.map(chain =>
            `<option value="${chain}">${chain}</option>`
        ).join('');
    }
}
function toggleAllChains(source) {
    const checkboxes = document.querySelectorAll('.chain-checkbox');
    checkboxes.forEach(cb => {
        cb.checked = source.checked;
    });

    // После массового изменения вызываем обновление данных
    onChainSelectionChange();
}

// Срабатывает при каждом клике на чекбокс
function onChainSelectionChange() {
    const selected = getSelectedChains();
    const allCheckboxes = document.querySelectorAll('.chain-checkbox');
    const selectAllCb = document.getElementById('selectAllChains');

    // Если выбраны все — ставим галочку на Select All, иначе снимаем
    if (selectAllCb) {
        selectAllCb.checked = (selected.length === allCheckboxes.length);
    }

    const label = document.getElementById('selectedChainsLabel');
    label.innerText = selected.length > 0 ? `Selected: ${selected.length}` : "All Chains";

    filterByChainsArray(selected);
}

async function filterByChainsArray(selected) {
    const maxId = document.getElementById('maxIdInput').value;

    // Превращаем массив ["ETH", "BSC"] в строку "ETH,BSC"
    const chainsParam = selected.join(',');

    const response = await fetch('/api/treasury/data?maxId=100&chains=' + selected.join(','));
    const data = await response.json();

    // Твоя логика отрисовки таблицы/хитмапа
    renderHeatmap(data);
}



// Initialize logs
refreshLogs();
logsAutoRefreshInterval = setInterval(refreshLogs, 3000);

// Check if app.js loaded
setTimeout(() => {
    if (typeof refreshData === 'undefined') {
        console.error('❌ app.js did NOT load!');
        alert('ERROR: app.js failed to load. Check Network tab in DevTools.');
    } else {
        console.log('✅ app.js loaded successfully');
    }
}, 100);

document.addEventListener('DOMContentLoaded', async () => {
    try {
        // Запрашиваем список сетей у сервера
        const response = await fetch('/api/treasury/chains');
        if (response.ok) {
            const realChains = await response.json();
            // Наполняем наш список чекбоксов
            updateChainCheckboxes(realChains);
        } else {
            console.error('Failed to load chains');
        }
    } catch (e) {
        console.error('Error fetching chains:', e);
    }
});