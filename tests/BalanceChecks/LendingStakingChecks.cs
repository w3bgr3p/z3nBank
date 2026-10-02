using System.Net;
using System.Numerics;
using Newtonsoft.Json.Linq;
using z3nSafe;

internal static class LendingStakingChecks
{
    private sealed class Handler(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            new(HttpStatusCode.OK) { Content = new StringContent(respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token))) };
    }
    public static async Task Run(Action<string, bool> check)
    {
        check("Provider aliases resolve identically for RPC and chain ID", Rpc.ChainId("arb") == 42161 && Rpc.Get("arb") == Rpc.Arbitrum && Rpc.ChainId("era") == 324 && Rpc.Get("era") == Rpc.Zksync && Rpc.ChainId("op") == 10);
        const string wallet = "0x1111111111111111111111111111111111111111", asset = "0x2222222222222222222222222222222222222222";
        var p = new DefiPosition("aave", 39, wallet, "eth", "Aave V3", "deposit", "TEST", "1", 1m,
            asset, "0x87870bca3f3fd6335c3f4ce8392d69350b4fa4e2", "pool");
        check("Aave batch retains each underlying reserve", DefiBatch.Select(new[] { p, p with { Id = "a2", AssetAddress = wallet }, p with { Id = "duplicate" } }, p.Protocol, new[] { 39 }).Count == 2);
        var debt = BigInteger.Zero; var available = new BigInteger(1000000); var chain = 1; string exactData = "";
        var methods = new List<string>();
        string Word(BigInteger value) => value.ToString("x").PadLeft(64, '0');
        using var rpc = new HttpClient(new Handler((_, text) => {
            var body = JObject.Parse(text); var method = (string)body["method"]!; methods.Add(method);
            var result = method switch {
                "eth_chainId" => "0x" + chain.ToString("x"), "eth_gasPrice" => "0x3b9aca00", "eth_estimateGas" => "0x186a0", "eth_getBalance" => "0xde0b6b3a7640000",
                "eth_call" => Call(body), _ => throw new InvalidOperationException(method)
            };
            string Call(JObject request) {
                var data = (string)request["params"]![0]!["data"]!;
                if (chain == 42161) {
                    if (data.Length == 10) return "0x" + DefiJoe.Joe[2..].PadLeft(64, '0');
                    if (data.Length == 138) return "0x" + Word(available) + Word(0);
                    exactData = data; return "0x";
                }
                if (data.Length == 74) return "0x" + Word(100) + Word(debt) + string.Concat(Enumerable.Repeat(Word(0), 4));
                exactData = data; return "0x" + Word(available);
            }
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = result }.ToString();
        }));
        var web3 = new Nethereum.Web3.Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.test/"), rpc));
        using var prices = new HttpClient(new Handler((request, _) => {
            var native = request.RequestUri!.OriginalString.Contains("token=0x000000");
            return new JObject { ["chainId"] = chain, ["address"] = native ? "0x0000000000000000000000000000000000000000" : p.AssetAddress,
                ["symbol"] = "TEST", ["decimals"] = native ? 18 : 6, ["priceUSD"] = native ? "3000" : "1" }.ToString();
        }));
        var q = await DefiWithdrawal.Prepare(web3, 1, p, 0, priceClient: prices);
        check("Aave exact withdrawal targets underlying reserve and same wallet", q.AmountRaw == "1000000" && exactData[10..74] == asset[2..].PadLeft(64, '0') && exactData[74..138] == Word(available) && exactData[138..] == wallet[2..].PadLeft(64, '0'));
        debt = 1; var blocked = false;
        try { await DefiWithdrawal.Prepare(web3, 1, p, 0, priceClient: prices); } catch (InvalidOperationException ex) { blocked = ex.Message.Contains("active debt"); }
        check("Aave debt blocks collateral withdrawal before simulation", blocked);
        debt = 0; available = 1; blocked = false;
        try { await DefiWithdrawal.Prepare(web3, 1, p, 0, "1000000", prices); } catch (InvalidOperationException) { blocked = true; }
        check("Aave execution recheck rejects decreased supplied balance", blocked);
        check("Unknown Aave pool is not automatically trusted", !DefiLending.IsAave(p with { VaultAddress = wallet }));
        chain = 42161; available = 1000000;
        p = p with { Protocol = "LFJ", Type = "staked", Chain = "arb", VaultAddress = DefiJoe.Staking, AssetAddress = DefiJoe.Joe };
        q = await DefiWithdrawal.Prepare(web3, chain, p, 0, priceClient: prices);
        check("LFJ uses live staking balance and exact withdraw amount", q.AmountRaw == "1000000" && exactData.EndsWith(Word(available)) && exactData.Length == 74);
        check("Arbitrum fee relies on total RPC gas estimate without double L1 charging", q.FeeUsd == .33m && q.L1FeeUsd == 0);
        check("Protocol previews never sign or broadcast", !methods.Any(m => m.StartsWith("eth_send") || m.Contains("sign")));
        check("Unknown Blackwing vaults and unsupported SyncSwap chains report specific reasons", DefiWithdrawal.Unavailable(p with { Protocol = "Blackwing" })!.Contains("Blackwing") && DefiWithdrawal.Unavailable(p with { Protocol = "SyncSwap", Chain = "eth" })!.Contains("LP withdrawal"));
    }
}
