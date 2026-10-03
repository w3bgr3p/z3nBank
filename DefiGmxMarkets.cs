using System.Numerics;
using Nethereum.Web3;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiGmxMarkets
{
    public const string Store = "0xfd70de6b91282d8017aa4e741e9ae325cab992d8";
    public const string Reader = "0xfa26cbb46e2614609406de08ca1dc7f70a684184";
    public const string Router = "0x7de39ff2e232a2203196788d37e234cf8f1b83f1";
    public const string Spender = "0x7452c558d45f8afc8c83dae62c3f8a5be19c71f6";
    public const string Vault = "0x0628d46b5d145f183adb6ef1f2c97ed1c4701c55";
    private const string Zero = "0x0000000000000000000000000000000000000000";
    public static bool Supported(DefiPosition p) => p.Protocol == "GMX V2" && p.Chain == "arb" && p.AdapterId == "gmx2_liquidity";
    internal static string Hash(string signature, params string[] args) => "0x" + Convert.ToHexString(Nethereum.Util.Sha3Keccack.Current.CalculateHash(
        Convert.FromHexString(DefiActionAbi.Build(Zero, Zero, signature, args).Data[10..]))).ToLowerInvariant();
    internal static string Key(string name) => Hash("key(string)", name);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, method, args)));
        async Task<BigInteger> Uint(string name) => DefiLending.Word(await Read(Store, "getUint(bytes32)", Key(name)), 0);
        if (!Store.Equals(DefiNftLiquidity.Address(await Read(Router, "dataStore()"), 0), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GMX ExchangeRouter data store mismatch");
        var marketData = await Read(Reader, "getMarket(address,address)", Store, p.VaultAddress!);
        var tokens = Enumerable.Range(0, 4).Select(i => DefiNftLiquidity.Address(marketData, i)).ToArray();
        if (!tokens[0].Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase) || tokens.Skip(2).Any(t => !DefiVault.AddressValid(t) || TokenSelection.IsNative(t)))
            throw new InvalidDataException("GMX market is missing from its verified reader");
        var pending = await DefiGmxPending.Requests(web3, p);
        if (pending.Count > 0) throw new InvalidOperationException("GMX withdrawal is awaiting keeper execution. No second request was created. Refresh this account to check completion, or use Cancel request to recover the GM tokens after the protocol cancellation delay.");
        var balance = DefiLending.Word(await Read(tokens[0], "balanceOf(address)", p.Wallet), 0);
        var input = exact == null ? null : JObject.Parse(exact);
        var amount = input == null ? balance : BigInteger.Parse((string)input["shares"]!);
        if (amount <= 0 || balance < amount) throw new InvalidOperationException("No GM market tokens remain, or their balance decreased");
        var (longAmount, shortAmount) = await EstimateOutputs(web3, p, tokens, amount);
        var gasPrice = GasPricing.Price((await SwapExecution.Read(web3.Eth.GasPrice.SendRequestAsync())).Value, boost);
        var limit = await Uint("WITHDRAWAL_GAS_LIMIT"); var multiplier = await Uint("ESTIMATED_GAS_FEE_MULTIPLIER_FACTOR");
        if (limit <= 0 || multiplier <= 0) throw new InvalidDataException("GMX execution fee configuration is unavailable");
        var fee = (await Uint("ESTIMATED_GAS_FEE_BASE_AMOUNT_V2_1") + 3 * await Uint("ESTIMATED_GAS_FEE_PER_ORACLE_PRICE") + limit * multiplier / BigInteger.Pow(10, 30)) * gasPrice;
        // Reserve the complete keeper payment and freeze it across the approval/execute recheck.
        if (input != null && fee > BigInteger.Parse((string)input["keeperFee"]!)) throw new InvalidOperationException("GMX keeper fee increased beyond the preview; preview again");
        fee = input == null ? (fee * 125 + 99) / 100 : BigInteger.Parse((string)input["keeperFee"]!);
        var plan = input ?? new JObject { ["shares"] = amount.ToString(), ["keeperFee"] = fee.ToString(),
            ["minLong"] = (longAmount * 995 / 1000).ToString(), ["minShort"] = (shortAmount * 995 / 1000).ToString() };
        if (longAmount < BigInteger.Parse((string)plan["minLong"]!) || shortAmount < BigInteger.Parse((string)plan["minShort"]!))
            throw new InvalidOperationException("GMX estimated withdrawal fell below its protected minimum; preview again");
        return await Request(web3, chainId, p, boost, prices, tokens, amount, fee, plan, longAmount, shortAmount);
    }
    internal static async Task<(BigInteger Long, BigInteger Short)> EstimateOutputs(Web3 web3, DefiPosition p, string[] tokens, BigInteger amount)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync("https://arbitrum-api.gmxinfra.io/prices/tickers", SwapExecution.Token);
        response.EnsureSuccessStatusCode(); var tickers = JArray.Parse(await response.Content.ReadAsStringAsync(SwapExecution.Token));
        JArray Price(string address) {
            if (TokenSelection.IsNative(address)) return new JArray("0", "0");
            var ticker = tickers.SingleOrDefault(t => address.Equals((string?)t["tokenAddress"], StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("GMX oracle quote is missing " + address);
            var timestamp = (long?)ticker["timestamp"] ?? 0;
            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 120) throw new InvalidDataException("GMX oracle prices are stale");
            var min = BigInteger.Parse((string)ticker["minPrice"]!); var max = BigInteger.Parse((string)ticker["maxPrice"]!);
            if (min <= 0 || max < min) throw new InvalidDataException("Invalid GMX oracle price range");
            return new JArray(min.ToString(), max.ToString());
        }
        var tx = DefiActionAbi.Build(p.Wallet, Reader,
            "getWithdrawalAmountOut(address,(address,address,address,address),((uint256,uint256),(uint256,uint256),(uint256,uint256)),uint256,address,uint8)",
            [Store, new JArray(tokens).ToString(), new JArray(tokens.Skip(1).Select(Price)).ToString(), amount.ToString(), Zero, "4"]);
        var result = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx));
        return (DefiLending.Word(result, 0), DefiLending.Word(result, 1));
    }
    private static async Task<DefiVault.Quote> Request(Web3 web3, int chainId, DefiPosition p, decimal boost,
        HttpClient? prices, string[] tokens, BigInteger amount, BigInteger fee, JObject plan, BigInteger longAmount, BigInteger shortAmount)
    {
        var addresses = new JArray(p.Wallet, Zero, Zero, tokens[0], new JArray(), new JArray());
        var parameters = new JArray(addresses, (string)plan["minLong"]!, (string)plan["minShort"]!, true, fee.ToString(), "0", new JArray());
        var calls = new JArray {
            DefiActionAbi.Build(p.Wallet, Router, "sendWnt(address,uint256)", [Vault, fee.ToString()]).Data,
            DefiActionAbi.Build(p.Wallet, Router, "sendTokens(address,address,uint256)", [tokens[0], Vault, amount.ToString()]).Data,
            DefiActionAbi.Build(p.Wallet, Router, "createWithdrawal(((address,address,address,address,address[],address[]),uint256,uint256,bool,uint256,uint256,bytes32[]))", [parameters.ToString()]).Data
        };
        var tx = DefiActionAbi.Build(p.Wallet, Router, "multicall(bytes[])", [calls.ToString()]); tx.Value = new HexBigInteger(fee);
        var action = new RabbyWithdrawAction { Approval = new JObject { ["token_id"] = tokens[0], ["to"] = Spender, ["str_raw_amount"] = amount.ToString() } };
        var outputs = new List<(string Asset, BigInteger Amount)>();
        string OutputAsset(string token) => token.Equals("0x82af49447d8a07e3bd95bd0d56f35241523fbab1", StringComparison.OrdinalIgnoreCase) ? Zero : token;
        if (longAmount > 0) outputs.Add((OutputAsset(tokens[2]), longAmount)); if (shortAmount > 0) outputs.Add((OutputAsset(tokens[3]), shortAmount));
        var quote = await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, plan.ToString(Newtonsoft.Json.Formatting.None), prices,
            action: action, futureOutputs: outputs);
        return quote with { Stage = "request", Notice = "Queues the full GM market balance with protected output minima. The keeper executes later and sends both underlying tokens to this wallet. The fee estimate includes the entire keeper payment; unused execution fee may be refunded." };
    }
}
