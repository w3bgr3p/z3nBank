using System.Net;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Nethereum.Util;
using z3nSafe;

internal static class ScrollChecks
{
    private sealed class Handler(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            new(HttpStatusCode.OK) { Content = new StringContent(respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token))) };
    }
    public static async Task Run(Action<string, bool> check)
    {
        const string wallet = "0x1111111111111111111111111111111111111111";
        var debt = false; var cash = true; var collateral = new BigInteger(1000000000000000);
        var methods = new List<string>(); string lastTx = "", feePayload = "";
        var unlocked = BigInteger.Parse("1000000000000000000"); var lockedLab = BigInteger.Zero;
        string Word(BigInteger value) => Convert.ToHexString(value.ToByteArray(true, true)).PadLeft(64, '0').ToLowerInvariant();
        string Addr(string value) => value[2..].PadLeft(64, '0');
        string Selector(string value) => "0x" + Sha3Keccack.Current.CalculateHash(value)[..8];
        using var rpc = new HttpClient(new Handler((_, text) => {
            var req = JObject.Parse(text); var method = (string)req["method"]!; methods.Add(method);
            string Call() {
                var tx = req["params"]![0]!; var data = (string)tx["data"]!;
                bool Is(string name) => data.StartsWith(Selector(name));
                if (string.Equals((string?)tx["to"], DefiFees.ScrollOracle, StringComparison.OrdinalIgnoreCase)) { feePayload = data; return "0x" + Word(1000000000000); }
                if (Is("core()")) return "0x" + Addr(DefiScrollLending.Core);
                if (Is("labDistributor()")) return "0x" + Addr(DefiLayerBankRewards.Distributor);
                if (Is("LAB()")) return "0x" + Addr(DefiLayerBankRewards.Lab);
                if (Is("rewardController()")) return "0x" + Addr(DefiLayerBankRewards.Rewards);
                if (Is("earnedBalances(address)")) return "0x" + Word(lockedLab) + Word(unlocked) + Word(96) + Word(0);
                if (Is("withdraw(uint256)")) { lastTx = data; return "0x"; }
                if (Is("underlying()")) return "0x" + Word(0);
                if (Is("accountLiquidityOf(address)")) return "0x" + Word(10) + Word(10) + Word(debt ? 1 : 0);
                if (Is("balanceOf(address)")) return "0x" + Word(10000000);
                if (Is("getCash()")) return "0x" + Word(cash ? 1000000000000000000 : 0);
                if (Is("redeemToken(address,uint256)")) { lastTx = data; return "0x" + Word(1000000000000000); }
                if (Is("borrowBalanceOf(address)")) return "0x" + Word(debt ? 1 : 0);
                if (Is("baseToken()")) return "0x" + Addr(DefiScrollLending.Usdc);
                if (Is("collateralBalanceOf(address,address)")) return "0x" + Word(collateral);
                if (Is("withdraw(address,uint256)")) { lastTx = data; return "0x"; }
                throw new InvalidOperationException(data);
            }
            var result = method switch { "eth_chainId" => "0x82750", "eth_gasPrice" => "0xf4240", "eth_estimateGas" => "0x186a0",
                "eth_getBalance" => "0xde0b6b3a7640000", "eth_getTransactionCount" => "0x0", "eth_call" => Call(), _ => throw new InvalidOperationException(method) };
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = req["id"], ["result"] = result }.ToString();
        }));
        using var prices = new HttpClient(new Handler((req, _) => new JObject { ["chainId"] = 534352,
            ["address"] = req.RequestUri!.OriginalString.Split("token=")[1], ["symbol"] = "ETH", ["decimals"] = 18, ["priceUSD"] = "3000" }.ToString()));
        var web3 = new Nethereum.Web3.Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.test/"), rpc));
        var p = new DefiPosition("layer", 8, wallet, "scrl", "LayerBank", "deposit", "ETH", "999", 999, DefiScrollLending.Native, DefiScrollLending.Core, "market");
        check("Scroll provider alias resolves configured RPC and fee network", DefiVault.Network("scrl").ChainId == 534352 && Rpc.Get("scrl") == Rpc.Scroll);
        check("Provider native IDs and Compound lending suffix normalize to addresses", DefiPositionsClient.AssetId("scrl", "scrl") == DefiScrollLending.Native && DefiPositionsClient.PoolId(DefiScrollLending.Comet + ":lending", "Compound V3") == DefiScrollLending.Comet);
        check("Unknown suffixes and malformed contracts remain untrusted", DefiPositionsClient.PoolId("bad:lending", "Compound V3") == "bad:lending" && DefiPositionsClient.PoolId(DefiScrollLending.Comet + ":other", "Compound V3")!.EndsWith(":other"));
        var q = await DefiWithdrawal.Prepare(web3, 534352, p, 0, priceClient: prices);
        check("LayerBank freezes live shares and simulates Core redemption", q.AmountRaw == "1000000000000000" && q.InputAmountRaw == "10000000" && q.Destination == DefiScrollLending.Core && lastTx.EndsWith(Word(10000000)));
        check("Scroll fee includes signature-size reserve and buffered L1 fee", q.L1FeeUsd == .00375m && q.FeeUsd == .00408m && feePayload.Count(c => c == 'f') >= 128);
        async Task Block(string name, DefiPosition position, string expected) {
            var blocked = false; try { await DefiWithdrawal.Prepare(web3, 534352, position, 0, priceClient: prices); }
            catch (InvalidOperationException ex) { blocked = ex.Message.Contains(expected); } check(name, blocked);
        }
        debt = true; await Block("LayerBank debt blocks collateral removal", p, "active debt"); debt = false;
        cash = false; await Block("Empty LayerBank liquidity explains protocol restriction", p, "no available underlying liquidity"); cash = true;
        check("LAB rewards explain their separate claim flow", DefiWithdrawal.Unavailable(p with { Type = "reward" })!.Contains("claim/vesting"));
        var reward = p with { Type = "reward", AssetAddress = DefiLayerBankRewards.Lab };
        check("Verified LAB reward offers an unlocked balance check", DefiWithdrawal.Unavailable(reward) == null);
        q = await DefiWithdrawal.Prepare(web3, 534352, reward, 0, priceClient: prices);
        check("LAB withdraw targets verified controller with only unlocked amount", q.Destination == DefiLayerBankRewards.Rewards && q.AmountRaw == unlocked.ToString() && lastTx.StartsWith(Selector("withdraw(uint256)")) && lastTx.EndsWith(Word(unlocked)));
        lockedLab = unlocked; unlocked = 0;
        await Block("Locked LAB never invokes an early exit or starts vesting", reward, "No unlocked LAB.s");
        unlocked = 1;
        var reducedReward = false;
        try { await DefiWithdrawal.Prepare(web3, 534352, reward, 0, "1000000000000000000", prices); }
        catch (InvalidOperationException ex) { reducedReward = ex.Message.Contains("decreased"); }
        check("LAB recheck blocks decreased unlocked rewards", reducedReward);
        p = p with { Protocol = "Compound V3", VaultAddress = DefiScrollLending.Comet, AssetAddress = "0x5300000000000000000000000000000000000004" };
        q = await DefiWithdrawal.Prepare(web3, 534352, p, 0, priceClient: prices);
        check("Compound uses live collateral balance and exact withdrawal", q.AmountRaw == collateral.ToString() && q.Destination == DefiScrollLending.Comet && lastTx.EndsWith(Word(collateral)));
        debt = true; await Block("Compound debt blocks collateral removal", p, "active debt"); debt = false;
        collateral = 1; await Block("Compound dust is blocked by fee comparison", p, "uneconomic");
        check("Aave Scroll market is recognized without ERC-4626 fallback", DefiLending.IsAave(p with { Protocol = "Aave V3", VaultAddress = "0x11fCfe756c05AD438e312a7fd934381537D3cFfe" }));
        check("Scroll previews never sign or send transactions", !methods.Any(m => m.Contains("sign") || m.StartsWith("eth_send")));
    }
}
