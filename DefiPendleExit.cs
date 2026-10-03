using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiPendleExit
{
    public const string Router = "0x888888888889758f76e7103c6cbf23abbf58f946";
    private const string Zero = "0x0000000000000000000000000000000000000000";
    private static readonly Dictionary<string, string> PrincipalMarkets = new(StringComparer.OrdinalIgnoreCase) {
        ["0x1102fe0a9ae3b82e41b60f42201dbff466a9792c"] = "0x6ea328bf810ef0f0bd1291eb52f1529aa073cefa",
        ["0xbb33e51bdc598d710ff59fdf523e80ab7c882c83"] = "0xc8fd1f1e059d97ec71ae566dd6ca788dc92f36af"
    };
    public static bool Supported(DefiPosition p) => p.Chain == "arb" && p.Protocol == "Pendle V2" && p.Type == "deposit" &&
        (PrincipalMarkets.ContainsKey(p.VaultAddress ?? "") || p.VaultAddress?.Equals("0xba4a858d664ddb052158168db04afa3cff5cfcc8", StringComparison.OrdinalIgnoreCase) == true);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, method, args)));
        var principal = PrincipalMarkets.TryGetValue(p.VaultAddress!, out var configured);
        var market = principal ? configured! : p.VaultAddress!;
        var tokenData = await Read(market, "readTokens()"); var sy = DefiNftLiquidity.Address(tokenData, 0); var pt = DefiNftLiquidity.Address(tokenData, 1);
        if (principal && !pt.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pendle principal token does not match its market");
        var expiry = DefiLending.Word(await Read(market, "expiry()"), 0);
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        if (expiry > block.Timestamp.Value) throw new InvalidOperationException("This Pendle adapter redeems expired markets only");
        var balance = DefiLending.Word(await Read(p.VaultAddress!, "balanceOf(address)", p.Wallet), 0);
        var plan = exact == null ? null : JObject.Parse(exact); var amount = plan == null ? balance : BigInteger.Parse((string)plan["amount"]!);
        if (amount <= 0 || balance < amount) throw new InvalidOperationException("No Pendle receipt tokens remain, or the balance decreased");
        var outContract = web3.Eth.GetContract(DefiBlackwing.Abi(("getTokensOut", [], ["address[]"])), sy);
        var outputs = await SwapExecution.Read(outContract.GetFunction("getTokensOut").CallAsync<List<string>>());
        var preferred = p.WithdrawActions.FirstOrDefault(a => DefiActionAbi.Signature(a.Function).Name == "exitPostExpToToken");
        var preferredToken = preferred?.Parameters?.Length == 5 ? (string?)JArray.Parse(preferred.Parameters[4])[0] : p.AssetAddress;
        var token = (string?)plan?["token"] ?? outputs.FirstOrDefault(t => t.Equals(preferredToken, StringComparison.OrdinalIgnoreCase)) ?? outputs.FirstOrDefault();
        if (token == null || !outputs.Contains(token, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Pendle output is not redeemable from this SY");
        var state = plan ?? new JObject { ["amount"] = amount.ToString(), ["token"] = token, ["minimum"] = "0" };
        async Task<DefiVault.Quote> Quote() {
            var output = new JArray(token, (string)state["minimum"]!, token, Zero, new JArray("0", Zero, "0x", false));
            var tx = DefiActionAbi.Build(p.Wallet, Router, "exitPostExpToToken(address,address,uint256,uint256,(address,uint256,address,address,(uint8,address,bytes,bool)))",
                [p.Wallet, market, principal ? amount.ToString() : "0", principal ? "0" : amount.ToString(), output.ToString()]);
            var action = new RabbyWithdrawAction { Approval = new JObject { ["token_id"] = p.VaultAddress, ["to"] = Router, ["str_raw_amount"] = amount.ToString() } };
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, state.ToString(Newtonsoft.Json.Formatting.None), prices, action: action);
        }
        var quote = await Quote();
        if (plan == null) {
            var received = quote.Outputs!.Single(o => o.Asset.Equals(token, StringComparison.OrdinalIgnoreCase));
            state["minimum"] = (BigInteger.Parse(received.AmountRaw) * 995 / 1000).ToString(); quote = await Quote();
        }
        return quote with { Notice = "Redeems the full expired Pendle PT or LP position through SY into the shown underlying token, with an on-chain output minimum." };
    }
}
