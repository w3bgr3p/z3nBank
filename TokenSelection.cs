namespace z3nSafe;

public static class TokenSelection
{
    public static string Key(int chainId, string address) => $"{chainId}:{address.ToLowerInvariant()}";
    public static bool IsNative(string address) =>
        address.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase) ||
        address.Equals("0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", StringComparison.OrdinalIgnoreCase);

    public sealed record Asset(int ChainId, string Address);
    public sealed record Target(int Id, string Wallet, string Chain, int ChainId, string Address, string Symbol, decimal ValueUSD);

    public static List<Target> Plan(IEnumerable<HeatmapGenerator.AccountData> accounts,
        IEnumerable<Asset> assets, decimal threshold, IEnumerable<int>? accountIds = null)
    {
        var keys = assets.Select(a => Key(a.ChainId, a.Address)).ToHashSet();
        var selected = accountIds?.ToHashSet();
        return accounts.Where(a => selected == null || selected.Contains(a.Id)).SelectMany(account => account.ChainData.SelectMany(chain => chain.Value
            .Where(t => !IsNative(t.Address) && t.ValueUSD > threshold && keys.Contains(Key(t.ChainId, t.Address)))
            .Select(t => new Target(account.Id, account.Address, chain.Key, t.ChainId, t.Address, t.Symbol, t.ValueUSD))))
            .DistinctBy(t => (t.Id, Key(t.ChainId, t.Address))).ToList();
    }
}
