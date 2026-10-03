using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiCygnus
{
    public const string Wrapper = "0x5ae84075f0e34946821a8015dab5299a00992721";
    public const string Underlying = "0xca72827a3d211cfd8f6b00ac98824872b72cab49";
    public static bool Supported(DefiPosition p) => p.Chain == "base" && p.Protocol == "Cygnus Finance" && Wrapper.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost, string? exact = null, HttpClient? prices = null)
    {
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("stToken", [], ["address"]), ("balanceOf", ["address"], ["uint256"])), Wrapper);
        if (!Underlying.Equals(await SwapExecution.Read(contract.GetFunction("stToken").CallAsync<string>()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cygnus wrapper underlying changed");
        var balance = await SwapExecution.Read(contract.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var amount = exact == null ? balance : BigInteger.Parse(exact);
        if (amount <= 0 || balance < amount) throw new InvalidOperationException("No wcgUSD remains, or its balance decreased");
        var tx = DefiActionAbi.Build(p.Wallet, Wrapper, "unwrap(uint256)", [amount.ToString()]);
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, amount.ToString(), prices);
    }
}
