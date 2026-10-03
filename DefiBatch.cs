namespace z3nSafe;

public static class DefiBatch
{
    public static List<DefiPosition> Select(IEnumerable<DefiPosition> rows, string protocol, IEnumerable<int> accounts)
    {
        var selected = accounts.ToHashSet();
        var candidates = rows.Where(p => selected.Contains(p.AccountId) && p.Protocol == protocol).ToList();
        // A complete NFT exit already collects its fees and farm rewards. Keep separate reward actions
        // when no full exit is selected, and never collapse different NFT IDs in the same AMM pool.
        bool SameNft(DefiPosition a, DefiPosition b) { try { return DefiNftLiquidity.TokenId(a) == DefiNftLiquidity.TokenId(b); } catch { return false; } }
        candidates = candidates.Where(p => !DefiNftLiquidity.Supported(p) || p.Type != "reward" ||
            !candidates.Any(d => d.AccountId == p.AccountId && d.Chain == p.Chain && d.Type == "deposit" &&
                DefiNftLiquidity.Supported(d) && SameNft(d, p))).ToList();
        return candidates
            .GroupBy(p => (p.AccountId, p.Chain, Action: ActionKey(p)))
            .Select(g => g.FirstOrDefault(p => p.Type is "deposit" or "staked") ?? g.First())
            .OrderBy(p => p.AccountId).ThenBy(p => p.Chain).ToList();
    }
    private static string ActionKey(DefiPosition p)
    {
        if (DefiNftLiquidity.Supported(p)) {
            try { return "nft:" + p.Protocol + ":" + DefiNftLiquidity.TokenId(p) + ":" + p.Type; }
            catch { return p.Id; }
        }
        if (DefiWithdrawal.UsesRabby(p))
        {
            try { var tx = DefiRabbyActions.Build(p); return tx.To.ToLowerInvariant() + ":" + tx.Data.ToLowerInvariant(); }
            catch { return p.Id; }
        }
        var asset = DefiWithdrawal.IsGate(p) || DefiLending.IsAave(p) || DefiCompoundV2.Supported(p) || DefiBlackwing.IsSupported(p) ||
            DefiScrollLending.IsLayerBank(p) || DefiScrollLending.IsCompound(p) || DefiMerkl.Supported(p) || DefiSablier.Supported(p) ||
            DefiSeamlessRewards.Supported(p) || DefiSonne.Supported(p) && p.Type == "reward" ? p.AssetAddress?.ToLowerInvariant() : null;
        var phase = p.Type == "reward" ? "reward" : "withdraw";
        return (p.VaultAddress?.ToLowerInvariant() ?? p.GroupId) + ":" + phase + ":" + asset;
    }
}
