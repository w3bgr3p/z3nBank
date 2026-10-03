using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiScrollLending
{
    public const string Core = "0xEC53c830f4444a8A56455c6836b5D2aA794289Aa";
    public const string Leth = "0x274C3795dadfEbf562932992bF241ae087e0a98C";
    public const string Lusdc = "0x0D8F8e271DD3f2fC58e5716d3Ff7041dBe3F0688";
    public const string Usdc = "0x06efdbff2a14a7c8e15944d1f4a48f9f95f663a4";
    public const string Comet = "0xB2f97c1Bd3bf02f5e74d13f02E3e26F93D77CE44";
    // compound-finance/comet, deployments/polygon/usdc/roots.json.
    public const string PolygonComet = "0xF25212E676D1F7F89Cd72fFEe66158f541246445";
    public const string BaseComet = "0x46e6b214b524310239732d51387075e0e70970bf";
    public const string ArbitrumComet = "0x9c4ec768c28520b50860ea7a15bd7213a9ff58bf";
    public const string Native = "0x0000000000000000000000000000000000000000";
    private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool IsLayerBank(DefiPosition p) => p.Protocol == "LayerBank" && Rpc.Normalize(p.Chain) == "scroll" && Same(p.VaultAddress, Core);
    public static bool IsCompound(DefiPosition p) => p.Protocol == "Compound V3" && p.Type == "deposit" &&
        (Rpc.Normalize(p.Chain) == "scroll" && Same(p.VaultAddress, Comet) || Rpc.Normalize(p.Chain) == "polygon" && Same(p.VaultAddress, PolygonComet) ||
         Rpc.ChainId(p.Chain) == 8453 && Same(p.VaultAddress, BaseComet) || Rpc.ChainId(p.Chain) == 42161 && Same(p.VaultAddress, ArbitrumComet));
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent, string? exact = null, HttpClient? prices = null)
    {
        var expectedChain = Rpc.ChainId(p.Chain);
        if (chainId != expectedChain || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidOperationException("RPC chain does not match the lending market");
        if (p.Type != "deposit") throw new InvalidOperationException("Only supplied lending assets can be withdrawn; reward claims require a separate adapter");
        if (IsCompound(p)) return await Compound(web3, chainId, p, gasPercent, exact, prices);
        if (!IsLayerBank(p)) throw new InvalidOperationException("Unverified Scroll lending contract");
        var market = Same(p.AssetAddress, Native) ? Leth : Same(p.AssetAddress, Usdc) ? Lusdc :
            throw new InvalidOperationException("LayerBank adapter currently supports the verified Scroll ETH and USDC markets");
        var token = web3.Eth.GetContract(DefiBlackwing.Abi(("core", [], ["address"]), ("underlying", [], ["address"]),
            ("balanceOf", ["address"], ["uint256"]), ("getCash", [], ["uint256"])), market);
        if (!Same(await SwapExecution.Read(token.GetFunction("core").CallAsync<string>()), Core) ||
            !Same(await SwapExecution.Read(token.GetFunction("underlying").CallAsync<string>()), p.AssetAddress!))
            throw new InvalidOperationException("LayerBank market identity does not match the position");
        var core = web3.Eth.GetContract(DefiBlackwing.Abi(("accountLiquidityOf", ["address"], ["uint256", "uint256", "uint256"]),
            ("redeemToken", ["address", "uint256"], ["uint256"])), Core);
        var account = core.GetFunction("accountLiquidityOf").CreateCallInput(p.Wallet);
        if (DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(account)), 2) > 0)
            throw new InvalidOperationException("LayerBank account has active debt; collateral withdrawal blocked");
        var shares = await SwapExecution.Read(token.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var amount = exact == null ? shares : BigInteger.Parse(exact);
        if (amount <= 0 || amount > shares) throw new InvalidOperationException("LayerBank supplied shares are empty or decreased");
        var cash = await SwapExecution.Read(token.GetFunction("getCash").CallAsync<BigInteger>());
        if (cash <= 0) throw new InvalidOperationException("LayerBank market currently has no available underlying liquidity. Your deposit exists, but the protocol cannot pay a withdrawal until liquidity returns.");
        var tx = core.GetFunction("redeemToken").CreateTransactionInput(p.Wallet, market, amount);
        BigInteger output;
        try { output = DefiLending.Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx)), 0); }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex) when (ex.Message.Contains("not enough underlying"))
        { throw new InvalidOperationException("LayerBank has insufficient available market liquidity for this withdrawal. No transaction was sent.", ex); }
        return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, output, tx, gasPercent, prices)) with { InputAmountRaw = amount.ToString() };
    }
    private static async Task<DefiVault.Quote> Compound(Web3 web3, int chainId, DefiPosition p, decimal gasPercent, string? exact, HttpClient? prices)
    {
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("baseToken", [], ["address"]), ("borrowBalanceOf", ["address"], ["uint256"]),
            ("balanceOf", ["address"], ["uint256"]), ("collateralBalanceOf", ["address", "address"], ["uint128"]),
            ("withdraw", ["address", "uint256"], [])), p.VaultAddress!);
        if (await SwapExecution.Read(contract.GetFunction("borrowBalanceOf").CallAsync<BigInteger>(p.Wallet)) > 0)
            throw new InvalidOperationException("Compound account has active debt; collateral withdrawal blocked");
        var asset = await SwapExecution.Read(contract.GetFunction("baseToken").CallAsync<string>());
        var balance = Same(p.AssetAddress, asset) ? await SwapExecution.Read(contract.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet)) :
            await SwapExecution.Read(contract.GetFunction("collateralBalanceOf").CallAsync<BigInteger>(p.Wallet, p.AssetAddress!));
        var amount = exact == null ? balance : BigInteger.Parse(exact);
        if (amount <= 0 || amount > balance) throw new InvalidOperationException("Compound supplied balance is empty or decreased");
        var tx = contract.GetFunction("withdraw").CreateTransactionInput(p.Wallet, p.AssetAddress!, amount);
        return await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount, tx, gasPercent, prices);
    }
}
