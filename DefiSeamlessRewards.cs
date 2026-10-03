using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiSeamlessRewards
{
    public const string Pool = "0x8f44fd754285aa6a2b8b9b97739b79746e0475a7";
    public const string Controller = "0x91ac2fff8cbef5859eaa6dda661febd533cd3780";
    public const string Escrow = "0x998e44232bef4f8b033e5a5175bdc97f2b10d5e5";
    public static bool Supported(DefiPosition p) => p.Chain == "base" && p.Protocol == "Seamless Protocol" && p.Type == "reward" && Pool.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, args)));
        var claimable = DefiLending.Word(await Read(Escrow, "getClaimableAmount(address)", p.Wallet), 0);
        if (p.AssetAddress!.Equals(Escrow, StringComparison.OrdinalIgnoreCase) && claimable > 0) {
            var key = "vested:" + Escrow;
            if (exact != null && exact != key) throw new InvalidOperationException("Vested SEAM changed; preview again");
            var tx = DefiActionAbi.Build(p.Wallet, Escrow, "claim(address)", [p.Wallet]);
            var remaining = DefiLending.Word(await Read(Escrow, "balanceOf(address)", p.Wallet), 0);
            return (await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, key, prices)) with {
                CompletesPending = claimable >= remaining, Notice = "Claims currently vested SEAM. Any remaining vesting credit stays in the protocol and can be claimed as it unlocks."
            };
        }
        var pool = web3.Eth.GetContract(DefiBlackwing.Abi(("getReservesList", [], ["address[]"])), Pool);
        var reserves = await SwapExecution.Read(pool.GetFunction("getReservesList").CallAsync<List<string>>());
        var receipts = new JArray();
        foreach (var token in reserves) {
            var data = await Read(Pool, "getReserveData(address)", token);
            // Include both supplied and borrowed assets: rewards survive a zero current balance.
            foreach (int i in new[] { 8, 10 }) {
                var receipt = DefiNftLiquidity.Address(data, i);
                if (DefiVault.AddressValid(receipt) && !TokenSelection.IsNative(receipt)) receipts.Add(receipt);
            }
        }
        var keyClaim = "reward:" + p.AssetAddress;
        if (exact != null && exact != keyClaim) throw new InvalidDataException("Seamless reward token changed");
        var claim = DefiActionAbi.Build(p.Wallet, Controller, "claimRewardsToSelf(address[],uint256,address)",
            [receipts.ToString(Newtonsoft.Json.Formatting.None), ((BigInteger.One << 256) - 1).ToString(), p.AssetAddress]);
        try {
            if (p.AssetAddress.Equals(Escrow, StringComparison.OrdinalIgnoreCase)) {
                var accrued = DefiLending.Word(await Read(Controller, "getUserRewards(address[],address,address)",
                    receipts.ToString(), p.Wallet, Escrow), 0);
                if (accrued <= 0) throw new InvalidOperationException("No unclaimed esSEAM reward or vested SEAM remains. Existing esSEAM is locked until its vesting schedule releases SEAM.");
                var seam = DefiNftLiquidity.Address(await Read(Escrow, "seam()"), 0);
                return (await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, claim, keyClaim, prices,
                    futureOutputs: new[] { (seam, accrued) })) with { Stage = "request", Notice = "Claims esSEAM vesting credit. It cannot be transferred or swapped; SEAM unlocks gradually and must be claimed later from this position." };
            }
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, claim, keyClaim, prices);
        } catch (Nethereum.JsonRpc.Client.RpcResponseException ex) {
            throw new InvalidOperationException("Seamless reward distributor rejected its claim on-chain. Reward funding or its transfer strategy is unavailable; no transaction was sent. " + ex.Message, ex);
        }
    }
}
