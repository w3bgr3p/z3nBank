using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiHana
{
    public const string Contract = "0xc5bf05cd32a14bffb705fb37a9d218895187376c";
    // Native Hana deposits use userEthDeposits(owner), isWithdrawable() and withdraw(address(0),amount).
    // Deployed selectors and historical withdrawal logs confirm the zero-address native-token argument.
    public static bool Supported(DefiPosition p) => p.Protocol == "Hana Network" &&
        p.Chain is "arb" or "base" or "matic" or "op" &&
        string.Equals(p.VaultAddress, Contract, StringComparison.OrdinalIgnoreCase) && TokenSelection.IsNative(p.AssetAddress ?? "");
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gas,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p) || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidOperationException("Hana deposit contract or network mismatch");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("isWithdrawable", [], ["bool"]),
            ("userEthDeposits", ["address"], ["uint256"])), Contract);
        if (!await SwapExecution.Read(contract.GetFunction("isWithdrawable").CallAsync<bool>()))
            throw new InvalidOperationException("Hana Network currently disables withdrawals at the contract level");
        var available = await SwapExecution.Read(contract.GetFunction("userEthDeposits").CallAsync<BigInteger>(p.Wallet));
        var amount = exact == null ? available : BigInteger.Parse(exact);
        if (amount <= 0 || amount > available) throw new InvalidOperationException("Hana on-chain deposit is empty or decreased since preview");
        var action = new RabbyWithdrawAction { Type = "withdraw", Contract = Contract, Function = "withdraw(address,uint256)()",
            Parameters = ["0x0000000000000000000000000000000000000000", amount.ToString()] };
        var quote = await DefiRabbyActions.Prepare(web3, chainId, p with { WithdrawActions = [action] }, gas, prices: prices);
        return quote with { InputAmountRaw = amount.ToString() };
    }
}
