using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiWithdrawal
{
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
        string? exactAmount = null, HttpClient? priceClient = null)
    {
        if (DefiLending.IsAave(p)) return await DefiLending.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiJoe.IsSupported(p)) return await DefiJoe.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiLayerBankRewards.IsSupported(p)) return await DefiLayerBankRewards.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiStargate.IsSupported(p)) return await DefiStargate.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiBlackwing.IsSupported(p)) return await DefiBlackwing.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiSyncSwap.IsSupported(p)) return await DefiSyncSwap.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
        if (DefiScrollLending.IsLayerBank(p) || DefiScrollLending.IsCompound(p))
            return await DefiScrollLending.Prepare(web3, chainId, p, gasPercent, exactAmount, priceClient);
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
    public static string? Unsupported(DefiPosition p) => p.Protocol switch
    {
        "Stargate" when !DefiStargate.IsSupported(p) => p.Type == "locked" ? "This Stargate escrow has not been verified for automatic withdrawal." : "Stargate liquidity/farm positions need pool redemption and reward adapters; they are not ERC-4626 vaults.",
        "PancakeSwap V3" => "PancakeSwap V3 needs the NFT position ID, liquidity removal and fee/reward collection through its position manager. The provider pool address is not an ERC-4626 withdrawal vault.",
        "Curve" => "Curve requires a verified pool/gauge exit and minimum outputs for each asset; ERC-4626 withdrawal does not apply.",
        "Merkl" => "Merkl rewards require a current distributor Merkle proof and a separate claim adapter. They are not ERC-4626 deposits.",
        "Hana Network" => "Hana Network deposits need a protocol-specific withdrawal adapter; the native dust estimate is not an ERC-4626 balance.",
        "Hana Finance" => "Hana Finance lending on Taiko needs a verified market adapter and network fee support.",
        "Blackwing" when !DefiBlackwing.IsSupported(p) => "This Blackwing vault has not been verified for automatic withdrawal.",
        "SyncSwap" when !DefiSyncSwap.IsSupported(p) => "SyncSwap LP withdrawal is currently supported on zkSync Era only.",
        "Balancer V2" => "Balancer V2 pool liquidity requires a pool exit adapter with minimum output protection; ERC-4626 withdrawal does not apply.",
        "LFJ" when !DefiJoe.IsSupported(p) => "This LFJ staking contract or asset is not supported by the sJOE adapter.",
        "Aave V3" when !DefiLending.IsAave(p) => "This Aave V3 market has not been verified for automatic withdrawal.",
        "Compound V3" when p.Type == "reward" => "COMP rewards are claimed through CometRewards, separately from withdrawing the supplied asset. A reward claim adapter is required.",
        "Compound V3" when !DefiScrollLending.IsCompound(p) => "This Compound market has not been verified for automatic withdrawal.",
        "LayerBank" when !DefiScrollLending.IsLayerBank(p) => "This LayerBank market has not been verified for automatic withdrawal.",
        "LayerBank" when p.Type == "reward" && !DefiLayerBankRewards.IsSupported(p) => "This reward requires a separate verified claim/vesting adapter.",
        _ => null
    };
    public static string? Unavailable(DefiPosition p)
    {
        var reason = Unsupported(p); if (reason != null) return reason;
        if ((p.Type is not ("deposit" or "staked") && !DefiStargate.IsSupported(p) && !DefiJoe.IsSupported(p) && !DefiLayerBankRewards.IsSupported(p)) || !DefiVault.AddressValid(p.AssetAddress) || !DefiVault.AddressValid(p.VaultAddress))
            return "No withdrawal adapter for this position type or contract";
        try { DefiVault.Network(p.Chain); return null; }
        catch (ArgumentException) { return $"No configured RPC for network {p.Chain}"; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }
}
