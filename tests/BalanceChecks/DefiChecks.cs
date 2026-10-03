using System.Net;
using Newtonsoft.Json.Linq;
using z3nSafe;

internal static class DefiChecks
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
    private sealed class AsyncHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(token);
    }
    public static async Task Run(Action<string, bool> check)
    {
        var sample = new DefiPosition("batch-1", 1, "wallet", "eth", "Protocol", "deposit", "Q", "1", 1m, "asset", "VAULT", "group");
        var batch = new[] { sample, sample with { Id = "leg", Type = "loan", VaultAddress = "vault" },
            sample with { Id = "other-account", AccountId = 2 }, sample with { Id = "other-protocol", Protocol = "Protocol V2" },
            sample with { Id = "other-chain", Chain = "bsc" }, sample with { Id = "other-vault", VaultAddress = "another" } };
        var chosen = DefiBatch.Select(batch, "Protocol", new[] { 1, 1 });
        check("Batch selects only explicit accounts and exact protocol", chosen.All(p => p.AccountId == 1 && p.Protocol == "Protocol") && chosen.Count == 3);
        check("Batch deduplicates vault legs without losing different chains or vaults", chosen.Count(p => p.Chain == "eth") == 2 && chosen.Count(p => p.Chain == "bsc") == 1);
        check("Batch prefers withdrawable deposit over a loan leg", DefiBatch.Select(batch.Reverse(), "Protocol", new[] { 1 }).All(p => p.Type == "deposit"));
        check("Empty account selection never becomes all accounts", DefiBatch.Select(batch, "Protocol", Array.Empty<int>()).Count == 0);
        const string wallet = "0x1111111111111111111111111111111111111111";
        const string asset = "0x2222222222222222222222222222222222222222";
        const string vault = "0x3333333333333333333333333333333333333333";
        var token = new JObject { ["id"] = asset, ["symbol"] = "TEST", ["amount"] = 123456789.123456789123456789m,
            ["decimals"] = 18, ["price"] = 1m };
        var loan = (JObject)token.DeepClone(); loan["price"] = null;
        var body = new JArray(new JObject { ["id"] = "test-protocol", ["name"] = "test-protocol", ["chain"] = "eth",
            ["portfolio_item_list"] = new JArray(new JObject { ["name"] = "Staked", ["pool"] = new JObject { ["id"] = vault },
                ["detail"] = new JObject { ["supply_token_list"] = new JArray(token), ["borrow_token_list"] = new JArray(loan),
                    ["token_list"] = new JArray(token.DeepClone()) } }) });
        var rows = DefiPositionsClient.Parse(body, 7, wallet);
        check("Rabby distinguishes supplies and loans without duplicate token_list", rows.Count == 2 && rows[0].Type == "staked" && rows[1].Type == "loan");
        check("DeFi preserves exact quantities and unknown USD", rows[0].Amount == "123456789.123456789123456789" && rows[1].ValueUsd == null);
        check("Rabby human amounts are not divided by token decimals again", rows[0].ValueUsd == 123456789.123456789123456789m);
        body[0]!["portfolio_item_list"]![0]!["detail"]!["health_rate"] = new JValue("MARKER");
        var largeJson = body.ToString().Replace("\"MARKER\"", "1.157920892373162e+59");
        var exactRows = DefiPositionsClient.Parse(DefiPositionsClient.ParseJson(largeJson), 7, wallet);
        check("Real Rabby huge health rates do not break precise amount parsing", exactRows[0].Amount == "123456789.123456789123456789");
        check("DeFi pool contract is distinct from underlying asset", rows[0].VaultAddress == vault && rows[0].AssetAddress == asset);
        check("DeFi identities retain account, wallet, chain, protocol and LP group", rows[0].AccountId == 7 && rows[0].Wallet == wallet && rows[0].Chain == "eth" && rows[0].Protocol == "test-protocol" && rows[0].GroupId == rows[1].GroupId);
        var labBody = new JArray(new JObject { ["id"] = "scrl_layerbank", ["name"] = "LayerBank", ["chain"] = "scrl", ["portfolio_item_list"] = new JArray(new JObject {
            ["pool"] = new JObject { ["id"] = DefiScrollLending.Core }, ["detail"] = new JObject { ["reward_token_list"] = new JArray(new JObject {
                ["id"] = DefiLayerBankRewards.Lab, ["symbol"] = "LAB.s", ["amount"] = 0.3m }) } }) });
        var priceCalls = 0;
        using (var client = new DefiPositionsClient(new Handler(request => {
            var pricing = request.RequestUri!.Host == "li.quest";
            if (pricing) priceCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pricing ? new JObject {
                ["chainId"] = 534352, ["address"] = DefiLayerBankRewards.Lab, ["priceUSD"] = "0.00015" }.ToString() : labBody.ToString()) };
        }))) {
            var priced = await client.ReadForScanAsync(7, wallet, default);
            await client.ReadForScanAsync(8, wallet, default);
            check("LAB reward uses exact free price and caches it across scan accounts", priced.Single().ValueUsd == 0.000045m && priced.Single().PriceSource == "LI.FI" && priceCalls == 1);
        }
        using (var client = new DefiPositionsClient(new Handler(request => request.RequestUri!.Host == "li.quest" ?
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(labBody.ToString()) })))
            check("Price service failure preserves LAB amount with unknown valuation", (await client.ReadForScanAsync(7, wallet, default)).Single() is { Amount: "0.3", ValueUsd: null });
        var malformed = false;
        try { DefiPositionsClient.Parse(new JObject(), 7, wallet); } catch (InvalidDataException) { malformed = true; }
        check("Missing DeFi response is not treated as zero balances", malformed);
        var noKey = false; var complex = false;
        using (var client = new DefiPositionsClient(new Handler(request => {
            noKey = request.Headers.Authorization == null;
            complex = request.RequestUri!.OriginalString == "https://api.rabby.io/v1/user/complex_protocol_list?id=" + wallet;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToString()) };
        }))) check("Rabby requests are free of API keys and paid providers", (await client.ReadAsync(7, wallet, default)).Count == 2 && noKey && complex);
        var limited = false; var requests = 0;
        using (var client = new DefiPositionsClient(new Handler(_ => {
            requests++; return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }))) try { await client.ReadAsync(7, wallet, default); } catch (InvalidOperationException ex) { limited = ex.Message.Contains("No paid fallback"); }
        check("Public API blocking never triggers a paid fallback", limited && requests == 1);
        using var cancel = new CancellationTokenSource(); var cancelled = false;
        using (var client = new DefiPositionsClient(new Handler(_ => {
            cancel.Cancel(); return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        }))) try { await client.ReadAsync(7, wallet, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check("Rate-limit retry waiting can be stopped", cancelled);
        var scanCalls = 0; var timeoutRecorded = false;
        using (var client = new DefiPositionsClient(new AsyncHandler(async ct => {
            if (++scanCalls == 1) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        })))
        {
            try { await client.ReadForScanAsync(7, wallet, default, TimeSpan.FromMilliseconds(25)); }
            catch (TimeoutException) { timeoutRecorded = true; }
            check("Account timeout is an error and the next account can still scan", timeoutRecorded && (await client.ReadForScanAsync(8, wallet, default)).Count == 0 && scanCalls == 2);
        }
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(25)))
        using (var client = new DefiPositionsClient(new AsyncHandler(async ct => {
            await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK);
        })))
        {
            var userStopped = false;
            try { await client.ReadForScanAsync(7, wallet, stop.Token); }
            catch (OperationCanceledException) { userStopped = true; }
            check("User stop remains cancellation, never an account timeout", userStopped);
        }
        scanCalls = 0; timeoutRecorded = false;
        using (var client = new DefiPositionsClient(new Handler(_ => ++scanCalls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") })))
        {
            try { await client.ReadForScanAsync(7, wallet, default, TimeSpan.FromMilliseconds(25)); }
            catch (TimeoutException) { timeoutRecorded = true; }
            check("Account deadline also bounds rate-limit backoff and allows the next wallet", timeoutRecorded && (await client.ReadForScanAsync(8, wallet, default)).Count == 0 && scanCalls == 2);
        }
        check("Rabby chain IDs map to the correct withdrawal networks", DefiVault.Network("eth").ChainId == 1 && DefiVault.Network("avax").ChainId == 43114 && DefiVault.Network("matic").ChainId == 137);
        check("Blast withdrawal network is enabled with its data fee adapter", DefiVault.Network("blast").ChainId == 81457);
        check("Optimism withdrawal network requires its full fee adapter", DefiVault.Network("op").ChainId == 10);
        check("Compound yield suffix preserves the verified Polygon market", DefiPositionsClient.PoolId(DefiScrollLending.PolygonComet + ":yield", "Compound V3") == DefiScrollLending.PolygonComet);
        var stg = new DefiPosition("stg", 1, wallet, "bsc", "Stargate", "locked", "STG", "26", 4m,
            "0xb0d502e938ed5f4df2e681fe6e419ff29631d62b", "0xd4888870c8686c748232719051b677791dbda26d", "escrow");
        check("Verified Stargate escrow allows lock checks", DefiWithdrawal.Unavailable(stg) == null);
        check("Unknown Stargate escrow is never trusted", DefiWithdrawal.Unavailable(stg with { VaultAddress = wallet }) != null);
        DefiStargate.ValidateLock(26, 100, 100, false, "26");
        check("Expired STG lock passes at the exact block boundary", true);
        var blockedLock = false;
        try { DefiStargate.ValidateLock(26, 101, 100, false, null); } catch (InvalidOperationException ex) { blockedLock = ex.Message.Contains("locked until"); }
        check("Unexpired STG lock shows its unlock time", blockedLock);
        blockedLock = false;
        try { DefiStargate.ValidateLock(27, 100, 100, false, "26"); } catch (InvalidOperationException) { blockedLock = true; }
        check("Changed STG lock amount requires a new preview", blockedLock);
        check("LFJ reward uses verified claim support", DefiWithdrawal.Unavailable(stg with { Protocol = "LFJ", Chain = "arb", Type = "reward", AssetAddress = DefiJoe.Reward, VaultAddress = DefiJoe.Staking }) == null);
        foreach (var name in new[] { "PancakeSwap V3", "Curve", "Merkl", "Hana Network", "Hana Finance" })
            check(name + " never attempts an incompatible ERC-4626 exit", DefiWithdrawal.Unsupported(stg with { Protocol = name, Type = "deposit" }) != null);
        check("Linea, Mode, Manta and Metis have configured fee paths", DefiVault.Network("linea").ChainId == 59144 &&
            DefiVault.Network("mode").ChainId == 34443 && DefiVault.Network("manta").ChainId == 169 && DefiVault.Network("metis").ChainId == 1088);
        check("Base and Taiko have configured withdrawal fee paths", DefiVault.Network("base").ChainId == 8453 && DefiVault.Network("taiko").ChainId == 167000);
        foreach (var pair in new[] { (0m, 1m), (1m, 0m), (1m, 1m), (1m, 2m) })
        {
            var blocked = false;
            try { DefiVault.ValidateCosts(pair.Item1, pair.Item2); } catch (InvalidOperationException) { blocked = true; }
            check($"DeFi fee guard blocks output={pair.Item1}, fee={pair.Item2}", blocked);
        }
        DefiVault.ValidateCosts(2m, 1m);
        check("DeFi valid cost passes", true);
        var q = new DefiVault.Quote(asset, "TEST", 18, "123", 2, 1, "300000", "123456", "0x1234");
        var tx = DefiVault.Transaction(q, vault, wallet);
        check("Withdrawal sends no native funds and uses exact simulated calldata", tx.To == vault && tx.From == wallet && tx.Value.Value == 0 && tx.GasPrice.Value == 123456 && tx.Data == q.Data);
        await CheckContract(check, wallet, asset, vault);
    }

    private sealed class RpcHandler(Func<JObject, string> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject {
                ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = response(body)
            }.ToString()) };
        }
    }
    private static async Task CheckContract(Action<string, bool> check, string wallet, string asset, string vault)
    {
        var calls = new List<string>(); var callData = ""; var available = "0xf4240"; var actualAsset = asset;
        using var rpcHttp = new HttpClient(new RpcHandler(body => {
            var method = (string)body["method"]!; calls.Add(method);
            if (method == "eth_chainId") return "0x1";
            if (method == "eth_gasPrice") return "0x3b9aca00";
            if (method == "eth_estimateGas") return "0x186a0";
            if (method == "eth_getBalance") return "0xde0b6b3a7640000";
            if (method == "eth_call") {
                var data = (string?)body["params"]?[0]?["data"] ?? "";
                if (data.StartsWith("0x38d52e0f")) return "0x" + actualAsset[2..].PadLeft(64, '0');
                if (data.StartsWith("0xce96cb77")) return "0x" + available[2..].PadLeft(64, '0');
                callData = data; return "0x" + "f4240".PadLeft(64, '0');
            }
            throw new InvalidOperationException("Unexpected RPC method: " + method);
        }));
        var web3 = new Nethereum.Web3.Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.test/"), rpcHttp));
        using var priceHttp = new HttpClient(new Handler(request => {
            var native = request.RequestUri!.OriginalString.Contains("token=0x000000");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject {
                ["chainId"] = 1, ["address"] = native ? "0x0000000000000000000000000000000000000000" : asset,
                ["decimals"] = native ? 18 : 6, ["symbol"] = native ? "ETH" : "USDC", ["priceUSD"] = native ? "3000" : "1"
            }.ToString()) };
        }));
        var quote = await DefiVault.Prepare(web3, 1, vault, wallet, asset, 0, priceClient: priceHttp);
        check("Vault quote reads chain/asset/limit and simulates without broadcasting", quote.AmountRaw == "1000000" && quote.ValueUsd == 1m && quote.FeeUsd == .33m && !calls.Any(c => c.StartsWith("eth_send")));
        check("Vault calldata has exact amount and same-wallet receiver/owner", callData.StartsWith("0xb460af94") && callData[10..74] == "f4240".PadLeft(64, '0') && callData[74..138] == wallet[2..].PadLeft(64, '0') && callData[138..] == wallet[2..].PadLeft(64, '0'));
        available = "0x0"; var locked = false;
        try { await DefiVault.Prepare(web3, 1, vault, wallet, asset, 0, priceClient: priceHttp); } catch (InvalidOperationException) { locked = true; }
        check("Locked vault is rejected before transaction construction", locked);
        available = "0x1"; var reduced = false;
        try { await DefiVault.Prepare(web3, 1, vault, wallet, asset, 0, "1000000", priceHttp); } catch (InvalidOperationException) { reduced = true; }
        check("Execution recheck rejects a reduced available amount", reduced);
        actualAsset = vault; var mismatch = false;
        try { await DefiVault.Prepare(web3, 1, vault, wallet, asset, 0, priceClient: priceHttp); } catch (InvalidOperationException) { mismatch = true; }
        check("Wrong underlying contract cannot be withdrawn", mismatch);
    }
}
