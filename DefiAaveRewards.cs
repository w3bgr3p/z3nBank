using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiAaveRewards
{
    public static bool Supported(DefiPosition p) => p.Type == "reward" &&
        (p.Protocol == "Aave" && p.Chain == "matic" || p.Protocol == "Aave V3" && p.Chain == "metis") &&
        DefiLending.IsAave(p with { Type = "deposit" });
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Aave incentive controller");
        var v2 = p.Protocol == "Aave";
        var controller = v2 ? "0x357d51124f59836ded84c8a1730d72b749d8bc23" : "0x30c1b8f0490fa0908863d6cbd2e36400b4310a6b";
        var pool = web3.Eth.GetContract(DefiBlackwing.Abi(("getReservesList", [], ["address[]"])), p.VaultAddress!);
        var reserves = await SwapExecution.Read(pool.GetFunction("getReservesList").CallAsync<List<string>>());
        var receipts = new List<string>();
        foreach (var asset in reserves) {
            var call = DefiActionAbi.Build(p.Wallet, p.VaultAddress!, "getReserveData(address)", [asset]);
            var receipt = DefiNftLiquidity.Address(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(call)), v2 ? 7 : 8);
            // Rewards remain claimable after a deposit has been withdrawn.
            if (DefiVault.AddressValid(receipt) && !TokenSelection.IsNative(receipt)) receipts.Add(receipt);
        }
        if (receipts.Count == 0) throw new InvalidOperationException("No incentive-bearing Aave deposits remain");
        var args = new JArray(receipts).ToString(Newtonsoft.Json.Formatting.None);
        var tx = DefiActionAbi.Build(p.Wallet, controller, v2 ? "claimRewards(address[],uint256,address)" : "claimAllRewardsToSelf(address[])",
            v2 ? [args, ((BigInteger.One << 256) - 1).ToString(), p.Wallet] : [args]);
        var key = "aave-claim:" + args;
        if (exact != null && exact != key) throw new InvalidDataException("Aave reward assets changed; check withdrawal again");
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, key, prices);
    }
}
