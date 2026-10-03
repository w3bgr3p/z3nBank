using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiWithdrawal
{
    public static bool UsesRabby(DefiPosition p) => p.WithdrawActions.Length > 0 && !DefiBeefy.Supported(p) && !DefiGearPool.Supported(p) && !DefiCygnus.Supported(p) && !DefiJoeLiquidity.Supported(p) && !DefiPendleExit.Supported(p) && !DefiHop.Supported(p) && !DefiGmxMarkets.Supported(p) && !DefiLido.Supported(p) && !DefiStader.Supported(p) && !DefiV2Liquidity.Supported(p) && !DefiSeamlessRewards.Supported(p) && !DefiGearRewards.Supported(p) && !IsGate(p) && !DefiCurve.Supported(p) && !DefiHana.Supported(p) && !DefiNftLiquidity.Supported(p) && !DefiCometRewards.Supported(p) && !DefiMerkl.Supported(p) && !DefiStargateLiquidity.Supported(p) &&
        !DefiLending.IsAave(p) && !DefiGmxGlp.Supported(p) && !DefiMantis.Supported(p) && !DefiMantaLocks.Supported(p) && !DefiCompoundV2.Supported(p) && !DefiSonne.Supported(p) && !DefiPassport.Supported(p) && !DefiPendleRewards.Supported(p) && !DefiSablier.Supported(p) && !DefiShellEscrow.Supported(p) && !DefiAaveRewards.Supported(p) && !DefiJoe.IsSupported(p) && !DefiLayerBankRewards.IsSupported(p) &&
        !DefiStargate.IsSupported(p) && !DefiBlackwing.IsSupported(p) && !DefiSyncSwap.IsSupported(p) &&
        !DefiScrollLending.IsLayerBank(p) && !DefiScrollLending.IsCompound(p);
    // SynFutures official Oyster SDK, src/config/blast.json and src/common/util.ts.
    public const string BlastGate = "0x6A372dBc1968f4a07cf2ce352f410962A972c257";
    public const string BlastWeth = "0x4300000000000000000000000000000000000004";
    public const string BlastUsdb = "0x4300000000000000000000000000000000000003";
    private const string GateAbi = "[{\"type\":\"function\",\"name\":\"reserveOf\",\"inputs\":[{\"type\":\"address\"},{\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"view\"},{\"type\":\"function\",\"name\":\"withdraw\",\"inputs\":[{\"type\":\"bytes32\"}],\"outputs\":[],\"stateMutability\":\"nonpayable\"}]";
    public static bool IsGate(DefiPosition p) => p.Chain == "blast" && p.Protocol == "SynFutures V3" &&
        string.Equals(p.VaultAddress, BlastGate, StringComparison.OrdinalIgnoreCase);
    public static byte[] GateArgument(string asset, BigInteger amount)
    {
        if (!DefiVault.AddressValid(asset) || amount <= 0 || amount >= (BigInteger.One << 96))
            throw new InvalidOperationException("Invalid SynFutures withdrawal token or uint96 amount");
        var encoded = new byte[32]; amount.ToByteArray(isUnsigned: true, isBigEndian: true).CopyTo(encoded, 12 - amount.GetByteCount(isUnsigned: true));
        Convert.FromHexString(asset[2..]).CopyTo(encoded, 12); return encoded;
    }
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exactAmount = null, HttpClient? priceClient = null, string action = "withdraw")
    {
        if (action == "cancel" && DefiGmxMarkets.Supported(p)) return await DefiGmxPending.Cancel(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (action != "withdraw") throw new InvalidOperationException("Unsupported protocol action");
        if (DefiBeefy.Supported(p)) return await DefiBeefy.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiGearPool.Supported(p)) return await DefiGearPool.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiV2Liquidity.Supported(p)) return await DefiV2Liquidity.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiGmxMarkets.Supported(p)) return await DefiGmxMarkets.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiCygnus.Supported(p)) return await DefiCygnus.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiJoeLiquidity.Supported(p)) return await DefiJoeLiquidity.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiPendleExit.Supported(p)) return await DefiPendleExit.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiHop.Supported(p)) return await DefiHop.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiLido.Supported(p)) return await DefiLido.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiStader.Supported(p)) return await DefiStader.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiSeamlessRewards.Supported(p)) return await DefiSeamlessRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiGearRewards.Supported(p)) return await DefiGearRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiCometRewards.Supported(p)) return await DefiCometRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiCurve.Supported(p)) return await DefiCurve.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiGmxGlp.Supported(p)) return await DefiGmxGlp.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiMantis.Supported(p)) return await DefiMantis.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiMantaLocks.Supported(p)) return await DefiMantaLocks.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiCompoundV2.Supported(p)) return await DefiCompoundV2.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiSonne.Supported(p)) return await DefiSonne.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiPassport.Supported(p)) return await DefiPassport.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiPendleRewards.Supported(p)) return await DefiPendleRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiSablier.Supported(p)) return await DefiSablier.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiShellEscrow.Supported(p)) return await DefiShellEscrow.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiMerkl.Supported(p)) return await DefiMerkl.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiStargateLiquidity.Supported(p)) return await DefiStargateLiquidity.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiAaveRewards.Supported(p)) return await DefiAaveRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiNftLiquidity.Supported(p)) return await DefiNftLiquidity.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiHana.Supported(p)) return await DefiHana.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiLending.IsAave(p)) return await DefiLending.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiJoe.IsSupported(p)) return await DefiJoe.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiLayerBankRewards.IsSupported(p)) return await DefiLayerBankRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiStargate.IsSupported(p)) return await DefiStargate.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiBlackwing.IsSupported(p)) return await DefiBlackwing.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiSyncSwap.IsSupported(p)) return await DefiSyncSwap.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiScrollLending.IsLayerBank(p) || DefiScrollLending.IsCompound(p))
            return await DefiScrollLending.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (UsesRabby(p)) return await DefiRabbyActions.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        var unsupported = Unsupported(p);
        if (unsupported != null) throw new InvalidOperationException(unsupported);
        if (!IsGate(p))
        {
            try { return await DefiVault.Prepare(web3, chainId, p.VaultAddress!, p.Wallet, p.AssetAddress!, gasPercent, exactAmount, priceClient); }
            catch (Nethereum.JsonRpc.Client.RpcResponseException ex)
            { throw new InvalidOperationException($"{p.Protocol}: ERC-4626 verification or simulation failed; a protocol-specific exit may be required. " + SwapExecution.ErrorDetails(ex), ex); }
        }
        if (chainId != 81457 || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != 81457)
            throw new InvalidOperationException("RPC chain does not match SynFutures Blast");
        if (p.Type != "deposit" || !(string.Equals(p.AssetAddress, BlastWeth, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.AssetAddress, BlastUsdb, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("SynFutures adapter supports free WETH/USDB Gate deposits only; LP and trading margin need separate exits");
        var gate = web3.Eth.GetContract(GateAbi, BlastGate);
        var reserve = await SwapExecution.Read(gate.GetFunction("reserveOf").CallAsync<BigInteger>(p.AssetAddress!, p.Wallet));
        var amount = exactAmount == null ? reserve : BigInteger.Parse(exactAmount);
        if (amount <= 0 || reserve < amount) throw new InvalidOperationException("No free SynFutures Gate reserve available, or reserve decreased; open positions are not closed automatically");
        var tx = gate.GetFunction("withdraw").CreateTransactionInput(p.Wallet, GateArgument(p.AssetAddress!, amount));
        return await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount, tx, gasPercent, priceClient);
    }
    public static string? Unsupported(DefiPosition p) => UsesRabby(p)
        ? DefiRabbyActions.Unavailable(p) : p.Protocol switch
    {
        "Stargate" when !DefiStargate.IsSupported(p) && !DefiStargateLiquidity.Supported(p) => p.Type == "locked" ? "This Stargate escrow has not been verified for automatic withdrawal." : "This Stargate liquidity/farm deployment has not been verified",
        "PancakeSwap V3" when !DefiNftLiquidity.Supported(p) => "This PancakeSwap NFT farm or network is not verified",
        "Curve" when !DefiCurve.Supported(p) => "This Curve pool/gauge exit has not been verified",
        "Merkl" when !DefiMerkl.Supported(p) => "This Merkl distributor or network has not been verified",
        "Hana Network" when !DefiHana.Supported(p) => "This Hana Network deposit asset or contract is not supported",
        "GMX V2" when !DefiGmxMarkets.Supported(p) => "This GMX deployment has not been verified",
        "Seamless Protocol" when !DefiLending.IsAave(p) && !DefiSeamlessRewards.Supported(p) => "No verified action for this Seamless position",
        "Hana Finance" when !DefiLending.IsAave(p) => "No verified action for this Hana Finance position",
        "Blackwing" when !DefiBlackwing.IsSupported(p) => "This Blackwing vault has not been verified for automatic withdrawal.",
        "SyncSwap" when !DefiSyncSwap.IsSupported(p) => "SyncSwap LP withdrawal is currently supported on zkSync Era only.",
        "Balancer V2" => "Balancer V2 pool liquidity requires a pool exit adapter with minimum output protection; ERC-4626 withdrawal does not apply.",
        "LFJ" when !DefiJoe.IsSupported(p) && !DefiJoeLiquidity.Supported(p) => "This LFJ contract is not verified.",
        "Aave V3" when !DefiLending.IsAave(p) && !DefiAaveRewards.Supported(p) => "This Aave V3 market has not been verified for automatic withdrawal.",
        "Compound V3" when p.Type == "reward" && !DefiCometRewards.Supported(p) => "This Comet reward controller has not been verified",
        "Compound V3" when !DefiScrollLending.IsCompound(p) && !DefiCometRewards.Supported(p) => "This Compound market has not been verified for automatic withdrawal.",
        "LayerBank" when !DefiScrollLending.IsLayerBank(p) && !DefiMantaLocks.Supported(p) => "This LayerBank market has not been verified for automatic withdrawal.",
        "LayerBank" when p.Type == "reward" && !DefiLayerBankRewards.IsSupported(p) => "This reward requires a separate verified claim/vesting adapter.",
        _ => null
    };
    public static string? Unavailable(DefiPosition p)
    {
        if (p.Type == "loan") return "This is an outstanding loan, not a withdrawable deposit. Repay the loan before withdrawing its collateral.";
        if (p.HasProxy) return "This position is held by a proxy contract; direct wallet withdrawal is unavailable.";
        var reason = Unsupported(p); if (reason != null) return reason;
        if (DefiMantaLocks.Supported(p) || DefiGmxGlp.Supported(p) || DefiCurve.Supported(p) || DefiGearRewards.Supported(p) || DefiSeamlessRewards.Supported(p)) { try { DefiVault.Network(p.Chain); return null; } catch (Exception ex) { return ex.Message; } }
        if ((p.Type is not ("deposit" or "staked") && p.WithdrawActions.Length == 0 && !DefiStargate.IsSupported(p) && !DefiShellEscrow.Supported(p) && !DefiSablier.Supported(p) && !DefiSonne.Supported(p) && !DefiPendleRewards.Supported(p) && !DefiJoe.IsSupported(p) && !DefiLayerBankRewards.IsSupported(p) && !DefiCometRewards.Supported(p) && !DefiAaveRewards.Supported(p) && !DefiMerkl.Supported(p)) || !DefiVault.AddressValid(p.AssetAddress) || !DefiVault.AddressValid(p.VaultAddress))
            return "No withdrawal adapter for this position type or contract";
        try { DefiVault.Network(p.Chain); return null; }
        catch (ArgumentException) { return $"No configured RPC for network {p.Chain}"; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }
}
