using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

public static class SwapCostGuard
{
    public sealed record Evaluation(bool Allowed, decimal ReceivedUSD, decimal FeesUSD, string Reason);
    private static decimal Money(string? text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidDataException("Missing or invalid fee/price data");
        return value;
    }
    public static Evaluation Compare(decimal received, decimal fees) => received <= 0 || fees < 0
        ? new(false, received, fees, "Cannot establish a positive minimum output and non-negative fees")
        : new(fees <= received, received, fees, fees > received
            ? $"Fees ${fees:F4} exceed minimum output ${received:F4}" : $"Fees ${fees:F4}; minimum output ${received:F4}");

    public static async Task<Evaluation> CheckAsync(object quote, Web3 web3, string wallet)
    {
        var initial = Evaluate(quote);
        if (!initial.Allowed) return initial;
        try
        {
            var boostedPrice = GasPricing.Price((await web3.Eth.GasPrice.SendRequestAsync()).Value);
            decimal gasUSD;
            if (quote is LiFiBridge.QuoteResponse lifi)
            {
                if (lifi.Action.FromChainId != lifi.Action.ToChainId || !TokenSelection.IsNative(lifi.Action.ToToken.Address))
                    throw new InvalidDataException("Swap guard requires native output in the source network");
                var native = lifi.Action.ToToken;
                BigInteger units = 0;
                foreach (var gas in lifi.Estimate.GasCosts)
                {
                    if (gas.Token == null || gas.Token.ChainId != lifi.Action.FromChainId || !TokenSelection.IsNative(gas.Token.Address) ||
                        !BigInteger.TryParse(gas.Limit ?? gas.Estimate, out var limit) || limit <= 0)
                        throw new InvalidDataException("Quote lacks usable gas limits for all steps");
                    units += (limit * 110 + 99) / 100;
                }
                if (!lifi.Estimate.GasCosts.Any(g => string.Equals(g.Type, "APPROVE", StringComparison.OrdinalIgnoreCase)))
                {
                    const string abi = "[{\"name\":\"allowance\",\"type\":\"function\",\"inputs\":[{\"name\":\"owner\",\"type\":\"address\"},{\"name\":\"spender\",\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"view\"},{\"name\":\"approve\",\"type\":\"function\",\"inputs\":[{\"name\":\"spender\",\"type\":\"address\"},{\"name\":\"amount\",\"type\":\"uint256\"}],\"outputs\":[{\"type\":\"bool\"}],\"stateMutability\":\"nonpayable\"}]";
                    var contract = web3.Eth.GetContract(abi, lifi.Action.FromToken.Address);
                    var amount = BigInteger.Parse(lifi.Action.FromAmount);
                    var allowance = await contract.GetFunction("allowance").CallAsync<BigInteger>(wallet, lifi.Estimate.ApprovalAddress);
                    if (allowance < amount)
                    {
                        var approval = new CallInput { From = wallet, To = lifi.Action.FromToken.Address,
                            Data = contract.GetFunction("approve").GetData(lifi.Estimate.ApprovalAddress, amount) };
                        var gas = await web3.Eth.Transactions.EstimateGas.SendRequestAsync(approval);
                        units += (gas.Value * 110 + 99) / 100;
                    }
                }
                gasUSD = BalanceMath.GetValueUsd((units * boostedPrice).ToString(), native.Decimals, native.PriceUSD);
                if (gasUSD <= 0) throw new InvalidDataException("Cannot price current network gas");
            }
            else
            {
                var relay = (RelayBridge.QuoteResponse)quote;
                var prices = relay.Steps.Where(s => s.Kind == "transaction").SelectMany(s => s.Items)
                    .Select(i => i.Data ?? throw new InvalidDataException("Missing transaction data"))
                    .Select(t => BigInteger.Parse(t.MaxFeePerGas ?? t.GasPrice ?? throw new InvalidDataException("Missing quoted gas price"))).ToList();
                if (prices.Count == 0 || prices.Any(p => p <= 0)) throw new InvalidDataException("Invalid quoted gas price");
                var ratio = Math.Max(GasPricing.CostMultiplier, (decimal)boostedPrice / (decimal)prices.Min() * 1.1m);
                gasUSD = Money((string?)JObject.FromObject(relay.Fees)["gas"]?["amountUsd"]) * ratio;
            }
            return Evaluate(quote, gasUSD);
        }
        catch (Exception ex) { return new(false, initial.ReceivedUSD, initial.FeesUSD, $"Cannot verify current gas/approval costs: {ex.Message}"); }
    }

    public static Evaluation Evaluate(object quote, decimal? originGasUSD = null)
    {
        try
        {
            decimal received, fees;
            if (quote is LiFiBridge.QuoteResponse lifi)
            {
                if (lifi.Estimate?.GasCosts == null || lifi.Estimate.GasCosts.Count == 0 || lifi.Estimate.FeeCosts == null)
                    throw new InvalidDataException("Quote lacks full gas and route fee estimates");
                var output = lifi.Action?.ToToken ?? throw new InvalidDataException("Quote lacks output token");
                if (string.IsNullOrEmpty(lifi.Estimate.ToAmountMin) || Money(output.PriceUSD) <= 0)
                    throw new InvalidDataException("Quote lacks minimum output or output price");
                received = BalanceMath.GetValueUsd(lifi.Estimate.ToAmountMin, output.Decimals, output.PriceUSD);
                var gas = lifi.Estimate.GasCosts.Sum(g => Money(g.AmountUSD)) * GasPricing.CostMultiplier;
                fees = Math.Max(gas, originGasUSD ?? gas) + lifi.Estimate.FeeCosts.Sum(f => Money(f.AmountUSD));
            }
            else if (quote is RelayBridge.QuoteResponse relay)
            {
                var details = JObject.FromObject(relay.Details ?? throw new InvalidDataException("Quote lacks output details"));
                var output = details["currencyOut"] ?? throw new InvalidDataException("Quote lacks output currency");
                var amount = Money((string?)output["amount"]);
                var minimum = Money((string?)output["minimumAmount"]);
                var outputChain = (int?)output["currency"]?["chainId"];
                if (!TokenSelection.IsNative((string?)output["currency"]?["address"] ?? "") || outputChain is null or <= 0 ||
                    outputChain != (int?)details["currencyIn"]?["currency"]?["chainId"])
                    throw new InvalidDataException("Quote output must be native in the source network");
                if (amount <= 0 || minimum <= 0 || minimum > amount)
                    throw new InvalidDataException("Quote lacks valid minimum output");
                received = Money((string?)output["amountUsd"]) * minimum / amount;
                var costs = JObject.FromObject(relay.Fees ?? throw new InvalidDataException("Quote lacks fees"));
                var gas = Money((string?)costs["gas"]?["amountUsd"]) * GasPricing.CostMultiplier;
                if (gas <= 0) throw new InvalidDataException("Quote lacks a positive origin gas estimate");
                var relayer = costs["relayer"] != null ? Money((string?)costs["relayer"]!["amountUsd"])
                    : (costs["relayerGas"] == null ? 0 : Money((string?)costs["relayerGas"]!["amountUsd"])) +
                      (costs["relayerService"] == null ? 0 : Money((string?)costs["relayerService"]!["amountUsd"]));
                var app = costs["app"] == null ? 0 : Money((string?)costs["app"]!["amountUsd"]);
                if (costs.Properties().Any(p => p.Name is not ("gas" or "relayer" or "relayerGas" or "relayerService" or "app" or "subsidized")))
                    throw new InvalidDataException("Quote contains unsupported fee components");
                fees = Math.Max(gas, originGasUSD ?? gas) + relayer + app;
            }
            else throw new InvalidDataException("Unsupported quote type");
            return Compare(received, fees);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(false, 0, 0, $"Cannot verify swap costs: {ex.Message}");
        }
    }
}
