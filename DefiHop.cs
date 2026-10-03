using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// Hop's official Polygon USDT pool. The entire unstake -> LP approval -> pool exit is simulated together.
public static class DefiHop
{
    public const string Staking = "0x297e5079df8173ae1696899d3eacd708f0af82ce";
    public const string Lp = "0x3ca3218d6c52b640b0857cc19b69aa9427bc842c";
    public const string Pool = "0xb2f7d27b21a69a033f85c42d5eb079043baadc81";
    public static bool Supported(DefiPosition p) => p.Protocol == "Hop Protocol" && p.Chain == "matic" && Staking.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, method, args)));
        if (!Lp.Equals(DefiNftLiquidity.Address(await Read(Staking, "stakingToken()"), 0), StringComparison.OrdinalIgnoreCase) ||
            !Pool.Equals(DefiNftLiquidity.Address(await Read(Lp, "owner()"), 0), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Hop staking token or pool changed");
        if (p.Type == "reward") {
            if (exact != null && exact != "hop-rewards") throw new InvalidDataException("Hop claim action changed");
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, DefiActionAbi.Build(p.Wallet, Staking, "getReward()", []), "hop-rewards", prices);
        }
        var stake = DefiLending.Word(await Read(Staking, "balanceOf(address)", p.Wallet), 0);
        var walletLp = DefiLending.Word(await Read(Lp, "balanceOf(address)", p.Wallet), 0);
        var plan = exact == null ? null : JObject.Parse(exact);
        var amount = plan == null ? stake + walletLp : BigInteger.Parse((string)plan["amount"]!);
        if (amount <= 0 || amount != stake + walletLp) throw new InvalidOperationException("Hop LP amount changed; preview again");
        var output = DefiLending.Word(await Read(Pool, "calculateRemoveLiquidityOneToken(address,uint256,uint8)", p.Wallet, amount.ToString(), "0"), 0);
        var token = DefiNftLiquidity.Address(await Read(Pool, "getToken(uint8)", "0"), 0);
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        plan ??= new JObject { ["amount"] = amount.ToString(), ["minimum"] = (output * 995 / 1000).ToString(), ["deadline"] = (block.Timestamp.Value + 1200).ToString() };
        if (BigInteger.Parse((string)plan["deadline"]!) <= block.Timestamp.Value) throw new InvalidOperationException("Hop preview expired; preview again");
        var steps = new List<DefiVault.Approval>();
        async Task Step(TransactionInput tx, string purpose) {
            var gas = (await SwapExecution.Read(web3.Eth.Transactions.EstimateGas.SendRequestAsync(tx))).Value;
            var pricing = await DefiFees.Pricing(web3, chainId, tx, gas, boost); gas = (pricing.Gas * 110 + 99) / 100;
            var l1 = await DefiFees.L1Fee(web3, chainId, tx, gas, pricing.Price);
            steps.Add(new(tx.To, tx.Data, gas.ToString(), pricing.Price.ToString(), l1.ToString()) { Purpose = purpose });
        }
        if (stake > 0) await Step(DefiActionAbi.Build(p.Wallet, Staking, "exit()", []), "unstake Hop LP and claim HOP");
        var allowance = DefiLending.Word(await Read(Lp, "allowance(address,address)", p.Wallet, Pool), 0);
        if (allowance < amount) await Step(DefiActionAbi.Build(p.Wallet, Lp, "approve(address,uint256)", [Pool, amount.ToString()]), "approval");
        var withdraw = DefiActionAbi.Build(p.Wallet, Pool, "removeLiquidityOneToken(uint256,uint8,uint256,uint256)",
            [amount.ToString(), "0", (string)plan["minimum"]!, (string)plan["deadline"]!]);
        return (await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, withdraw, plan.ToString(Newtonsoft.Json.Formatting.None), prices,
            preparedSteps: steps)) with { Notice = "Unstakes the full Hop LP balance, claims HOP, then redeems all LP into canonical USDT with a minimum output. The total fee includes every transaction in this sequence." };
    }
}
