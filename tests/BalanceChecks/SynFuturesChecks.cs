using System.Net;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using z3nSafe;

internal static class SynFuturesChecks
{
    private sealed class Handler(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            new(HttpStatusCode.OK) { Content = new StringContent(respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token))) };
    }
    public static async Task Run(Action<string, bool> check)
    {
        const string wallet = "0x1111111111111111111111111111111111111111";
        var p = new DefiPosition("gate", 1, wallet, "blast", "SynFutures V3", "deposit", "WETH", "0.001", 3,
            DefiWithdrawal.BlastWeth, DefiWithdrawal.BlastGate, "group");
        var encoded = DefiWithdrawal.GateArgument(p.AssetAddress!, 123);
        check("Gate packs uint96 amount above the exact token address", Convert.ToHexString(encoded).ToLowerInvariant() == "00000000000000000000007b" + p.AssetAddress![2..]);
        var overflow = false; try { DefiWithdrawal.GateArgument(p.AssetAddress!, BigInteger.One << 96); } catch (InvalidOperationException) { overflow = true; }
        check("Gate rejects uint96 overflow", overflow);
        check("SynFutures adapter requires the official Blast Gate", !DefiWithdrawal.IsGate(p with { VaultAddress = wallet }) && !DefiWithdrawal.IsGate(p with { Chain = "base" }));
        check("Gate batch retains different deposited assets", DefiBatch.Select(new[] { p, p with { Id = "usdb", AssetAddress = DefiWithdrawal.BlastUsdb }, p with { Id = "duplicate" } }, p.Protocol, new[] { 1 }).Count == 2);
        string callData = "", reserveData = ""; var methods = new List<string>(); var reserve = new BigInteger(1000000000000000); var oracleFee = new BigInteger(1000000000000); var balance = BigInteger.Pow(10, 18);
        var chain = 81457;
        using var rpc = new HttpClient(new Handler((_, text) => {
            var body = JObject.Parse(text); var method = (string)body["method"]!; methods.Add(method);
            string Hex(BigInteger number) => "0x" + number.ToString("x");
            var result = method switch {
                "eth_chainId" => Hex(chain), "eth_gasPrice" => Hex(1000000), "eth_estimateGas" => Hex(100000),
                "eth_getBalance" => Hex(balance), "eth_getTransactionCount" => "0x1",
                "eth_call" => Call(body), _ => throw new InvalidOperationException("Unexpected " + method)
            };
            string Call(JObject request) {
                var tx = request["params"]![0]!; var data = (string)tx["data"]!;
                if (string.Equals((string?)tx["to"], DefiFees.BlastOracle, StringComparison.OrdinalIgnoreCase)) return "0x" + oracleFee.ToString("x").PadLeft(64, '0');
                if (data.StartsWith("0x8e19899e")) { callData = data; return "0x"; }
                reserveData = data; return "0x" + reserve.ToString("x").PadLeft(64, '0');
            }
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = result }.ToString();
        }));
        var web3 = new Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.test/"), rpc));
        using var prices = new HttpClient(new Handler((request, _) => {
            var native = request.RequestUri!.OriginalString.Contains("token=0x000000");
            return new JObject { ["chainId"] = 81457, ["address"] = native ? "0x0000000000000000000000000000000000000000" : p.AssetAddress,
                ["symbol"] = native ? "ETH" : "WETH", ["decimals"] = 18, ["priceUSD"] = "3000" }.ToString();
        }));
        var q = await DefiWithdrawal.Prepare(web3, 81457, p, 0, priceClient: prices);
        check("Gate reads live reserve and simulates the protocol-specific withdrawal", q.AmountRaw == reserve.ToString() && callData == "0x8e19899e" + Convert.ToHexString(DefiWithdrawal.GateArgument(p.AssetAddress!, reserve)).ToLowerInvariant());
        check("Blast total fee includes buffered L1 oracle fee", q.L1FeeUsd == .00375m && q.FeeUsd == .00408m);
        check("Gate preview never signs or broadcasts", !methods.Any(m => m.StartsWith("eth_send") || m.Contains("sign")));
        check("Gate reserve query binds the token and selected wallet", reserveData[10..74] == p.AssetAddress![2..].PadLeft(64, '0') && reserveData[74..] == wallet[2..].PadLeft(64, '0'));
        balance = 110000000000; var funds = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p, 0, priceClient: prices); } catch (InvalidOperationException ex) { funds = ex.Message.Contains("Insufficient native"); }
        check("Enough L2 gas but insufficient L1 fee blocks withdrawal", funds);
        balance = BigInteger.Pow(10, 18); reserve = 1; var reduced = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p, 0, "1000000000000000", prices); } catch (InvalidOperationException) { reduced = true; }
        check("Execution recheck refuses reduced Gate reserve", reduced);
        reserve = 1000000000000000; oracleFee = 0; var missing = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p, 0, priceClient: prices); } catch (InvalidOperationException ex) { missing = ex.Message.Contains("L1 data fee unavailable"); }
        check("Missing L1 fee never silently becomes zero", missing);
        var locked = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p with { Type = "staked" }, 0, priceClient: prices); } catch (InvalidOperationException) { locked = true; }
        check("Gate adapter never closes trading margin or LP positions", locked);
        chain = 1; var wrongChain = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p, 0, priceClient: prices); } catch (InvalidOperationException ex) { wrongChain = ex.Message.Contains("RPC chain"); }
        check("Gate refuses a wrong-chain RPC", wrongChain);
        chain = 81457; var wrongAsset = false;
        try { await DefiWithdrawal.Prepare(web3, 81457, p with { AssetAddress = wallet }, 0, priceClient: prices); } catch (InvalidOperationException ex) { wrongAsset = ex.Message.Contains("WETH/USDB"); }
        check("Gate refuses an unsupported underlying asset", wrongAsset);
    }
}
