using Nethereum.Web3;
using System.Numerics;

namespace z3nSafe;

public static class DefiPending
{
    // Read-only reconciliation. A failed RPC never erases a persisted request.
    public static async Task<bool> Exists(Web3 web3, DefiPosition p)
    {
        if ((await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != Rpc.ChainId(p.Chain))
            throw new InvalidDataException("Pending withdrawal RPC chain mismatch; saved request kept");
        async Task<string> Read(string target, string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, args)));
        if (DefiGmxMarkets.Supported(p)) return (await DefiGmxPending.Requests(web3, p)).Count > 0;
        if (DefiLido.Supported(p)) {
            var ids = await SwapExecution.Read(web3.Eth.GetContract(DefiBlackwing.Abi(("getWithdrawalRequests", ["address"], ["uint256[]"])), DefiLido.Queue)
                .GetFunction("getWithdrawalRequests").CallAsync<List<BigInteger>>(p.Wallet));
            return ids.Count > 0;
        }
        if (DefiStader.Supported(p)) {
            var data = await Read(DefiStader.Pool, "getUserMaticXSwapRequests(address)", p.Wallet);
            return DefiLending.Word(data, checked((int)DefiLending.Word(data, 0) / 32)) > 0;
        }
        if (DefiSonne.Supported(p) && p.Type != "reward") return DefiLending.Word(await Read(DefiSonne.Staking, "withdrawal(address)", p.Wallet), 0) > 0;
        if (DefiLayerBankRewards.IsSupported(p)) {
            var earned = await Read(DefiLayerBankRewards.Rewards, "earnedBalances(address)", p.Wallet);
            return DefiLending.Word(earned, 0) + DefiLending.Word(earned, 1) > 0;
        }
        if (DefiSeamlessRewards.Supported(p) && p.AssetAddress?.Equals(DefiSeamlessRewards.Escrow, StringComparison.OrdinalIgnoreCase) == true)
            return DefiLending.Word(await Read(DefiSeamlessRewards.Escrow, "balanceOf(address)", p.Wallet), 0) > 0;
        return true;
    }
}
