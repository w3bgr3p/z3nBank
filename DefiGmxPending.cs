using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiGmxPending
{
    public sealed record Request(string Key, BigInteger Shares);
    public static async Task<List<Request>> Requests(Web3 web3, DefiPosition p)
    {
        async Task<string> Read(string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, DefiGmxMarkets.Store, signature, args)));
        var accountKey = DefiGmxMarkets.Hash("key(bytes32,address)", DefiGmxMarkets.Key("ACCOUNT_WITHDRAWAL_LIST"), p.Wallet);
        var count = DefiLending.Word(await Read("getBytes32Count(bytes32)", accountKey), 0);
        if (count < 0 || count > 100) throw new InvalidDataException("Too many GMX requests; reduce the account queue");
        var requests = new List<Request>();
        if (count == 0) return requests;
        var keys = await Read("getBytes32ValuesAt(bytes32,uint256,uint256)", accountKey, "0", count.ToString());
        var start = checked((int)DefiLending.Word(keys, 0) / 32);
        if (DefiLending.Word(keys, start) != count) throw new InvalidDataException("GMX request list changed; refresh again");
        for (int i = 0; i < (int)count; i++) {
            var key = "0x" + keys.Substring(2 + (start + 1 + i) * 64, 64);
            string Field(string name) => DefiGmxMarkets.Hash("key(bytes32,bytes32)", key, DefiGmxMarkets.Key(name));
            var owner = DefiNftLiquidity.Address(await Read("getAddress(bytes32)", Field("ACCOUNT")), 0);
            if (!owner.Equals(p.Wallet, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GMX request owner mismatch");
            var market = DefiNftLiquidity.Address(await Read("getAddress(bytes32)", Field("MARKET")), 0);
            if (market.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase))
                requests.Add(new Request(key, DefiLending.Word(await Read("getUint(bytes32)", Field("MARKET_TOKEN_AMOUNT")), 0)));
        }
        return requests;
    }
    public static async Task<DefiVault.Quote> Cancel(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        var requests = await Requests(web3, p);
        var request = exact == null ? requests.FirstOrDefault() : requests.SingleOrDefault(r => "cancel:" + r.Key == exact);
        if (request == null || request.Shares <= 0) throw new InvalidOperationException("GMX request is already executed or cancelled. Refresh this account; no cancellation was sent.");
        async Task<string> Read(string target, string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, args)));
        var marketData = await Read(DefiGmxMarkets.Reader, "getMarket(address,address)", DefiGmxMarkets.Store, p.VaultAddress!);
        var tokens = Enumerable.Range(0, 4).Select(i => DefiNftLiquidity.Address(marketData, i)).ToArray();
        if (!tokens[0].Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GMX market mismatch");
        var tx = DefiActionAbi.Build(p.Wallet, DefiGmxMarkets.Router, "cancelWithdrawal(bytes32)", [request.Key]);
        try { await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx)); }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex) { throw new InvalidOperationException("GMX currently rejects cancellation: its delay may not have elapsed or the request may have been executed. No cancellation was sent. " + SwapExecution.ErrorDetails(ex), ex); }
        // Value the returned GM using current reader amounts and exact underlying prices, never a cached portfolio value.
        var amounts = await DefiGmxMarkets.EstimateOutputs(web3, p, tokens, request.Shares);
        using var owned = prices == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = prices ?? owned!; decimal value = 0;
        for (int i = 0; i < 2; i++) {
            var amount = i == 0 ? amounts.Long : amounts.Short;
            if (amount == 0) continue;
            using var response = await http.GetAsync($"https://li.quest/v1/token?chain={chainId}&token={tokens[i + 2]}", SwapExecution.Token);
            response.EnsureSuccessStatusCode(); var info = JObject.Parse(await response.Content.ReadAsStringAsync(SwapExecution.Token));
            if ((int?)info["chainId"] != chainId || !tokens[i + 2].Equals((string?)info["address"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GMX underlying price identity mismatch");
            value += BalanceMath.GetValueUsd(amount.ToString(), (int)info["decimals"]!, (string)info["priceUSD"]!);
        }
        var decimals = checked((int)DefiLending.Word(await Read(tokens[0], "decimals()"), 0));
        if (decimals > 28 || value <= 0) throw new InvalidDataException("Cannot value the returned GM tokens");
        var unitPrice = value / ((decimal)request.Shares / (decimal)BigInteger.Pow(10, decimals));
        var marketPrice = new JObject { ["chainId"] = chainId, ["address"] = tokens[0], ["symbol"] = "GM", ["decimals"] = decimals,
            ["priceUSD"] = unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return (await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, "cancel:" + request.Key, prices,
            verifiedPrices: new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase) { [tokens[0]] = marketPrice })) with {
            Stage = "cancel", CompletesPending = requests.Count == 1,
            Notice = "Cancels this pending request and returns GM market tokens to this wallet, not the underlying coins. The unused keeper payment is refunded after protocol cancellation costs."
        };
    }
}
