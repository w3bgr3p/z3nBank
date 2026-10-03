using System.Net;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using Nethereum.JsonRpc.Client;
using z3nSafe;

internal static class RabbyActionChecks
{
    public static async Task Run(Action<string, bool> check)
    {
        var maximum = ((BigInteger.One << 256) - 1).ToString();
        var payload = new JArray(new JObject {
            ["id"] = "taiko_hana", ["chain"] = "taiko", ["name"] = "Hana Finance",
            ["portfolio_item_list"] = new JArray(new JObject {
                ["pool"] = new JObject { ["id"] = Pool }, ["stats"] = new JObject { ["debt_usd_value"] = 0 },
                ["detail"] = new JObject { ["supply_token_list"] = new JArray(new JObject { ["id"] = Asset, ["symbol"] = "Q", ["amount"] = 999, ["price"] = 2 }) },
                ["withdraw_actions"] = new JArray(new JObject { ["type"] = "withdraw", ["contract_id"] = Pool,
                    ["func"] = "withdraw(address,uint256,address)(uint256)", ["str_params"] = new JArray(Asset, maximum, Wallet) })
            })
        });
        var p = DefiPositionsClient.Parse(DefiPositionsClient.ParseJson(payload.ToString()), 1, Wallet).Single();
        check("Discovery preserves Rabby actions and exact uint256 parameters", p.WithdrawActions.Single().Parameters![1] == maximum);
        var restored = JsonConvert.DeserializeObject<DefiPosition>(JsonConvert.SerializeObject(p))!;
        check("Rabby withdrawal actions survive snapshot persistence", restored.WithdrawActions.Single().Parameters![1] == maximum);
        var tx = DefiRabbyActions.Build(p);
        check("Rabby function signature and exact arguments encode the expected calldata", tx.Data.StartsWith("0x69328dec") && tx.Data.Substring(74, 64) == new string('f', 64) && tx.To == Pool);
        check("Provider action enables Hana without a per-protocol withdrawal adapter", DefiWithdrawal.Unavailable(p) == null);
        check("Batch executes a shared Rabby action once for multiple position legs", DefiBatch.Select(new[] { p, p with { Id = "other-leg", Symbol = "OTHER" } }, p.Protocol, new[] { 1 }).Count == 1);
        check("Proxy-held positions do not expose a direct wallet withdrawal", DefiRabbyActions.Unavailable(p with { HasProxy = true }) != null);
        var blocked = false; try { DefiRabbyActions.Signature("transfer(address,uint256)"); } catch { blocked = true; }
        check("Transfer and approval functions cannot masquerade as withdrawals", blocked);
        var outputs = DefiRabbyActions.Outputs(Simulation, "taiko");
        check("Simulation output uses exact raw_amount rather than indexed amount", outputs.Single().Amount == BigInteger.Parse("2000000000000000000"));
        blocked = false; try { DefiRabbyActions.Outputs(Simulation, "arb"); } catch { blocked = true; }
        check("Wrong-chain simulator output is rejected", blocked);
        using var rpcHttp = new HttpClient(new RpcHandler()); var web3 = new Web3(new RpcClient(new Uri("https://rpc.invalid"), rpcHttp));
        var api = new ApiHandler(); using var http = new HttpClient(api);
        var quote = await DefiRabbyActions.Prepare(web3, 167000, p, 0, prices: http, simulationClient: http);
        check("Generic action preview combines RPC checks, simulated outputs and the fee guard", quote.ValueUsd == 4 && quote.Destination == Pool && quote.Outputs?.Count == 1 && api.Simulations == 1);
        var fresh = await DefiRabbyActions.Prepare(web3, 167000, p, 0, quote.InputAmountRaw, http, http);
        DefiVault.ValidateRecheck(quote, fresh);
        check("Execution can recheck the exact confirmed action without trusting indexed amounts", fresh.Data == quote.Data && api.Simulations == 2);
        tx.Value = new Nethereum.Hex.HexTypes.HexBigInteger(BigInteger.Parse("100000000000000"));
        var paidRequest = await DefiVault.PrepareTransaction(web3, 167000, Asset, BigInteger.Parse("2000000000000000000"), tx, 0, http);
        check("Native keeper payment is included exactly once in the fee guard and broadcast value", paidRequest.FeeUsd == .42m &&
            paidRequest.TotalFeeWei == "210000000000000" && DefiVault.Transaction(paidRequest, Pool, Wallet).Value.Value == tx.Value.Value);
        p.WithdrawActions[0].Parameters![2] = "0x4444444444444444444444444444444444444444";
        blocked = false; try { await DefiRabbyActions.Prepare(web3, 167000, p, 0, prices: http, simulationClient: http); } catch (InvalidDataException) { blocked = true; }
        check("A foreign wallet recipient is rejected before simulation or signing", blocked && api.Simulations == 2);
    }
    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string Asset = "0x2222222222222222222222222222222222222222";
    private const string Pool = "0x3333333333333333333333333333333333333333";
    private sealed class RpcHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var data = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            var method = (string)data["method"]!;
            var result = method switch {
                "eth_chainId" => "0x28c58", "eth_getTransactionCount" => "0x1", "eth_call" => "0x",
                "eth_estimateGas" => "0x186a0", "eth_gasPrice" => "0x3b9aca00", "eth_getBalance" => "0xde0b6b3a7640000",
                "eth_getCode" => (string?)data["params"]?[0] == Asset ? "0x6000" : "0x",
                _ => throw new Exception("Unexpected RPC: " + method)
            };
            return Reply(new JObject { ["jsonrpc"] = "2.0", ["id"] = data["id"], ["result"] = result }.ToString());
        }
    }
    private static HttpResponseMessage Reply(string data) => new(HttpStatusCode.OK) { Content = new StringContent(data) };
    private sealed class ApiHandler : HttpMessageHandler
    {
        public int Simulations;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post) {
                Simulations++;
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
                if ((int?)body["tx"]?["chainId"] != 167000 || (string?)body["tx"]?["from"] != Wallet) throw new Exception("Wrong simulation wallet/network");
                return Reply(Simulation.ToString());
            }
            var address = request.RequestUri!.Query.Contains(Asset) ? Asset : "0x0000000000000000000000000000000000000000";
            return Reply(new JObject { ["chainId"] = 167000, ["address"] = address, ["symbol"] = address == Asset ? "Q" : "ETH", ["decimals"] = 18, ["priceUSD"] = address == Asset ? "2" : "2000" }.ToString());
        }
    }
    private static JObject Simulation => new() {
        ["pre_exec"] = new JObject { ["success"] = true },
        ["balance_change"] = new JObject { ["success"] = true, ["receive_token_list"] = new JArray(new JObject {
            ["id"] = Asset, ["chain"] = "taiko", ["raw_amount"] = "2000000000000000000", ["amount"] = 999 }) }
    };
}
