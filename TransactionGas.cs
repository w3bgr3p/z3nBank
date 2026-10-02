using System.Numerics;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace z3nSafe;

public static class TransactionGas
{
    public static void RequireBalance(BigInteger balance, BigInteger value, BigInteger gas, BigInteger price)
    {
        var required = value + gas * price;
        if (balance < required)
            throw new InvalidOperationException($"Insufficient native balance for transaction value and gas. Have {balance} wei; need at least {required} wei ({gas} gas at {price} wei/gas). Token USD value cannot pay native gas.");
    }
    public static async Task<HexBigInteger> EstimateAsync(Web3 web3, TransactionInput input)
    {
        var balance = await SwapExecution.Read(web3.Eth.GetBalance.SendRequestAsync(input.From));
        var value = input.Value?.Value ?? BigInteger.Zero;
        var price = input.GasPrice?.Value ?? throw new InvalidOperationException("Missing gas price");
        RequireBalance(balance.Value, value, 21000, price);
        HexBigInteger estimate;
        try { estimate = await SwapExecution.Read(web3.Eth.Transactions.EstimateGas.SendRequestAsync(input)); }
        catch (Exception ex) when (ex is not OperationCanceledException &&
            ex.Message.Contains("gas required exceeds allowance", StringComparison.OrdinalIgnoreCase))
        {
            var budget = price > 0 ? BigInteger.Max(0, balance.Value - value) / price : BigInteger.Zero;
            throw new InvalidOperationException($"eth_estimateGas cannot fit the transaction into the available native gas budget: {budget} gas; balance={balance.Value} wei, value={value} wei, gasPrice={price} wei. {SwapExecution.ErrorDetails(ex)}", ex);
        }
        var limit = (estimate.Value * 110 + 99) / 100;
        RequireBalance(balance.Value, value, limit, price);
        return new HexBigInteger(limit);
    }
}
