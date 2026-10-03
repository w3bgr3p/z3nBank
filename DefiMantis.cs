using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// Verified Mantissa Mode Pool/LP deployments, matching its public application configuration.
public static class DefiMantis
{
    public const string Pool = "0x4af97f73343b226c5a5872dcd2d1c4944bdb3e77";
    public const string Lp = "0x967f594f73930a02817daf3112ccc7f2c611def8";
    public static bool Supported(DefiPosition p) => p.Protocol == "MantisSwap" && p.Chain == "mode" && p.Type == "deposit" &&
        Lp.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Mantis pool");
        var lp = web3.Eth.GetContract(DefiBlackwing.Abi(("underlier", [], ["address"]), ("balanceOf", ["address"], ["uint256"])), Lp);
        if (!(await SwapExecution.Read(lp.GetFunction("underlier").CallAsync<string>())).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mantis LP underlier mismatch");
        var pool = web3.Eth.GetContract(DefiBlackwing.Abi(("tokenLPs", ["address"], ["address"]),
            ("getWithdrawAmount", ["address", "uint256", "bool"], ["uint256", "uint256", "uint256"])), Pool);
        if (!(await SwapExecution.Read(pool.GetFunction("tokenLPs").CallAsync<string>(p.AssetAddress!))).Equals(Lp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mantis pool-to-LP link mismatch");
        var balance = await SwapExecution.Read(lp.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var state = exact == null ? null : JObject.Parse(exact);
        var amount = state == null ? balance : BigInteger.Parse((string)state["amount"]!);
        if (amount <= 0 || amount > balance) throw new InvalidOperationException("Mantis LP balance is empty or decreased");
        var outputCall = pool.GetFunction("getWithdrawAmount").CreateCallInput(Lp, amount, false);
        var output = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(outputCall));
        var received = DefiLending.Word(output, 0) - DefiLending.Word(output, 1);
        if (received <= 0) throw new InvalidOperationException("Mantis has no redeemable underlying liquidity");
        state ??= new JObject { ["amount"] = amount.ToString(), ["minimum"] = (received * 995 / 1000).ToString(),
            ["deadline"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200 };
        if ((long)state["deadline"]! <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidOperationException("Mantis preview expired");
        var minimum = BigInteger.Parse((string)state["minimum"]!);
        if (received < minimum) throw new InvalidOperationException("Mantis output decreased below the confirmed minimum");
        var tx = DefiActionAbi.Build(p.Wallet, Pool, "withdraw(address,address,uint256,uint256,uint256)",
            [p.AssetAddress!, p.Wallet, amount.ToString(), minimum.ToString(), state["deadline"]!.ToString()]);
        // LP.burnFrom subtracts allowance(owner, pool), even when the pool burns its own LP.
        var action = new RabbyWithdrawAction { Approval = new JObject {
            ["token_id"] = Lp, ["to"] = Pool, ["str_raw_amount"] = amount.ToString()
        } };
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx,
            state.ToString(Newtonsoft.Json.Formatting.None), prices, action: action);
    }
}
