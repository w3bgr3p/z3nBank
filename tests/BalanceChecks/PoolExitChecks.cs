using System.Net;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Nethereum.Util;
using z3nSafe;

internal static class PoolExitChecks
{
    private sealed class Handler(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            new(HttpStatusCode.OK) { Content = new StringContent(respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token))) };
    }
    public static async Task Run(Action<string, bool> check)
    {
        const string wallet = "0x1111111111111111111111111111111111111111", a = "0x2222222222222222222222222222222222222222", b = "0x3333333333333333333333333333333333333333";
        const string lp = "0x4444444444444444444444444444444444444444";
        string Word(BigInteger n) => Convert.ToHexString(n.ToByteArray(true, true)).PadLeft(64, '0').ToLowerInvariant();
        string Address(string s) => s[2..].ToLowerInvariant().PadLeft(64, '0');
        string Selector(string s) => "0x" + Sha3Keccack.Current.CalculateHash(s)[..8];
        var chain = 56; var balance = new BigInteger(10000000); var approved = true; var changed = false;
        var calls = new List<string>(); var burns = new List<string>();
        string Call(JObject request)
        {
            var tx = request["params"]![0]!; var data = (string)tx["data"]!; var to = (string)tx["to"]!;
            bool Is(string sig) => data.StartsWith(Selector(sig));
            if (Is("vaultTokenAddress(address)")) return "0x" + Address(lp);
            if (Is("balance(address,address)")) return "0x" + Word(1000000);
            if (Is("lastDepositBlock(address)")) return "0x" + Word(10);
            if (Is("withdrawBlockWait()")) return "0x" + Word(10);
            if (Is("balanceOf(address)")) return "0x" + Word(chain == 56 ? balance : data.EndsWith(Address(lp)) ? 0 : 1000000);
            if (Is("withdraw(address,uint256)")) { burns.Add(data); return "0x"; }
            if (Is("poolType()")) return "0x" + Word(changed ? 2 : 1);
            if (Is("master()")) return "0x" + Address(DefiSyncSwap.Master);
            if (Is("vault()")) return "0x" + Address(DefiSyncSwap.Vault);
            if (Is("token0()")) return "0x" + Address(a);
            if (Is("token1()")) return "0x" + Address(b);
            if (Is("getPool(address,address)")) return "0x" + Address(lp);
            if (Is("allowance(address,address)")) return "0x" + Word(approved ? 1000000 : 0);
            if (Is("burnLiquidity(address,uint256,bytes,uint256[],address,bytes)"))
            { burns.Add(data); return "0x" + Word(32) + Word(2) + Address(a) + Word(100000) + Address(b) + Word(100000); }
            if (Is("balanceOf(address,address)")) return "0x" + Word(1000000);
            if (Is("totalSupply()")) return "0x" + Word(10000000);
            if (Is("getFeeRecipient()")) return "0x" + Word(0);
            if (Is("invariantLast()")) return "0x" + Word(0);
            if (Is("approve(address,uint256)")) { burns.Add(data); return "0x" + Word(1); }
            throw new InvalidOperationException(data + " " + to);
        }
        using var rpc = new HttpClient(new Handler((_, text) => {
            var req = JObject.Parse(text); var method = (string)req["method"]!; calls.Add(method);
            var result = method switch {
                "eth_chainId" => "0x" + chain.ToString("x"), "eth_blockNumber" => "0x64", "eth_gasPrice" => "0xf4240",
                "eth_estimateGas" => "0x186a0", "eth_getBalance" => "0xde0b6b3a7640000",
                "eth_getStorageAt" => "0x" + Address(changed ? wallet : DefiBlackwing.Implementation),
                "eth_call" => Call(req), _ => throw new InvalidOperationException(method) };
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = req["id"], ["result"] = result }.ToString();
        }));
        using var prices = new HttpClient(new Handler((r, _) => {
            var address = r.RequestUri!.OriginalString.Split("token=")[1]; var native = address.EndsWith(new string('0', 40));
            return new JObject { ["chainId"] = chain, ["address"] = address, ["symbol"] = native ? "ETH" : "TEST", ["decimals"] = native ? 18 : 6, ["priceUSD"] = native ? "3000" : "1" }.ToString();
        }));
        var web3 = new Nethereum.Web3.Web3(new Nethereum.JsonRpc.Client.RpcClient(new Uri("https://rpc.test/"), rpc));
        var p = new DefiPosition("blackwing", 18, wallet, "bsc", "Blackwing", "staked", "TEST", "999", 999, a, DefiBlackwing.Vault, "pool");
        var q = await DefiWithdrawal.Prepare(web3, chain, p, 0, priceClient: prices);
        check("Blackwing burns live shares and quotes live underlying, never cached amounts", q.InputAmountRaw == "10000000" && q.AmountRaw == "1000000" && burns.Last().EndsWith(Word(balance)));
        check("Blackwing batches retain separate assets within one vault", DefiBatch.Select(new[] { p, p with { Id = "second", AssetAddress = b } }, "Blackwing", [18]).Count == 2);
        changed = true; var blocked = false;
        try { await DefiWithdrawal.Prepare(web3, chain, p, 0, priceClient: prices); } catch (InvalidOperationException ex) { blocked = ex.Message.Contains("implementation changed"); }
        check("Blackwing proxy upgrades block unverified withdrawal", blocked); changed = false;
        balance = 9999999; blocked = false;
        try { await DefiWithdrawal.Prepare(web3, chain, p, 0, "10000000", prices); } catch (InvalidOperationException) { blocked = true; }
        check("Blackwing execution refuses reduced share balance", blocked);
        await CheckSyncSwap(check, web3, prices, p, () => approved = false, () => changed = true, calls, burns, Word);
        async Task CheckSyncSwap(Action<string, bool> report, Nethereum.Web3.Web3 w, HttpClient priceHttp, DefiPosition original, Action noApproval, Action wrongPool,
            List<string> methods, List<string> calldata, Func<BigInteger, string> word)
        {
            chain = 324; p = original with { Chain = "era", Protocol = "SyncSwap", Type = "deposit", VaultAddress = lp };
            q = await DefiWithdrawal.Prepare(w, chain, p, 0, priceClient: priceHttp);
            report("SyncSwap previews both protected outputs and targets router", q.Outputs?.Count == 2 && q.ValueUsd == .199m && q.InputAmountRaw == "1000000" && DefiVault.Transaction(q, lp, wallet).To == DefiSyncSwap.Router);
            report("SyncSwap burn encodes recipient, wrapped mode and both minima", calldata.Last().Contains(Address(wallet) + word(2)) && calldata.Last().Contains(word(99500) + word(99500)));
            void Reject(string name, DefiVault.Quote fresh, decimal spent = 0)
            {
                var refused = false;
                try { DefiVault.ValidateRecheck(q, fresh, spent); } catch (InvalidOperationException) { refused = true; }
                report(name, refused);
            }
            Reject("LP execution rejects changed input shares", q with { InputAmountRaw = "999999" });
            Reject("LP execution rejects changed router", q with { Destination = wallet });
            Reject("LP execution rejects reduced protected output", q with { Outputs = [q.Outputs![0] with { AmountRaw = "99499" }, q.Outputs[1]] });
            Reject("Post-approval guard includes already spent approval fee", q, q.FeeUsd);
            Reject("Post-approval guard blocks combined fee above received value", q, q.ValueUsd);
            DefiVault.ValidateRecheck(q, q);
            report("An unchanged LP plan passes the execution recheck", true);
            report("LP batch deduplicates both underlying legs into one withdrawal", DefiBatch.Select(new[] { p, p with { Id = "other", AssetAddress = b } }, "SyncSwap", [18]).Count == 1);
            noApproval(); q = await DefiWithdrawal.Prepare(w, chain, p, 0, priceClient: priceHttp);
            report("Missing allowance previews exact approval and includes both transaction fees", q.RequiresApprovalSimulation && q.Approval?.Destination == lp && q.Approval.Data.EndsWith(word(1000000)) && q.FeeUsd == .01023m && q.Outputs?.Count == 2);
            wrongPool(); blocked = false;
            try { await DefiWithdrawal.Prepare(w, chain, p, 0, priceClient: priceHttp); } catch (InvalidOperationException) { blocked = true; }
            report("SyncSwap refuses stable or unverified pools", blocked);
            report("Pool previews never sign or broadcast", !methods.Any(m => m.StartsWith("eth_send") || m.Contains("sign")));
        }
    }
}
