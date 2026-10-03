namespace z3nSafe;

public static class DefiBatch
{
    public static List<DefiPosition> Select(IEnumerable<DefiPosition> rows, string protocol, IEnumerable<int> accounts)
    {
        var selected = accounts.ToHashSet();
        return rows.Where(p => selected.Contains(p.AccountId) && p.Protocol == protocol)
            .GroupBy(p => (p.AccountId, p.Chain, Contract: p.VaultAddress?.ToLowerInvariant() ?? p.GroupId,
                Asset: DefiWithdrawal.IsGate(p) || DefiLending.IsAave(p) || DefiBlackwing.IsSupported(p) ||
                    DefiScrollLending.IsLayerBank(p) || DefiScrollLending.IsCompound(p) ? p.AssetAddress?.ToLowerInvariant() : null))
            .SelectMany(g => g.Any(DefiWithdrawal.UsesRabby) ? g.Where(p => p.Type != "loan") :
                new[] { g.FirstOrDefault(p => p.Type is "deposit" or "staked") ?? g.First() })
            .GroupBy(p => (p.AccountId, p.Chain, Action: ActionKey(p)))
            .Select(g => g.First())
            .OrderBy(p => p.AccountId).ThenBy(p => p.Chain).ToList();
    }
    private static string ActionKey(DefiPosition p)
    {
        if (DefiWithdrawal.UsesRabby(p))
        {
            try { var tx = DefiRabbyActions.Build(p); return tx.To.ToLowerInvariant() + ":" + tx.Data.ToLowerInvariant(); }
            catch { return p.Id; }
        }
        return p.Id;
    }
}
