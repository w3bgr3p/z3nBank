using System.Text;
using Newtonsoft.Json;
using z3n;

namespace z3nSafe;

public class HeatmapGenerator
{
    private readonly Db _db;

    public HeatmapGenerator(Db dbConnection)
    {
        _db = dbConnection;
    }

    public class AccountData
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public Dictionary<string, List<TokenInfo>> ChainData { get; set; } = new();
    }

    public class TokenInfo
    {
        public string Symbol { get; set; }
        public string Amount { get; set; }
        public int Decimals { get; set; }
        public string PriceUSD { get; set; }
        public int ChainId { get; set; }
        public string Address { get; set; }
        public decimal ValueUSD { get; set; }
    }
    
    public List<AccountData> GetTreasuryData(int maxId = 1000, List<string> selectedChains = null)
    {
        var result = new List<AccountData>();
        var allColumns = _db.GetTableColumns("_treasury");
    
        // Если сети выбраны — фильтруем список колонок, иначе берем все кроме ID
        var columnsToProcess = (selectedChains != null && selectedChains.Count > 0)
            ? allColumns.Where(c => selectedChains.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
            : allColumns.Where(c => c.ToLower() != "id").ToList();

        for (int id = 1; id <= maxId; id++)
        {
            var address = _db.Get("evm", "_addresses", where: $"id = {id}");
            if (string.IsNullOrEmpty(address)) continue;

            var accountData = new AccountData { Id = id, Address = address };

            foreach (var chainName in columnsToProcess)
            {
                var chainJson = _db.Get(chainName, "_treasury", where: $"id = {id}");
                if (!string.IsNullOrEmpty(chainJson))
                {
                    try {
                        var tokens = JsonConvert.DeserializeObject<List<TokenInfo>>(chainJson);
                        if (tokens != null && tokens.Count > 0)
                            accountData.ChainData[chainName] = tokens;
                    } catch { /* log error */ }
                }
            }
            result.Add(accountData);
        }
        return result;
    }
    
    public void GenerateHtmlHeatmap(string outputPath, int maxId = 1000, bool autoOpen = true)
    {
        Console.WriteLine("Collecting treasury data...");
        var data = GetTreasuryData(maxId);
        
        Console.WriteLine($"Found {data.Count} accounts");
        
        var dataJson = JsonConvert.SerializeObject(data, Formatting.Indented);
        
        var html = GetHtmlTemplate(dataJson);
        
        File.WriteAllText(outputPath, html);
        Console.WriteLine($"Heatmap saved to: {outputPath}");
        
        if (autoOpen)
        {
            OpenInBrowser(outputPath);
        }
    }
    
    private void OpenInBrowser(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", fullPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", fullPath);
            }
            
            Console.WriteLine($"✓ Opened in browser: {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not auto-open browser: {ex.Message}");
            Console.WriteLine($"Please open manually: {Path.GetFullPath(filePath)}");
        }
    }

    private string GetHtmlTemplate(string dataJson)
    {
        return @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Treasury Heatmap</title>
    

    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: 'Iosevka', 'Consolas', monospace;
            background: #0d1117;
            color: #c9d1d9;
            font-size: 11px;
            overflow: hidden;
            height: 100vh;
            display: flex;
            flex-direction: column;
        }

        

        .header {
            background: #161b22;
            border-bottom: 1px solid #30363d;
            padding: 10px 15px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-shrink: 0;
        }

        .header h1 {
            font-size: 14px;
            font-weight: 600;
            color: #58a6ff;
        }

        .stats-bar {
            display: flex;
            gap: 15px;
            font-size: 10px;
        }

        .stat-item {
            display: flex;
            gap: 4px;
            align-items: center;
        }

        .stat-label {
            color: #8b949e;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }

        .stat-value {
            color: #58a6ff;
            font-weight: 600;
        }

        .table-wrapper {
            flex: 1;
            overflow: auto;
            position: relative;
        }

        .table-container {
            display: inline-block;
            min-width: 100%;
            padding: 8px;
        }

        table {
            border-collapse: separate;
            border-spacing: 0;
            font-size: 9px;
        }

        th, td {
            border: 1px solid #21262d;
            padding: 0;
            text-align: center;
        }

        th {
            background: #161b22;
            color: #8b949e;
            font-weight: 600;
            position: sticky;
            top: 0;
            z-index: 10;
            padding: 4px 6px;
            text-transform: uppercase;
            letter-spacing: 0.3px;
            font-size: 8px;
            white-space: nowrap;
        }

        th.id-col {
            position: sticky;
            left: 0;
            z-index: 11;
            width: 35px;
            background: #161b22;
        }

        th.address-col {
            position: sticky;
            left: 35px;
            z-index: 11;
            min-width: 150px;
            text-align: left;
            background: #161b22;
        }

        td.id-cell {
            position: sticky;
            left: 0;
            z-index: 5;
            background: #0d1117;
            font-weight: 600;
            color: #58a6ff;
            padding: 4px;
            border-right: 2px solid #30363d;
        }

        td.address-cell {
            position: sticky;
            left: 35px;
            z-index: 5;
            background: #0d1117;
            font-family: 'Courier New', monospace;
            font-size: 9px;
            padding: 4px 6px;
            text-align: left;
            color: #c9d1d9;
            border-right: 2px solid #30363d;
            white-space: nowrap;
        }

        .cell-wrapper {
            padding: 2px;
        }

        .heatmap-cell {
            width: 14px;
            height: 14px;
            cursor: pointer;
            transition: transform 0.15s, box-shadow 0.15s;
            border-radius: 2px;
            display: flex;
            align-items: center;
            justify-content: center;
            position: relative;
            margin: 0 auto;
        }

        .heatmap-cell.empty {
            background: #161b22;
            border: 1px dashed #30363d;
        }

        .heatmap-cell.level-1 {
            background: #0e4429;
            border: 1px solid #0e4429;
        }

        .heatmap-cell.level-2 {
            background: #006d32;
            border: 1px solid #006d32;
        }

        .heatmap-cell.level-3 {
            background: #26a641;
            border: 1px solid #26a641;
        }

        .heatmap-cell.level-4 {
            background: #39d353;
            border: 1px solid #39d353;
        }

        .heatmap-cell:hover {
            transform: scale(1.4);
            z-index: 100;
            box-shadow: 0 0 10px rgba(88, 166, 255, 0.6);
        }

        .cell-value {
            font-size: 7px;
            color: #ffffff;
            font-weight: 700;
            text-shadow: 0 1px 2px rgba(0,0,0,0.9);
            line-height: 1;
        }

        .tooltip {
            position: fixed;
            background: #1c2128;
            border: 2px solid #58a6ff;
            border-radius: 6px;
            padding: 10px;
            max-width: 350px;
            box-shadow: 0 8px 24px rgba(0,0,0,0.7);
            z-index: 1000;
            pointer-events: none;
            opacity: 0;
            transition: opacity 0.12s;
            font-size: 10px;
        }

        .tooltip.show {
            opacity: 1;
        }

        .tooltip-header {
            font-weight: 600;
            color: #58a6ff;
            margin-bottom: 6px;
            font-size: 11px;
            padding-bottom: 4px;
            border-bottom: 1px solid #30363d;
        }

        .tooltip-address {
            font-size: 9px;
            color: #8b949e;
            font-family: 'Courier New', monospace;
            margin-bottom: 8px;
        }

        .tooltip-content {
            max-height: 250px;
            overflow-y: auto;
        }

        .token-item {
            display: flex;
            justify-content: space-between;
            padding: 3px 5px;
            margin: 2px 0;
            background: rgba(48, 54, 61, 0.4);
            border-radius: 3px;
            border-left: 2px solid #238636;
            font-size: 9px;
        }

        .token-left {
            display: flex;
            flex-direction: column;
            gap: 1px;
        }

        .token-symbol {
            font-weight: 600;
            color: #7ee787;
            font-size: 9px;
        }

        .token-amount {
            color: #8b949e;
            font-size: 8px;
            font-family: 'Courier New', monospace;
        }

        .token-value {
            color: #ffd700;
            font-weight: 600;
            font-size: 9px;
            white-space: nowrap;
        }

        .total-value {
            margin-top: 6px;
            padding-top: 4px;
            border-top: 1px solid #30363d;
            font-weight: 600;
            color: #ffd700;
            text-align: right;
            font-size: 10px;
        }

        .legend {
            padding: 8px 15px;
            background: #161b22;
            border-top: 1px solid #30363d;
            display: flex;
            gap: 12px;
            align-items: center;
            font-size: 9px;
            color: #8b949e;
            flex-shrink: 0;
        }

        .legend-item {
            display: flex;
            align-items: center;
            gap: 4px;
        }

        .legend-box {
            width: 10px;
            height: 10px;
            border-radius: 2px;
        }

        ::-webkit-scrollbar {
            width: 8px;
            height: 8px;
        }

        ::-webkit-scrollbar-track {
            background: #0d1117;
        }

        ::-webkit-scrollbar-thumb {
            background: #30363d;
            border-radius: 4px;
        }

        ::-webkit-scrollbar-thumb:hover {
            background: #484f58;
        }
    </style>
</head>
<body>
    <div class='header'>
        <h1>🗺️ Treasury Heatmap</h1>
        <div class='stats-bar'>
            <div class='stat-item'>
                <span class='stat-label'>Accounts:</span>
                <span class='stat-value' id='totalAccounts'>-</span>
            </div>
            <div class='stat-item'>
                <span class='stat-label'>Active:</span>
                <span class='stat-value' id='activeAccounts'>-</span>
            </div>
            <div class='stat-item'>
                <span class='stat-label'>Chains:</span>
                <span class='stat-value' id='totalChains'>-</span>
            </div>
            <div class='stat-item'>
                <span class='stat-label'>Total:</span>
                <span class='stat-value' id='totalValue'>$0</span>
            </div>
        </div>
    </div>

    <div class='table-wrapper'>
        <div class='table-container' id='tableContainer'></div>
    </div>

    <div class='legend'>
        <span>Less</span>
        <div class='legend-item'><div class='legend-box' style='background: #161b22; border: 1px dashed #30363d;'></div></div>
        <div class='legend-item'><div class='legend-box' style='background: #0e4429;'></div></div>
        <div class='legend-item'><div class='legend-box' style='background: #006d32;'></div></div>
        <div class='legend-item'><div class='legend-box' style='background: #26a641;'></div></div>
        <div class='legend-item'><div class='legend-box' style='background: #39d353;'></div></div>
        <span>More</span>
    </div>

    <div class='tooltip' id='tooltip'>
        <div class='tooltip-header' id='tooltipHeader'></div>
        <div class='tooltip-address' id='tooltipAddress'></div>
        <div class='tooltip-content' id='tooltipContent'></div>
    </div>

    <script>
        const treasuryData = " + dataJson + @";

        function getAllChains(data) {
            const chains = new Set();
            data.forEach(account => {
                Object.keys(account.ChainData).forEach(chain => chains.add(chain));
            });
            return Array.from(chains).sort();
        }

        function calculateChainTotal(tokens) {
            return tokens.reduce((sum, token) => sum + (token.ValueUSD || 0), 0);
        }

        function calculateTotalValue(data) {
            let total = 0;
            data.forEach(account => {
                Object.values(account.ChainData).forEach(tokens => {
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
            const amount = parseFloat(amountRaw) / Math.pow(10, decimals);
            return amount.toFixed(6);
        }

        function formatCompactUSD(value) {
            if (value >= 1000) return (value / 1000).toFixed(1) + 'k';
            if (value >= 100) return value.toFixed(0);
            if (value >= 10) return value.toFixed(1);
            if (value >= 1) return value.toFixed(2);
            return value.toFixed(3);
        }

        function getValueLevel(value, maxValue) {
            if (value === 0) return 'empty';
            const ratio = value / maxValue;
            if (ratio > 0.75) return 'level-4';
            if (ratio > 0.5) return 'level-3';
            if (ratio > 0.25) return 'level-2';
            return 'level-1';
        }

        function createHeatmap(data) {
            const chains = getAllChains(data);
            const activeAccountsCount = data.filter(acc => Object.keys(acc.ChainData).length > 0).length;
            const totalValue = calculateTotalValue(data);

            // Calculate max value per chain for color scaling
            const maxValuePerChain = {};
            chains.forEach(chain => {
                let max = 0;
                data.forEach(account => {
                    const tokens = account.ChainData[chain];
                    if (tokens) {
                        const total = calculateChainTotal(tokens);
                        if (total > max) max = total;
                    }
                });
                maxValuePerChain[chain] = max;
            });

            document.getElementById('totalAccounts').textContent = data.length;
            document.getElementById('activeAccounts').textContent = activeAccountsCount;
            document.getElementById('totalChains').textContent = chains.length;
            document.getElementById('totalValue').textContent = formatUSD(totalValue);

            let html = '<table><thead><tr>';
            html += '<th class=""id-col"">ID</th>';
            html += '<th class=""address-col"">Address</th>';
            
            chains.forEach(chain => {
                html += `<th title=""${chain}"">${chain.substring(0, 10)}</th>`;
            });
            
            html += '</tr></thead><tbody>';

            data.forEach(account => {
                html += '<tr>';
                html += `<td class=""id-cell"">${account.Id}</td>`;
                html += `<td class=""address-cell"" title=""${account.Address}"">${account.Address}</td>`;
                
                chains.forEach(chain => {
                    const tokens = account.ChainData[chain];
                    const hasBalance = tokens && tokens.length > 0;
                    const chainTotal = hasBalance ? calculateChainTotal(tokens) : 0;
                    const level = getValueLevel(chainTotal, maxValuePerChain[chain]);
                    const displayValue = chainTotal >= 1 ? formatCompactUSD(chainTotal) : '';
                    
                    html += '<td><div class=""cell-wrapper"">';
                    html += `<div class=""heatmap-cell ${level}"" 
                                 data-account-id=""${account.Id}""
                                 data-address=""${account.Address}""
                                 data-chain=""${chain}""
                                 data-tokens='${hasBalance ? JSON.stringify(tokens) : '[]'}'>`;
                    if (displayValue) {
                        html += `<span class=""cell-value"">${displayValue}</span>`;
                    }
                    html += '</div></div></td>';
                });
                
                html += '</tr>';
            });

            html += '</tbody></table>';
            
            document.getElementById('tableContainer').innerHTML = html;
            attachTooltipListeners();
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
                const amount = formatAmount(token.AmountRaw, token.Decimals);
                tokensHtml += `
                    <div class=""token-item"">
                        <div class=""token-left"">
                            <span class=""token-symbol"">${token.Symbol}</span>
                            <span class=""token-amount"">${amount}</span>
                        </div>
                        <span class=""token-value"">${formatUSD(token.ValueUSD)}</span>
                    </div>
                `;
            });

            tokensHtml += `<div class=""total-value"">Total: ${formatUSD(total)}</div>`;
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

        createHeatmap(treasuryData);
    </script>
</body>
</html>";
    }
}