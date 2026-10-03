using System.Net;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using z3nSafe;

internal static class DefiQueueChecks
{
    private sealed class Handler(Func<JObject, string> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent(new JObject {
                ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = response(body)
            }.ToString()) };
        }
    }
    public static async Task Run(Action<string, bool> check)
    {
        const string wallet = "0x1111111111111111111111111111111111111111";
        const string market = "0x2222222222222222222222222222222222222222";
        const string other = "0x3333333333333333333333333333333333333333";
        string Word(BigInteger n) => n.ToString("x").PadLeft(64, '0');
        string Address(string a) => "0x" + a[2..].PadLeft(64, '0');
        var p = new DefiPosition("gmx", 1, wallet, "arb", "GMX V2", "deposit", "USDC", "1", 1, market, market, "group") { AdapterId = "gmx2_liquidity" };
        string owner = wallet, selectedMarket = market; int count = 1, addressReads = 0; var methods = new List<string>();
        using var client = new HttpClient(new Handler(body => {
            var method = (string)body["method"]!; methods.Add(method);
            if (method == "eth_chainId") return "0xa4b1";
            if (method != "eth_call") throw new Exception("Unexpected queue RPC: " + method);
            var data = (string)body["params"]![0]!["data"]!;
            if (data.StartsWith(DefiActionAbi.Build(wallet, market, "getBytes32Count(bytes32)", ["0x" + new string('0', 64)]).Data[..10])) { addressReads = 0; return "0x" + Word(count); }
            if (data.StartsWith(DefiActionAbi.Build(wallet, market, "getBytes32ValuesAt(bytes32,uint256,uint256)", ["0x" + new string('0', 64), "0", "1"]).Data[..10])) return "0x" + Word(32) + Word(1) + new string('4', 64);
            if (data.StartsWith(DefiActionAbi.Build(wallet, market, "getUint(bytes32)", ["0x" + new string('0', 64)]).Data[..10])) return "0x" + Word(123);
            return addressReads++ == 0 ? Address(owner) : Address(selectedMarket);
        }));
        var web3 = new Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.invalid"), client));
        check("GMX queue reads exact market shares for the selected owner", (await DefiGmxPending.Requests(web3, p)).Single().Shares == 123);
        selectedMarket = other;
        check("A pending GMX request in another market does not block this market", (await DefiGmxPending.Requests(web3, p)).Count == 0);
        selectedMarket = market; owner = other; bool blocked = false;
        try { await DefiGmxPending.Requests(web3, p); } catch (InvalidDataException) { blocked = true; }
        check("Foreign GMX request ownership is rejected before constructing cancellation", blocked);
        count = 0; owner = wallet;
        check("A completed GMX request is removed by read-only reconciliation", !await DefiPending.Exists(web3, p));
        check("Queue reconciliation never signs or broadcasts", methods.All(m => m is "eth_call" or "eth_chainId"));
    }
}
