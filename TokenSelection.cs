namespace z3nSafe;

public static class TokenSelection
{
    public static string Key(int chainId, string address) => $"{chainId}:{address.ToLowerInvariant()}";
    public static bool IsNative(string address) =>
        address.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase) ||
        address.Equals("0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", StringComparison.OrdinalIgnoreCase);

    public sealed record Asset(int ChainId, string Address);
    public sealed record Target(int Id, string Wallet, string Chain, int ChainId, string Address, string Symbol, decimal ValueUSD)
    {
        public int Decimals { get; init; }
        public string PriceUSD { get; init; } = "0";
    }

    public static Dictionary<string, List<Jumper.TokenInfo>> ExecutionTokens(IEnumerable<Target> targets) =>
        targets.GroupBy(t => t.ChainId.ToString()).ToDictionary(g => g.Key, g => g.Select(t => new Jumper.TokenInfo {
            ChainId = t.ChainId, Address = t.Address, Symbol = t.Symbol, Decimals = t.Decimals,
            PriceUSD = t.PriceUSD, Amount = "0" }).ToList());

    public static List<Target> Plan(IEnumerable<HeatmapGenerator.AccountData> accounts,
        IEnumerable<Asset> assets, decimal threshold, IEnumerable<int>? accountIds = null, bool excludeStables = false)
    {
        var keys = assets.Select(a => Key(a.ChainId, a.Address)).ToHashSet();
        var selected = accountIds?.ToHashSet();
        return accounts.Where(a => selected == null || selected.Contains(a.Id)).SelectMany(account => account.ChainData.SelectMany(chain => chain.Value
            .Where(t => !IsNative(t.Address) && (!excludeStables || !BalanceMath.IsStable(t.PriceUSD)) && t.ValueUSD > threshold && keys.Contains(Key(t.ChainId, t.Address)))
            .Select(t => new Target(account.Id, account.Address, chain.Key, t.ChainId, t.Address, t.Symbol, t.ValueUSD) { Decimals = t.Decimals, PriceUSD = t.PriceUSD })))
            .DistinctBy(t => (t.Id, Key(t.ChainId, t.Address))).ToList();
    }
}
