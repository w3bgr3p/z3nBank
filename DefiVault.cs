using System.Numerics;
using Nethereum.Web3;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// ERC-4626 withdraw returns an exact underlying amount to the owner. No approval is needed.
public static class DefiVault
{
    private const string Abi = "[{\"type\":\"function\",\"name\":\"asset\",\"inputs\":[],\"outputs\":[{\"type\":\"address\"}],\"stateMutability\":\"view\"},{\"type\":\"function\",\"name\":\"maxWithdraw\",\"inputs\":[{\"name\":\"owner\",\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"view\"},{\"type\":\"function\",\"name\":\"withdraw\",\"inputs\":[{\"name\":\"assets\",\"type\":\"uint256\"},{\"name\":\"receiver\",\"type\":\"address\"},{\"name\":\"owner\",\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"nonpayable\"}]";
    public sealed record Quote(string Asset, string Symbol, int Decimals, string AmountRaw,
        decimal ValueUsd, decimal FeeUsd, string Gas, string GasPrice, string Data, decimal L1FeeUsd = 0)
    {
        public string? InputAmountRaw { get; init; }
        public string? Destination { get; init; }
        public List<Output>? Outputs { get; init; }
        public Approval? Approval { get; init; }
        public bool RequiresApprovalSimulation { get; init; }
    }
    public sealed record Output(string Asset, string Symbol, int Decimals, string AmountRaw, decimal ValueUsd);
    public sealed record Approval(string Destination, string Data, string Gas, string GasPrice);
    public static bool AddressValid(string? value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^0x[0-9a-fA-F]{40}$");
    public static (int ChainId, string Rpc) Network(string chain)
    {
        var name = chain switch { "eth" => "ethereum", "binance-smart-chain" => "bsc", "avax" => "avalanche",
            "xdai" => "gnosis", "matic" => "polygon", _ => chain };
        var id = Rpc.ChainId(name);
        // L2 withdrawals require chain-specific L1 fee estimates before automatic execution.
        if (id is not (1 or 10 or 56 or 100 or 137 or 43114 or 81457 or 42161 or 324 or 534352 or 8453 or 167000))
            throw new InvalidOperationException("This network needs an additional withdrawal fee adapter.");
        return (id, Rpc.Get(name));
    }
    public static async Task<Quote> Prepare(Web3 web3, int chainId, string vault, string wallet,
        string expectedAsset, decimal gasPercent, string? exactAmount = null, HttpClient? priceClient = null)
    {
        var actualChain = await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync());
        if (actualChain.Value != chainId) throw new InvalidOperationException("RPC chain does not match the position");
        var contract = web3.Eth.GetContract(Abi, vault);
        var asset = await SwapExecution.Read(contract.GetFunction("asset").CallAsync<string>());
        if (!string.Equals(asset, expectedAsset, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Contract underlying asset does not match this position; no withdrawal adapter is available");
        var max = await SwapExecution.Read(contract.GetFunction("maxWithdraw").CallAsync<BigInteger>(wallet));
        var amount = exactAmount == null ? max : BigInteger.Parse(exactAmount);
        if (amount <= 0 || max < amount) throw new InvalidOperationException("No withdrawal available now, or the available amount changed. The position may be locked.");
        var function = contract.GetFunction("withdraw");
        var tx = function.CreateTransactionInput(wallet, amount, wallet, wallet);
        return await PrepareTransaction(web3, chainId, asset, amount, tx, gasPercent, priceClient);
    }
    public static async Task<Quote> PrepareTransaction(Web3 web3, int chainId, string asset, BigInteger amount,
        TransactionInput tx, decimal gasPercent, HttpClient? priceClient = null,
        IReadOnlyList<(string Asset, BigInteger Amount)>? outputAmounts = null,
        BigInteger? reservedGas = null, Approval? approval = null)
    {
        // eth_call and estimateGas check the actual owner/receiver path before signing.
        // A verified LP adapter may reserve gas before its required approval. It is simulated again after approval.
        if (reservedGas == null) await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx));
        var gas = reservedGas == null ? await SwapExecution.Read(web3.Eth.Transactions.EstimateGas.SendRequestAsync(tx)) : new HexBigInteger(reservedGas.Value);
        var networkPrice = await SwapExecution.Read(web3.Eth.GasPrice.SendRequestAsync());
        var gasPrice = GasPricing.Price(networkPrice.Value, gasPercent);
        var gasLimit = (gas.Value * 110 + 99) / 100;
        var l1Fee = await DefiFees.L1Fee(web3, chainId, tx, gasLimit, gasPrice);
        var totalFee = gasLimit * gasPrice + l1Fee + (approval == null ? 0 : BigInteger.Parse(approval.Gas) * BigInteger.Parse(approval.GasPrice));
        using var ownedHttp = priceClient == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = priceClient ?? ownedHttp!;
        async Task<JObject> Price(string address)
        {
            using var response = await http.GetAsync($"https://li.quest/v1/token?chain={chainId}&token={address}", SwapExecution.Token);
            response.EnsureSuccessStatusCode();
            var info = JObject.Parse(await response.Content.ReadAsStringAsync(SwapExecution.Token));
            if ((int?)info["chainId"] != chainId || !string.Equals((string?)info["address"], address, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Price response does not match requested asset");
            return info;
        }
        var underlying = await Price(asset);
        var native = await Price("0x0000000000000000000000000000000000000000");
        var decimals = (int?)underlying["decimals"] ?? throw new InvalidDataException("Missing asset decimals");
        var usd = BalanceMath.GetValueUsd(amount.ToString(), decimals, (string?)underlying["priceUSD"] ?? "");
        var fee = BalanceMath.GetValueUsd(totalFee.ToString(), 18, (string?)native["priceUSD"] ?? "");
        var l1FeeUsd = BalanceMath.GetValueUsd(l1Fee.ToString(), 18, (string?)native["priceUSD"] ?? "");
        var outputs = new List<Output> { new(asset, (string?)underlying["symbol"] ?? "?", decimals, amount.ToString(), usd) };
        if (outputAmounts != null)
        {
            outputs.Clear();
            foreach (var output in outputAmounts)
            {
                var info = await Price(output.Asset);
                var precision = (int?)info["decimals"] ?? throw new InvalidDataException("Missing output decimals");
                outputs.Add(new Output(output.Asset, (string?)info["symbol"] ?? "?", precision, output.Amount.ToString(),
                    BalanceMath.GetValueUsd(output.Amount.ToString(), precision, (string?)info["priceUSD"] ?? "")));
            }
            usd = outputs.Sum(o => o.ValueUsd);
        }
        ValidateCosts(usd, fee);
        var balance = await SwapExecution.Read(web3.Eth.GetBalance.SendRequestAsync(tx.From));
        if (balance.Value < totalFee) throw new InvalidOperationException($"Insufficient native balance for withdrawal gas including L1 data fee: " +
            $"available {Web3.Convert.FromWei(balance.Value):0.##################}, required {Web3.Convert.FromWei(totalFee):0.##################}. Wrapped tokens cannot pay network gas.");
        return new Quote(asset, (string?)underlying["symbol"] ?? "?", decimals, amount.ToString(), usd, fee,
            gasLimit.ToString(), gasPrice.ToString(), tx.Data, l1FeeUsd) { Destination = tx.To, Outputs = outputAmounts == null ? null : outputs,
                Approval = approval, RequiresApprovalSimulation = reservedGas != null };
    }
    public static TransactionInput Transaction(Quote quote, string vault, string wallet) => new()
    {
        From = wallet, To = quote.Destination ?? vault, Data = quote.Data, Value = new HexBigInteger(0),
        Gas = new HexBigInteger(BigInteger.Parse(quote.Gas)), GasPrice = new HexBigInteger(BigInteger.Parse(quote.GasPrice))
    };
    public static void ValidateCosts(decimal outputUsd, decimal feeUsd)
    {
        if (outputUsd <= 0 || feeUsd <= 0) throw new InvalidOperationException("Cannot determine output value or network fee; withdrawal blocked");
        if (feeUsd >= outputUsd) throw new InvalidOperationException(FormattableString.Invariant($"Withdrawal fee ${feeUsd:0.########} exceeds output ${outputUsd:0.########}") + "; withdrawal is uneconomic and was not sent");
    }
    public static void ValidateRecheck(Quote preview, Quote fresh, decimal spentFeeUsd = 0)
    {
        ValidateCosts(fresh.ValueUsd, fresh.FeeUsd + spentFeeUsd);
        if ((fresh.InputAmountRaw ?? fresh.AmountRaw) != (preview.InputAmountRaw ?? preview.AmountRaw))
            throw new InvalidOperationException("Withdrawal input amount changed; preview again");
        if (!string.Equals(preview.Destination, fresh.Destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Withdrawal destination changed; preview again");
        if (fresh.FeeUsd + spentFeeUsd > preview.FeeUsd * 1.1m)
            throw new InvalidOperationException("Network fee increased by more than 10%; preview again");
        if (fresh.ValueUsd < preview.ValueUsd * .995m)
            throw new InvalidOperationException("Withdrawal output decreased; preview again");
        if (preview.Outputs != null && (fresh.Outputs == null || preview.Outputs.Any(old =>
            !fresh.Outputs.Any(next => next.Asset.Equals(old.Asset, StringComparison.OrdinalIgnoreCase) &&
                BigInteger.Parse(next.AmountRaw) >= BigInteger.Parse(old.AmountRaw)))))
            throw new InvalidOperationException("LP minimum output changed; preview again");
    }
}
