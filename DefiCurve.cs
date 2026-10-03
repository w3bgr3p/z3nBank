using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// Verified Curve pools observed in the portfolio; remove_liquidity burns the live LP balance.
public static class DefiCurve
{
    public static bool Supported(DefiPosition p) => p.Protocol == "Curve" && p.Type is "deposit" or "unknown" &&
        DefiActionTrust.CurvePools.ContainsKey(p.Chain + ":" + p.VaultAddress?.ToLowerInvariant());
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Curve pool");
        var pool = DefiActionTrust.CurvePools[p.Chain + ":" + p.VaultAddress!.ToLowerInvariant()];
        var lp = web3.Eth.GetContract(DefiBlackwing.Abi(("balanceOf", ["address"], ["uint256"]), ("totalSupply", [], ["uint256"])), p.VaultAddress);
        var balance = await SwapExecution.Read(lp.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var state = exact == null ? null : JObject.Parse(exact);
        var amount = state == null ? balance : BigInteger.Parse((string)state["amount"]!);
        if (amount <= 0 || amount > balance) throw new InvalidOperationException("Curve LP balance is empty or decreased");
        if ((string?)state?["mode"] == "eth-only") return await EthOnly(web3, chainId, p, gasPercent, pool, amount, state, prices);
        var supply = await SwapExecution.Read(lp.GetFunction("totalSupply").CallAsync<BigInteger>());
        if (supply <= 0) throw new InvalidOperationException("Curve pool has no active supply");
        var count = p.Chain == "matic" || p.VaultAddress.Equals("0xc4ad29ba4b3c580e6d59105fff484999997675ff", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("balances", ["uint256"], ["uint256"])), pool);
        var minima = new List<string>();
        for (int i = 0; i < count; i++) {
            var reserve = await SwapExecution.Read(contract.GetFunction("balances").CallAsync<BigInteger>(new BigInteger(i)));
            var available = reserve * amount / supply;
            var minimum = state == null ? available * 995 / 1000 : BigInteger.Parse(state["minima"]![i]!.ToString());
            if (available < minimum) throw new InvalidOperationException("Curve output decreased below the confirmed minimum");
            minima.Add(minimum.ToString());
        }
        state ??= new JObject { ["amount"] = amount.ToString(), ["minima"] = new JArray(minima) };
        var tx = DefiActionAbi.Build(p.Wallet, pool, $"remove_liquidity(uint256,uint256[{count}])", [amount.ToString(), state["minima"]!.ToString()]);
        try { return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, state.ToString(Newtonsoft.Json.Formatting.None), prices); }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex) {
            if (exact == null && pool.Equals("0xc5424b857f758e906013f3555dad202e4bdb4567", StringComparison.OrdinalIgnoreCase))
                return await EthOnly(web3, chainId, p, gasPercent, pool, amount, null, prices);
            // Diagnose transfer restrictions without changing state. Some legacy pools contain
            // suspended Synthetix assets; their LP balance exists but removal cannot transfer coins.
            for (int i = 0; i < count; i++) {
                try {
                    var coin = DefiNftLiquidity.Address(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(
                        DefiActionAbi.Build(p.Wallet, pool, "coins(uint256)", [i.ToString()]))), 0);
                    if (TokenSelection.IsNative(coin) || BigInteger.Parse(minima[i]) == 0) continue;
                    var transfer = DefiActionAbi.Build(pool, coin, "transfer(address,uint256)", [p.Wallet, minima[i]]);
                    await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(transfer));
                }
                catch (Nethereum.JsonRpc.Client.RpcResponseException transferError) {
                    throw new InvalidOperationException($"Curve pool coin #{i + 1} cannot be transferred out by the pool. The underlying token may be suspended or require settlement. Your LP remains intact; no withdrawal was sent. " + SwapExecution.ErrorDetails(transferError), ex);
                }
            }
            throw new InvalidOperationException("Curve rejected the simulated pool exit; no LP was burned. " + SwapExecution.ErrorDetails(ex), ex);
        }
    }
    private static async Task<DefiVault.Quote> EthOnly(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string pool, BigInteger amount, JObject? state, HttpClient? prices)
    {
        if (chainId != 1 || !pool.Equals("0xc5424b857f758e906013f3555dad202e4bdb4567", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unverified Curve single-coin exit");
        var call = DefiActionAbi.Build(p.Wallet, pool, "calc_withdraw_one_coin(uint256,int128)", [amount.ToString(), "0"]);
        var output = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(call)), 0);
        state ??= new JObject { ["mode"] = "eth-only", ["amount"] = amount.ToString(), ["minimum"] = (output * 995 / 1000).ToString() };
        var minimum = BigInteger.Parse((string)state["minimum"]!);
        if (output <= 0 || output < minimum) throw new InvalidOperationException("Curve ETH output fell below the confirmed minimum");
        var tx = DefiActionAbi.Build(p.Wallet, pool, "remove_liquidity_one_coin(uint256,int128,uint256)", [amount.ToString(), "0", minimum.ToString()]);
        return (await DefiVault.PrepareTransaction(web3, chainId, "0x0000000000000000000000000000000000000000", output, tx, boost, prices)) with {
            InputAmountRaw = state.ToString(Newtonsoft.Json.Formatting.None),
            Notice = "Exits this sETH pool into ETH only, avoiding transfer of its suspended synth. A 0.5% minimum ETH output is enforced on-chain."
        };
    }
}
