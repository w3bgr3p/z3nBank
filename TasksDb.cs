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
    
    
    public sealed record UpdateResult(int Processed, int Updated, int Failed, int Skipped, int CurrentId)
    {
        public int Total { get; init; }
        public int Unprocessed { get; init; }
        public bool Stopped { get; init; }
        public BalanceUpdateFailure[] Failures { get; init; } = [];
    }

    public static async Task<UpdateResult> UpdateDb(Db dbConnection, int dbRange = 1000,
        decimal minValue = 0.001m, Logger? log = null, Action<UpdateResult>? progress = null, Jumper? client = null,
        IReadOnlyCollection<int>? accountIds = null)
    {
        if (dbRange < 1 || minValue < 0) throw new ArgumentOutOfRangeException(nameof(dbRange));
        var targets = BalanceUpdateAccounts(dbRange, accountIds);
        var db = dbConnection;
        log ??= new Logger(true);
        using var ownedClient = client == null ? new Jumper(log) : null;
        var jumper = client ?? ownedClient!;
        Dictionary<int, string>? chainNames = null;
        var updated = 0;
        var failed = 0;
        var skipped = 0;
        var failures = new List<BalanceUpdateFailure>(); var stopped = false; var currentId = 0;
        var processed = 0;
        UpdateResult Report() => new(processed, updated, failed, skipped, currentId) {
            Total = targets.Length, Unprocessed = targets.Length - processed, Stopped = stopped, Failures = failures.ToArray() };
        foreach (var id in targets)
        {
            currentId = id; var stage = "Read wallet address";
            log._acc = id.ToString();
            try
            {
                var address = db.Get("evm", "_addresses", where: $"id = {id}", log: true, thrw: true)?.Trim();
                if (string.IsNullOrEmpty(address))
                {
                    skipped++;
                    continue;
                }
                if (chainNames == null) { stage = "Fetch chain metadata"; chainNames = await jumper.GetChainMapping(); }
                stage = "Fetch wallet balances";
                var bal = await jumper.GetBalances(address);
                stage = "Validate balance response";
                // Indexers can omit a chain after a swap. Keep checking contracts already known to the wallet.
                stage = "Read saved balance snapshot";
                foreach (var column in db.GetTableColumns("_treasury").Where(c => !c.Equals("id", StringComparison.OrdinalIgnoreCase)))
                {
                    var metadata = chainNames.FirstOrDefault(c => c.Value == column);
                    if (metadata.Key == 0 || bal.Balances.ContainsKey(metadata.Key.ToString())) continue;
                    var previous = db.Get(column, "_treasury", id: id, log: true, thrw: true);
                    if (!string.IsNullOrWhiteSpace(previous)) bal.Balances[metadata.Key.ToString()] =
                        JsonConvert.DeserializeObject<List<Jumper.TokenInfo>>(previous) ?? [];
                }
                var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var chain in bal.Balances)
                {
                    if (!int.TryParse(chain.Key, out var chainId) || !chainNames.TryGetValue(chainId, out var chainName))
                        throw new InvalidDataException($"No EVM metadata for returned chain {chain.Key}; keeping previous snapshot.");
                    stage = "Read saved balance snapshot";
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var known = new List<Jumper.TokenInfo>(chain.Value);
                    if (db.GetTableColumns("_treasury").Contains(chainName, StringComparer.Ordinal))
                    {
                        var saved = db.Get(chainName, "_treasury", id: id, log: true, thrw: true);
                        if (!string.IsNullOrWhiteSpace(saved)) known.AddRange(JsonConvert.DeserializeObject<List<Jumper.TokenInfo>>(saved) ?? []);
                    }
                    stage = $"Verify RPC balances ({chainName})";
                    var verified = known.Count == 0 ? new List<Jumper.TokenInfo>() :
                        await TreasuryRpcBalances.Read(new Nethereum.Web3.Web3(Rpc.Get(chainName)), chainId, address, known,
                            cancellation: timeout.Token);
                    var tokens = verified.Where(t => t.ValueUSD > minValue).ToList();
                    snapshot[chainName] = JsonConvert.SerializeObject(tokens);
                }
                stage = "Save balance snapshot";
                db.ReplaceTreasurySnapshot(id, snapshot);
                updated++;
                log.Send($"Balance snapshot saved: {snapshot.Count} chains | amounts: RPC | prices/discovery: LI.FI", "INFO");
                await Task.Delay(250);
            }
            catch (Exception ex)
            {
                failed++;
                var failure = BalanceUpdateFailure.Describe(id, stage, ex); failures.Add(failure);
                stopped = failure.StopsBatch;
                log.Send($"Balance update failed | stage: {stage} | code: {failure.Code} | {failure.Reason} | details: {failure.Details} | previous balances kept", "ERROR");
            }
            finally { processed++; progress?.Invoke(Report()); }
            if (stopped) break;
        }
        var result = Report();
        if (stopped) log.Send($"Balance update stopped due to a shared dependency failure | updated: {updated} | failed: {failed} | not attempted: {result.Unprocessed}", "ERROR");
        return result;
    }

    public static int[] BalanceUpdateAccounts(int maxId, IReadOnlyCollection<int>? accountIds)
    {
        if (maxId < 1) throw new ArgumentOutOfRangeException(nameof(maxId));
        if (accountIds == null || accountIds.Count == 0) return Enumerable.Range(1, maxId).ToArray();
        if (accountIds.Any(id => id < 1 || id > maxId)) throw new ArgumentOutOfRangeException(nameof(accountIds), "Selected account IDs must be within Max ID");
        return accountIds.Distinct().OrderBy(id => id).ToArray();
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
