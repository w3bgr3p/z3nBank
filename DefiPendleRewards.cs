using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiPendleRewards
{
    public static bool Supported(DefiPosition p) => p.Protocol == "Pendle V2" && p.Chain == "arb" && p.Type == "reward" &&
        DefiVault.AddressValid(p.VaultAddress) && p.AdapterId == "pendle_liquidity";
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Pendle reward position");
        var market = web3.Eth.GetContract(DefiBlackwing.Abi(("getRewardTokens", [], ["address[]"])), p.VaultAddress!);
        var tokens = await SwapExecution.Read(market.GetFunction("getRewardTokens").CallAsync<List<string>>());
        if (!tokens.Contains(p.AssetAddress!, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Pendle market does not reward this token");
        var key = "pendle-claim:" + p.VaultAddress;
        if (exact != null && exact != key) throw new InvalidOperationException("Pendle reward market changed");
        var tx = DefiActionAbi.Build(p.Wallet, p.VaultAddress!, "redeemRewards(address)", [p.Wallet]);
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, key, prices);
    }
}
