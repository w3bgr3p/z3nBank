using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiLido
{
    public const string Token = "0xae7ab96520de3a18e5e111b5eaab095312d7fe84";
    public const string Queue = "0x889edc2edab5f40e902b864ad4d7ade8e412f9b1";
    private const string Native = "0x0000000000000000000000000000000000000000";
    public static bool Supported(DefiPosition p) => p.Chain == "eth" && p.Protocol == "LIDO" && Token.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, method, args)));
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("getWithdrawalRequests", ["address"], ["uint256[]"]),
            ("findCheckpointHints", ["uint256[]", "uint256", "uint256"], ["uint256[]"]),
            ("getClaimableEther", ["uint256[]", "uint256[]"], ["uint256[]"])), Queue);
        var ids = await SwapExecution.Read(contract.GetFunction("getWithdrawalRequests").CallAsync<List<BigInteger>>(p.Wallet));
        if (ids.Count > 1000) throw new InvalidDataException("Too many Lido requests; reduce the claim batch");
        if (ids.Count > 0) {
            var status = await Read(Queue, "getWithdrawalStatus(uint256[])", new JArray(ids.Select(i => i.ToString())).ToString());
            int start = checked((int)DefiLending.Word(status, 0) / 32); int count = checked((int)DefiLending.Word(status, start));
            if (count != ids.Count) throw new InvalidDataException("Lido request status length mismatch");
            var mature = new List<BigInteger>();
            for (int i = 0; i < count; i++) {
                int word = start + 1 + i * 6;
                if (!p.Wallet.Equals(DefiNftLiquidity.Address(status, word + 2), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Lido request owner mismatch");
                if (DefiLending.Word(status, word + 4) == 1 && DefiLending.Word(status, word + 5) == 0) mature.Add(ids[i]);
            }
            if (mature.Count == 0) throw new InvalidOperationException("Lido withdrawal awaits protocol finalization. Check withdrawal again once its requests are claimable.");
            var last = DefiLending.Word(await Read(Queue, "getLastCheckpointIndex()"), 0);
            var hints = await SwapExecution.Read(contract.GetFunction("findCheckpointHints").CallAsync<List<BigInteger>>(mature.ToArray(), BigInteger.One, last));
            var amounts = await SwapExecution.Read(contract.GetFunction("getClaimableEther").CallAsync<List<BigInteger>>(mature.ToArray(), hints.ToArray()));
            var amount = amounts.Aggregate(BigInteger.Zero, (sum, n) => sum + n);
            var key = "claim:" + string.Join(",", mature);
            if (exact != null && exact != key) throw new InvalidOperationException("Lido mature requests changed; preview again");
            var tx = DefiActionAbi.Build(p.Wallet, Queue, "claimWithdrawals(uint256[],uint256[])",
                [new JArray(mature.Select(i => i.ToString())).ToString(), new JArray(hints.Select(i => i.ToString())).ToString()]);
            return (await DefiVault.PrepareTransaction(web3, chainId, Native, amount, tx, boost, prices)) with { InputAmountRaw = key, CompletesPending = mature.Count == ids.Count };
        }
        var balance = DefiLending.Word(await Read(Token, "balanceOf(address)", p.Wallet), 0);
        if (balance < 100) throw new InvalidOperationException("No stETH balance above Lido's minimum request amount remains");
        var max = new BigInteger(1000) * BigInteger.Pow(10, 18); var parts = new JArray(); var left = balance;
        while (left > 0) { var part = BigInteger.Min(left, max); if (part < 100) break; parts.Add(part.ToString()); left -= part; if (parts.Count > 100) throw new InvalidOperationException("Lido request exceeds 100 chunks"); }
        var input = "request:" + (balance - left);
        if (exact != null && exact != input) throw new InvalidOperationException("stETH balance changed; preview again");
        var request = DefiActionAbi.Build(p.Wallet, Queue, "requestWithdrawals(uint256[],address)", [parts.ToString(), p.Wallet]);
        var action = new RabbyWithdrawAction { Approval = new JObject { ["token_id"] = Token, ["to"] = Queue, ["str_raw_amount"] = (balance - left).ToString() } };
        var quote = await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, request, input, prices, action: action,
            futureOutputs: new[] { (Native, balance - left) });
        return quote with { Stage = "request", Notice = "Queues stETH for withdrawal and receives withdrawal NFTs. ETH arrives only after finalization and a separate claim; the final amount can be reduced by protocol losses." };
    }
}
