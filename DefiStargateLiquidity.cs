using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiStargateLiquidity
{
    // Stargate's published v1 deployment registry. Pool-to-router links are checked on-chain.
    private static readonly Dictionary<string, (string Pool, string Router)> Markets = new() {
        ["base"] = ("0x4c80e24119cfb836cdf0a6b53dc23f04f7e652ca", "0x45f1a95a4d3f3836523f5c83673c797f4d4d263b"),
        ["op"] = ("0xd22363e3762ca7339569f3d33eade20127d5f98c", "0xb0d502e938ed5f4df2e681fe6e419ff29631d62b"),
        ["linea"] = ("0xaad094f6a75a14417d39f04e690fc216f080a41a", "0x2f6f07cdcf3588944bf4c42ac74ff24bf56e7590")
    };
    public static bool Supported(DefiPosition p) => p.Protocol == "Stargate" && p.Type == "deposit" &&
        Markets.TryGetValue(p.Chain, out var market) && market.Pool.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Stargate liquidity pool");
        var market = Markets[p.Chain];
        var pool = web3.Eth.GetContract(DefiBlackwing.Abi(("router", [], ["address"]), ("poolId", [], ["uint256"]),
            ("balanceOf", ["address"], ["uint256"])), market.Pool);
        if (!(await SwapExecution.Read(pool.GetFunction("router").CallAsync<string>())).Equals(market.Router, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stargate pool router does not match the deployment");
        var shares = await SwapExecution.Read(pool.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var amount = exact == null ? shares : BigInteger.Parse(exact);
        if (amount <= 0 || amount > shares) throw new InvalidOperationException("Stargate LP balance is empty or decreased");
        var poolId = await SwapExecution.Read(pool.GetFunction("poolId").CallAsync<BigInteger>());
        var tx = DefiActionAbi.Build(p.Wallet, market.Router, "instantRedeemLocal(uint16,uint256,address)", [poolId.ToString(), amount.ToString(), p.Wallet]);
        var redeemed = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx)), 0);
        if (redeemed <= 0) throw new InvalidOperationException("Stargate has no local redemption liquidity now");
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, amount.ToString(), prices);
    }
}
