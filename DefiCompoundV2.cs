using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiCompoundV2
{
    public const string Comptroller = "0x3d9819210a31b4961b30ef54be2aed79b9c9cd3b";
    public const string Ceth = "0x4ddc2d193948926d02f9b1fe9e1daa0718270ed5";
    public static bool Supported(DefiPosition p) => p.Protocol == "Compound" && p.Chain == "eth" && p.Type == "deposit" &&
        Comptroller.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p) || chainId != 1) throw new InvalidOperationException("Unverified Compound v2 market");
        var controller = web3.Eth.GetContract(DefiBlackwing.Abi(("getAssetsIn", ["address"], ["address[]"]),
            ("getAllMarkets", [], ["address[]"])), Comptroller);
        var entered = await SwapExecution.Read(controller.GetFunction("getAssetsIn").CallAsync<List<string>>(p.Wallet));
        foreach (var market in entered) {
            var debt = web3.Eth.GetContract(DefiBlackwing.Abi(("borrowBalanceCurrent", ["address"], ["uint256"])), market);
            if (await SwapExecution.Read(debt.GetFunction("borrowBalanceCurrent").CallAsync<BigInteger>(p.Wallet)) > 0)
                throw new InvalidOperationException("Compound v2 has outstanding borrowing. Repay the loans before withdrawing collateral; no repayment or collateral transaction was sent.");
        }
        string? receipt = TokenSelection.IsNative(p.AssetAddress!) ? Ceth : null;
        if (receipt == null) {
            var markets = await SwapExecution.Read(controller.GetFunction("getAllMarkets").CallAsync<List<string>>());
            foreach (var market in markets.Where(m => !m.Equals(Ceth, StringComparison.OrdinalIgnoreCase))) {
                var token = web3.Eth.GetContract(DefiBlackwing.Abi(("underlying", [], ["address"])), market);
                if ((await SwapExecution.Read(token.GetFunction("underlying").CallAsync<string>())).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase)) { receipt = market; break; }
            }
        }
        if (receipt == null) throw new InvalidDataException("No listed Compound cToken for this underlying asset");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("balanceOf", ["address"], ["uint256"]),
            ("exchangeRateCurrent", [], ["uint256"]), ("redeem", ["uint256"], ["uint256"])), receipt);
        var balance = await SwapExecution.Read(contract.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var shares = exact == null ? balance : BigInteger.Parse(exact);
        if (shares <= 0 || shares > balance) throw new InvalidOperationException("Compound cToken balance is empty or decreased");
        var rate = await SwapExecution.Read(contract.GetFunction("exchangeRateCurrent").CallAsync<BigInteger>());
        var tx = contract.GetFunction("redeem").CreateTransactionInput(p.Wallet, shares);
        var code = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx)), 0);
        if (code != 0) throw new InvalidOperationException($"Compound rejected redemption with error code {code}; reserve liquidity or account restrictions prevent withdrawal");
        return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, shares * rate / BigInteger.Pow(10, 18), tx, gasPercent, prices))
            with { InputAmountRaw = shares.ToString() };
    }
}
