using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiJoeLiquidity
{
    public const string Pair = "0x53eccccaaa368a9431b3659a8e37ce4b411ad258";
    public static bool Supported(DefiPosition p) => p.Chain == "arb" && p.Protocol == "LFJ" && p.Type == "deposit" && Pair.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, Pair, method, args)));
        JObject? plan = exact == null ? null : JObject.Parse(exact);
        JArray ids;
        if (plan != null) ids = (JArray)plan["ids"]!;
        else {
            using var client = new DefiPositionsClient();
            var rows = await client.ReadAsync(p.AccountId, p.Wallet, SwapExecution.Token);
            var current = rows.FirstOrDefault(r => Supported(r)) ?? throw new InvalidOperationException("LFJ liquidity no longer appears in the wallet");
            var action = current.WithdrawActions.SingleOrDefault(a => DefiActionAbi.Signature(a.Function).Name == "removeLiquidity");
            if (action?.Parameters?.Length != 9) throw new InvalidDataException("LFJ did not return current liquidity bin IDs");
            ids = JArray.Parse(action.Parameters[5]);
        }
        if (ids.Count is < 1 or > 200) throw new InvalidDataException("Invalid LFJ liquidity bin count");
        var balances = new JArray(); var owned = new JArray();
        foreach (var id in ids) {
            var balance = DefiLending.Word(await Read("balanceOf(address,uint256)", p.Wallet, id.ToString()), 0);
            if (balance > 0) { owned.Add(id.ToString()); balances.Add(balance.ToString()); }
        }
        var input = new JObject { ["ids"] = owned, ["amounts"] = balances }.ToString(Newtonsoft.Json.Formatting.None);
        if (exact != null && exact != input) throw new InvalidOperationException("LFJ liquidity bins changed; preview again");
        if (owned.Count == 0) throw new InvalidOperationException("No LFJ bin liquidity remains");
        // The owner can burn its bin shares directly. This requires no blanket ERC-1155 approval.
        var tx = DefiActionAbi.Build(p.Wallet, Pair, "burn(address,address,uint256[],uint256[])", [p.Wallet, p.Wallet, owned.ToString(), balances.ToString()]);
        var result = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx));
        int start = checked((int)DefiLending.Word(result, 0) / 32), count = checked((int)DefiLending.Word(result, start));
        if (count != owned.Count) throw new InvalidDataException("LFJ burn output length mismatch");
        var x = BigInteger.Zero; var y = BigInteger.Zero; var mask = (BigInteger.One << 128) - 1;
        for (int i = 0; i < count; i++) { var packed = DefiLending.Word(result, start + 1 + i); x += packed & mask; y += packed >> 128; }
        var tokenX = DefiNftLiquidity.Address(await Read("getTokenX()"), 0); var tokenY = DefiNftLiquidity.Address(await Read("getTokenY()"), 0);
        var outputs = new List<(string Asset, BigInteger Amount)>(); if (x > 0) outputs.Add((tokenX, x)); if (y > 0) outputs.Add((tokenY, y));
        if (outputs.Count == 0) throw new InvalidOperationException("LFJ bins return no underlying tokens");
        return (await DefiVault.PrepareTransaction(web3, chainId, outputs[0].Asset, outputs[0].Amount, tx, boost, prices, outputs)) with {
            InputAmountRaw = input, Notice = "Burns all discovered LFJ liquidity bins directly into both underlying tokens. Outputs are simulated and rechecked before sending; this owner burn has no on-chain slippage parameters." };
    }
}
