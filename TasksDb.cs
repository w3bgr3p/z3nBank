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
        public decimal ValueUSD => BalanceMath.GetValueUsd(Amount, Decimals, PriceUSD);
    }
    
    
    public sealed record UpdateResult(int Processed, int Updated, int Failed, int Skipped, int CurrentId);

    public static async Task<UpdateResult> UpdateDb(Db dbConnection, int dbRange = 1000,
        decimal minValue = 0.001m, Logger? log = null, Action<UpdateResult>? progress = null, Jumper? client = null)
    {
        if (dbRange < 1 || minValue < 0) throw new ArgumentOutOfRangeException(nameof(dbRange));
        var db = dbConnection;
        log ??= new Logger(true);
        using var ownedClient = client == null ? new Jumper(log) : null;
        var jumper = client ?? ownedClient!;
        var chainNames = await jumper.GetChainMapping();
        var updated = 0;
        var failed = 0;
        var skipped = 0;
        for (var id = 1; id <= dbRange; id++)
        {
            log._acc = id.ToString();
            try
            {
                var address = db.Get("evm", "_addresses", where: $"id = {id}", log: true)?.Trim();
                if (string.IsNullOrEmpty(address))
                {
                    skipped++;
                    continue;
                }
                var bal = await jumper.GetBalances(address);
                var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var chain in bal.Balances)
                {
                    if (!int.TryParse(chain.Key, out var chainId) || !chainNames.TryGetValue(chainId, out var chainName))
                        throw new InvalidDataException($"No EVM metadata for returned chain {chain.Key}; keeping previous snapshot.");
                    var tokens = chain.Value.Where(t => t.ValueUSD > minValue)
                        .GroupBy(t => t.Address, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                    snapshot[chainName] = JsonConvert.SerializeObject(tokens);
                }
                db.ReplaceTreasurySnapshot(id, snapshot);
                updated++;
                log.Send($"Balance snapshot saved: {snapshot.Count} chains", "INFO");
                await Task.Delay(250);
            }
            catch (Exception ex)
            {
                failed++;
                log.Send($"Balance update failed; previous data kept: {ex.Message}", "ERROR");
            }
            finally { progress?.Invoke(new UpdateResult(id, updated, failed, skipped, id)); }
        }
        return new UpdateResult(dbRange, updated, failed, skipped, dbRange);
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
                        tokens = tokens?.Where(t => t.ChainId > 0 && t.ValueUSD > 0).ToList();
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
