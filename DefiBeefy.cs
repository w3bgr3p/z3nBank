using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

// Beefy Vault V7 uses want()/withdraw(shares), not ERC-4626 redeem().
public static class DefiBeefy
{
    private static readonly HashSet<string> Vaults = new(StringComparer.OrdinalIgnoreCase) {
        "op:0x72683e768d3dedbbb8b08e5526aa1ee56b21041c",
        "op:0x51007e51e8174eedea7cb620768a483678433cc8",
        "linea:0xc46833f6217db6586fd129cc2f61361dfce4c21d"
    };
    public static bool Supported(DefiPosition p) => p.Protocol == "Beefy" && p.Type == "deposit" && Vaults.Contains(p.Chain + ":" + p.VaultAddress);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, p.VaultAddress!, signature, args)));
        var want = DefiNftLiquidity.Address(await Read("want()"), 0);
        if (!want.Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Beefy underlying token changed");
        var balance = DefiLending.Word(await Read("balanceOf(address)", p.Wallet), 0);
        var shares = exact == null ? balance : BigInteger.Parse(exact);
        if (shares <= 0 || shares > balance) throw new InvalidOperationException("No Beefy shares remain, or the balance decreased");
        var tx = DefiActionAbi.Build(p.Wallet, p.VaultAddress!, "withdraw(uint256)", [shares.ToString()]);
        var quote = await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, shares.ToString(), prices);
        if (quote.Outputs?.Any(o => !o.Asset.Equals(want, StringComparison.OrdinalIgnoreCase)) == true) throw new InvalidDataException("Unexpected Beefy withdrawal asset");
        return quote with { Notice = "Withdraws the live mooToken balance to its underlying asset. This vault has no on-chain minimum output parameter; the output is simulated again before sending." };
    }
}
