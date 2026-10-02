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
            .Select(g => g.FirstOrDefault(p => p.Type is "deposit" or "staked") ?? g.First())
            .OrderBy(p => p.AccountId).ThenBy(p => p.Chain).ToList();
    }
}
