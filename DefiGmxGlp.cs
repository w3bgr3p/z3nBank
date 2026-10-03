using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// GMX's published Arbitrum v1 GLP deployment and RewardRouterV2 redemption/fee claim paths.
public static class DefiGmxGlp
{
    public const string Tracker = "0x1addd80e6039594ee970e5872d247bf0414c8903";
    public const string FeeTracker = "0x4e971a87900b931ff39d1aad67697f49835400b6";
    public const string Router = "0xb95db5b167d75e6d04227cfffa61069348d271f5";
    public const string RewardRouter = "0x5e4766f932ce00aa4a1a82d3da85adf15c5694a1";
    public const string Glp = "0x4277f8f2c384827b5273592ff7cebd9f2c1ac258";
    public static bool Supported(DefiPosition p) => p.Protocol == "GMX" && p.Chain == "arb" && p.Type is "staked" or "reward" &&
        Tracker.Equals(p.VaultAddress?.Split(':')[0], StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified GMX GLP staking market");
        if (p.Type == "reward") {
            var key = "glp-fees";
            if (exact != null && exact != key) throw new InvalidOperationException("GMX reward action changed");
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent,
                DefiActionAbi.Build(p.Wallet, RewardRouter, "claimFees()", []), key, prices);
        }
        var tracker = web3.Eth.GetContract(DefiBlackwing.Abi(("depositBalances", ["address", "address"], ["uint256"])), FeeTracker);
        var balance = await SwapExecution.Read(tracker.GetFunction("depositBalances").CallAsync<BigInteger>(p.Wallet, Glp));
        var state = exact == null ? null : JObject.Parse(exact);
        var amount = state == null ? balance : BigInteger.Parse((string)state["amount"]!);
        if (amount <= 0 || amount > balance) throw new InvalidOperationException("No staked GLP remains, or the balance decreased");
        var output = BigInteger.Zero; string? asset = (string?)state?["asset"]; var errors = new List<string>();
        var candidates = asset != null ? new[] { asset } : new[] { p.AssetAddress!, "0xaf88d065e77c8cc2239327c5edb3a432268e5831", "0xff970a61a04b1ca14834a43f5de4533ebddb5cc8", "0xfd086bc7cd5c481dcc9c85ebe478a1c0b69fcbb9", "0x82af49447d8a07e3bd95bd0d56f35241523fbab1" }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates) {
            if (!DefiVault.AddressValid(candidate) || TokenSelection.IsNative(candidate)) continue;
            var probe = DefiActionAbi.Build(p.Wallet, Router, "unstakeAndRedeemGlp(address,uint256,uint256,address)", [candidate, amount.ToString(), "0", p.Wallet]);
            try { output = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(probe)), 0); if (output > 0) { asset = candidate; break; } }
            catch (Nethereum.JsonRpc.Client.RpcResponseException ex) { errors.Add(candidate + ": " + ex.Message); }
        }
        if (output <= 0 || asset == null) throw new InvalidOperationException("GMX GLP redemption is blocked by the current protocol reserves. " + string.Join("; ", errors));
        state ??= new JObject { ["asset"] = asset, ["amount"] = amount.ToString(), ["minimum"] = (output * 995 / 1000).ToString() };
        var minimum = BigInteger.Parse((string)state["minimum"]!);
        if (output < minimum) throw new InvalidOperationException("GLP redemption output dropped below the confirmed minimum");
        var tx = DefiActionAbi.Build(p.Wallet, Router, "unstakeAndRedeemGlp(address,uint256,uint256,address)", [asset, amount.ToString(), minimum.ToString(), p.Wallet]);
        return (await DefiVault.PrepareTransaction(web3, chainId, asset, output, tx, gasPercent, prices)) with {
            InputAmountRaw = state.ToString(Newtonsoft.Json.Formatting.None), Notice = "Redeems the complete GLP position into the selected available reserve token. Its indexed basket rows are parts of one GLP holding."
        };
    }
}
