namespace z3nSafe;
using z3n;
using Newtonsoft.Json;
public class TasksDb
{
    
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
    
    
    public static async Task UpdateDb(Db dbConnection, int dbRange = 1000, decimal minValue = 0.001m, Logger log = null)
    {
        var id = 0;
        var db = dbConnection;
        log = log ?? new Logger(true);
        var jumper = new Jumper(log);
        var chainNames = await jumper.GetChainMapping();

        while (id <= dbRange)
        {
            id++;
            log._acc = id.ToString();
            await Task.Delay(108);

            var address = db.Get("evm", "_addresses", where: $"id = {id}");
            var bal = await jumper.GetBalances(address);

            if (bal?.Balances == null)
            {
                Console.WriteLine($"--- {id}: {address} | NO DATA ---");
                continue;
            }

            bool hasAnyTokens = false;

            foreach (var chain in bal.Balances)
            {
                if (!int.TryParse(chain.Key, out int chainIdInt))
                {
                    continue;
                }

                string chainName = chainNames.ContainsKey(chainIdInt)
                    ? chainNames[chainIdInt]
                    : $"Unknown_{chain.Key}";

                var tokensInChain = chain.Value
                    .Where(t => t.ValueUSD > minValue)
                    .ToList();

                if (tokensInChain.Any())
                {
                    hasAnyTokens = true;
                    string accountChainJson = JsonConvert.SerializeObject(tokensInChain, Formatting.Indented);

                    Console.WriteLine($"--- {id}: {address} | Chain: {chainName} ---");
                    Console.WriteLine(accountChainJson);

                    db.AddColumn($"{chainName}", "_treasury");
                    db.Upd($"{chainName} = '{accountChainJson}'", "_treasury", where: $"id = {id}");

                    log?.Send(accountChainJson, $"{id}_{address.Substring(address.Length - 4)}_{chainName}");
                }
            }

            if (!hasAnyTokens)
            {
                Console.WriteLine($"--- {id}: {address} | EMPTY (all < {minValue} USD) ---");
            }
        }
    }
    
    public async Task<List<AccountData>> GetTreasuryData(Db _db, int maxId = 1000, List<string> selectedChains = null)
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

    
}