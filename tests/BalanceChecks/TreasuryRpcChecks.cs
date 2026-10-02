using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using z3n;
using z3nSafe;

internal static class TreasuryRpcChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        public string Chain = "0x1", Head = "0x64";
        public bool FailToken;
        public List<JObject> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Calls.Add(body);
            var method = (string)body["method"]!;
            JToken result = method switch {
                "eth_chainId" => Chain, "eth_blockNumber" => Head,
                "eth_getBalance" => "0xde0b6b3a7640000",
                "eth_call" => "0x" + new string('0', 64),
                "eth_getTransactionReceipt" => new JObject { ["transactionHash"] = "0x" + new string('1', 64),
                    ["blockHash"] = "0x" + new string('2', 64), ["blockNumber"] = "0x64", ["status"] = "0x1",
                    ["transactionIndex"] = "0x0", ["gasUsed"] = "0x5208", ["cumulativeGasUsed"] = "0x5208", ["logs"] = new JArray() },
                _ => throw new Exception("Unexpected RPC method: " + method)
            };
            var response = new JObject { ["jsonrpc"] = "2.0", ["id"] = body["id"] };
            if (FailToken && method == "eth_call") response["error"] = new JObject { ["code"] = -32000, ["message"] = "RPC unavailable" };
            else response["result"] = result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToString()) };
        }
    }
    public static async Task Run(Action<string, bool> check)
    {
        const string wallet = "0x1111111111111111111111111111111111111111";
        var stale = new Jumper.TokenInfo { Address = "0x2222222222222222222222222222222222222222", ChainId = 1,
            Symbol = "Q", Amount = "1000000", Decimals = 6, PriceUSD = "2" };
        var native = new Jumper.TokenInfo { Address = "0x0000000000000000000000000000000000000000", ChainId = 1,
            Symbol = "ETH", Amount = "1", Decimals = 18, PriceUSD = "2000" };
        var handler = new Handler(); using var http = new HttpClient(handler);
        var web3 = new Web3(new RpcClient(new Uri("https://rpc.invalid"), http));
        var balances = await TreasuryRpcBalances.Read(web3, 1, wallet, [native, stale]);
        check("Indexed amounts are replaced by RPC native and ERC-20 amounts", balances[0].Amount == "1000000000000000000" && balances[1].Amount == "0" && stale.Amount == "1000000");
        check("All amounts use the same explicit block", handler.Calls.Where(c => (string?)c["method"] is "eth_call" or "eth_getBalance").All(c => (string?)c["params"]?[1] == "0x64"));
        handler.Chain = "0x38"; var rejected = false;
        try { await TreasuryRpcBalances.Read(web3, 1, wallet, [native]); } catch (InvalidDataException) { rejected = true; }
        check("Wrong-network RPC is rejected", rejected); handler.Chain = "0x1";
        rejected = false; try { await TreasuryRpcBalances.Read(web3, 1, wallet, [native], 101); } catch (InvalidDataException) { rejected = true; }
        check("RPC behind the confirmed receipt is rejected", rejected);
        var path = Path.Combine(AppContext.BaseDirectory, $"rpc-balances-{Guid.NewGuid():N}.db");
        try {
            var db = new Db(dbMode.SQLite, sqLitePath: path);
            db.Query("CREATE TABLE _treasury (id INTEGER PRIMARY KEY, Ethereum TEXT, Blast TEXT)", thrw: true);
            var saved = JsonConvert.SerializeObject(new[] { stale });
            db.ReplaceTreasurySnapshot(1, new Dictionary<string, string> { ["Ethereum"] = saved, ["Blast"] = "unrelated" });
            var quote = new LiFiBridge.QuoteResponse { Action = new LiFiBridge.ActionInfo { ToToken = new LiFiBridge.TokenInfo {
                Address = native.Address, ChainId = 1, Decimals = 18, PriceUSD = "2000", Symbol = "ETH" } } };
            var revision = TreasuryRpcBalances.Revision;
            await TreasuryRpcBalances.RefreshAfterSwap(db, 1, "Ethereum", 1, wallet, web3, [], quote, "0x" + new string('1', 64));
            balances = JsonConvert.DeserializeObject<List<Jumper.TokenInfo>>(db.Get("Ethereum", "_treasury", id: 1))!;
            check("Post-swap refresh checks cached contracts and inserts native output even when indexer omits both", balances.Any(t => t.Symbol == "Q" && t.Amount == "0") && balances.Any(t => t.Symbol == "ETH" && t.ValueUSD == 2000));
            check("Partial snapshot preserves other chains and signals UI refresh", db.Get("Blast", "_treasury", id: 1) == "unrelated" && TreasuryRpcBalances.Revision == revision + 1);
            saved = db.Get("Ethereum", "_treasury", id: 1); handler.FailToken = true;
            rejected = false; try { await TreasuryRpcBalances.RefreshAfterSwap(db, 1, "Ethereum", 1, wallet, web3, [], quote, "0x" + new string('1', 64)); } catch { rejected = true; }
            check("RPC failure preserves the complete previous chain snapshot", rejected && db.Get("Ethereum", "_treasury", id: 1) == saved && TreasuryRpcBalances.Revision == revision + 1);
        } finally { File.Delete(path); }
    }
}
