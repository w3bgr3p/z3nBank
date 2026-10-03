using System.Numerics;
using Nethereum.Web3;
using Nethereum.Hex.HexTypes;

namespace z3nSafe;

public static class DefiActionApprovals
{
    public static async Task<List<DefiVault.Approval>> Prepare(Web3 web3, int chainId, DefiPosition p, RabbyWithdrawAction action, decimal gasPercent)
    {
        var steps = new List<DefiVault.Approval>();
        if (action.Approval?["to"] == null) return steps;
        var spender = (string)action.Approval["to"]!; var token = (string?)action.Approval["token_id"];
        if (!DefiActionTrust.Approval(p.Chain, spender) || !DefiVault.AddressValid(token) || TokenSelection.IsNative(token!))
            throw new InvalidDataException("Unverified approval token or spender");
        if (!BigInteger.TryParse((string?)action.Approval["str_raw_amount"], out var amount) || amount <= 0)
            throw new InvalidDataException("Approval lacks an exact positive str_raw_amount");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("allowance", ["address", "address"], ["uint256"]),
            ("balanceOf", ["address"], ["uint256"]), ("approve", ["address", "uint256"], ["bool"])), token!);
        var balance = await SwapExecution.Read(contract.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        if (balance < amount) throw new InvalidOperationException("Approval input exceeds the live receipt-token balance; refresh the preview");
        var allowance = await SwapExecution.Read(contract.GetFunction("allowance").CallAsync<BigInteger>(p.Wallet, spender));
        if (allowance >= amount) return steps;
        var amounts = allowance > 0 ? new[] { BigInteger.Zero, amount } : new[] { amount };
        foreach (var value in amounts) {
            var tx = contract.GetFunction("approve").CreateTransactionInput(p.Wallet, spender, value);
            // A reset followed by an exact approval is simulated together by Rabby. The latter may be rejected
            // against the current non-zero allowance, so reserve its standard upper bound before the reset.
            var gas = value != 0 && allowance > 0 ? new BigInteger(100000) :
                (await SwapExecution.Read(web3.Eth.Transactions.EstimateGas.SendRequestAsync(tx))).Value;
            var pricing = await DefiFees.Pricing(web3, chainId, tx, gas, gasPercent);
            gas = (pricing.Gas * 110 + 99) / 100;
            var price = pricing.Price;
            var l1 = await DefiFees.L1Fee(web3, chainId, tx, gas, price);
            steps.Add(new(token!, tx.Data, gas.ToString(), price.ToString(), l1.ToString()));
        }
        return steps;
    }
}
