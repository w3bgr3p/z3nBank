using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiLayerBankRewards
{
    public const string Lab = "0x2a00647f45047f05bded961eb8ecabc42780e604";
    public const string Distributor = "0xf1f897601a525f57c5ea751a1f3ec5f9adac0321";
    public const string Rewards = "0x20112c6128550e13600d52200a5f185c6aae4e42";
    public static bool IsSupported(DefiPosition p) => DefiScrollLending.IsLayerBank(p) && p.Type == "reward" &&
        string.Equals(p.AssetAddress, Lab, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!IsSupported(p) || chainId != 534352 || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidOperationException("LayerBank reward contract or RPC chain does not match");
        var core = web3.Eth.GetContract(DefiBlackwing.Abi(("labDistributor", [], ["address"])), DefiScrollLending.Core);
        var dist = web3.Eth.GetContract(DefiBlackwing.Abi(("LAB", [], ["address"]), ("core", [], ["address"]),
            ("rewardController", [], ["address"])), Distributor);
        var rewards = web3.Eth.GetContract(DefiBlackwing.Abi(("LAB", [], ["address"]), ("labDistributor", [], ["address"]),
            ("earnedBalances", ["address"], ["uint256", "uint256", "bytes"]), ("withdraw", ["uint256"], [])), Rewards);
        async Task Verify(Nethereum.Contracts.Contract contract, string getter, string expected)
        {
            if (!string.Equals(await SwapExecution.Read(contract.GetFunction(getter).CallAsync<string>()), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LayerBank reward contract identity changed; withdrawal blocked");
        }
        await Verify(core, "labDistributor", Distributor); await Verify(dist, "LAB", Lab);
        await Verify(dist, "core", DefiScrollLending.Core); await Verify(dist, "rewardController", Rewards);
        await Verify(rewards, "LAB", Lab); await Verify(rewards, "labDistributor", Distributor);
        // Read the first two ABI words; the dynamic third field contains locked earnings and is not withdrawn.
        var earned = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(rewards.GetFunction("earnedBalances").CreateCallInput(p.Wallet)));
        var locked = DefiLending.Word(earned, 0); var unlocked = DefiLending.Word(earned, 1);
        if (unlocked <= 0) throw new InvalidOperationException("No unlocked LAB.s rewards available. The displayed amount is a provider reward estimate. Claiming accrued rewards starts vesting; early exit can charge a penalty. This check does not claim, lock or burn tokens." +
            (locked > 0 ? $" Locked reward balance: {Web3.Convert.FromWei(locked)} LAB.s." : ""));
        var amount = exact == null ? unlocked : BigInteger.Parse(exact);
        if (amount <= 0 || amount > unlocked) throw new InvalidOperationException("Unlocked LAB.s rewards decreased; request a new preview");
        return await DefiVault.PrepareTransaction(web3, chainId, Lab, amount,
            rewards.GetFunction("withdraw").CreateTransactionInput(p.Wallet, amount), gasPercent, prices);
    }
}
