using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

// compound-finance/comet: CometRewards.claim accrues and pays the owner, not the caller.
public static class DefiCometRewards
{
    private static readonly Dictionary<string, string> Controllers = new() {
        ["arb"] = "0x88730d254a2f7e6ac8388c3198afd694ba9f7fae",
        ["base"] = "0x123964802e6ababbe1bc9547d72ef1b69b00a6b1",
        ["matic"] = "0x45939657d1ca34a8fa39a924b71d28fe8431e581"
    };
    public static bool Supported(DefiPosition p) => p.Protocol == "Compound V3" && p.Type == "reward" &&
        Controllers.ContainsKey(p.Chain) && DefiScrollLending.IsCompound(p with { Type = "deposit" });
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Comet reward market");
        var controller = Controllers[p.Chain];
        var read = DefiActionAbi.Build(p.Wallet, controller, "rewardConfig(address)", [p.VaultAddress!]);
        var config = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(read));
        if (!DefiNftLiquidity.Address(config, 0).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Comet reward token does not match the position");
        var tx = DefiActionAbi.Build(p.Wallet, controller, "claim(address,address,bool)", [p.VaultAddress!, p.Wallet, "true"]);
        var key = "comet-claim:" + p.VaultAddress!.ToLowerInvariant();
        if (exact != null && key != exact) throw new InvalidDataException("Comet reward market changed");
        try { return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, key, prices); }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex) when (ex.Message.Contains("transfer amount exceeds balance", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Compound reward controller cannot fund this claim: its reward token balance is insufficient. Your accrued reward is retained; no claim was sent. " + SwapExecution.ErrorDetails(ex), ex);
        }
    }
}
