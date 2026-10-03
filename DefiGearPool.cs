using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiGearPool
{
    private static readonly HashSet<string> Pools = new(StringComparer.OrdinalIgnoreCase) {
        "0x24946bcbbd028d5abb62ad9b635eb1b1a67af668", "0x86130bdd69143d8a4e5fc50bf4323d48049e98e4",
        "0xb03670c20f87f2169a7c4ebe35746007e9575901", "0xb2a015c71c17bcac6af36645dead8c572ba08a08",
        "0xb8cf3ed326bb0e51454361fb37e9e8df6dc5c286"
    };
    public static bool Supported(DefiPosition p) => p.Protocol == "Gearbox" && p.Chain == "eth" && p.Type == "deposit" && Pools.Contains(p.VaultAddress!);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, args)));
        var pool = p.VaultAddress!;
        var underlying = DefiNftLiquidity.Address(await Read(pool, "underlyingToken()"), 0);
        var wrappedStEth = pool.Equals("0xb8cf3ed326bb0e51454361fb37e9e8df6dc5c286", StringComparison.OrdinalIgnoreCase) &&
            underlying.Equals("0x7f39c581f595b53c5cb19bd0b3f8da6c935e2ca0", StringComparison.OrdinalIgnoreCase) &&
            p.AssetAddress?.Equals(DefiLido.Token, StringComparison.OrdinalIgnoreCase) == true;
        if (!underlying.Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase) && !wrappedStEth) throw new InvalidDataException("Gearbox underlying token mismatch");
        var diesel = DefiNftLiquidity.Address(await Read(pool, "dieselToken()"), 0);
        var balance = DefiLending.Word(await Read(diesel, "balanceOf(address)", p.Wallet), 0);
        var shares = exact == null ? balance : BigInteger.Parse(exact);
        if (shares <= 0 || shares > balance) throw new InvalidOperationException("No Gearbox diesel shares remain, or the balance decreased");
        var gross = DefiLending.Word(await Read(pool, "fromDiesel(uint256)", shares.ToString()), 0);
        var liquidity = DefiLending.Word(await Read(pool, "availableLiquidity()"), 0);
        if (liquidity < gross) throw new InvalidOperationException("Gearbox v2 pool lacks liquidity for the full withdrawal. The deposit remains on-chain; no shares were burned. Available raw underlying: " + liquidity + "; required: " + gross + ".");
        var tx = DefiActionAbi.Build(p.Wallet, pool, "removeLiquidity(uint256,address)", [shares.ToString(), p.Wallet]);
        var output = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx)), 0);
        if (output <= 0) throw new InvalidOperationException("Gearbox returns no underlying after its withdrawal fee");
        return (await DefiVault.PrepareTransaction(web3, chainId, underlying, output, tx, boost, prices)) with {
            InputAmountRaw = shares.ToString(), Notice = "Withdraws live diesel shares. This v2 pool has no queue or on-chain minimum output parameter; simulation is checked again before sending."
        };
    }
}
