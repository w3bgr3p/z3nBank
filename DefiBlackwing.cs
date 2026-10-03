using System.Numerics;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiBlackwing
{
    // Verified BSC launch vault implementation, Sourcify contracts/evm/launch_vault/vault.sol.
    public const string Vault = "0xd00789260984160a64dcf19a03896dff73bf4514";
    public const string Implementation = "0xc6ade8a68026d582ab37b879d188caf7e405dd09";
    public const string ArbitrumImplementation = "0xa92299289361fdcbb4ce9acbb512a84bd5fab37d";
    private const string ImplementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
    public static bool IsSupported(DefiPosition p) => p.Protocol == "Blackwing" && p.Type is "staked" or "deposit" &&
        (Rpc.Normalize(p.Chain) == "bsc" && string.Equals(p.VaultAddress, Vault, StringComparison.OrdinalIgnoreCase) ||
         Rpc.ChainId(p.Chain) == 42161 && string.Equals(p.VaultAddress, Implementation, StringComparison.OrdinalIgnoreCase));
    internal static string Abi(params (string Name, string[] Inputs, string[] Outputs)[] functions) => new JArray(functions.Select(f =>
        new JObject { ["type"] = "function", ["name"] = f.Name, ["stateMutability"] = "nonpayable",
            ["inputs"] = new JArray(f.Inputs.Select(t => new JObject { ["type"] = t })),
            ["outputs"] = new JArray(f.Outputs.Select(t => new JObject { ["type"] = t })) })).ToString();
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exactAmount = null, HttpClient? prices = null)
    {
        if (!IsSupported(p) || chainId != Rpc.ChainId(p.Chain) || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidOperationException("Blackwing vault or RPC chain is not verified");
        var implementation = await SwapExecution.Read(web3.Eth.GetStorageAt.SendRequestAsync(p.VaultAddress!, new HexBigInteger(ImplementationSlot)));
        if (!implementation.EndsWith((chainId == 56 ? Implementation : ArbitrumImplementation)[2..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Blackwing implementation changed; withdrawal adapter must be verified again");
        var vault = web3.Eth.GetContract(Abi(("vaultTokenAddress", ["address"], ["address"]),
            ("balance", ["address", "address"], ["uint256"]), ("withdraw", ["address", "uint256"], []),
            ("lastDepositBlock", ["address"], ["uint256"]), ("withdrawBlockWait", [], ["uint256"])), p.VaultAddress!);
        var lp = await SwapExecution.Read(vault.GetFunction("vaultTokenAddress").CallAsync<string>(p.AssetAddress!));
        if (!DefiVault.AddressValid(lp)) throw new InvalidOperationException("Invalid Blackwing vault token");
        var shares = await SwapExecution.Read(web3.Eth.GetContract(Abi(("balanceOf", ["address"], ["uint256"])), lp)
            .GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        var burn = exactAmount == null ? shares : BigInteger.Parse(exactAmount);
        if (burn <= 0 || burn > shares) throw new InvalidOperationException("Blackwing shares are empty or decreased since preview");
        var last = await SwapExecution.Read(vault.GetFunction("lastDepositBlock").CallAsync<BigInteger>(p.Wallet));
        var wait = await SwapExecution.Read(vault.GetFunction("withdrawBlockWait").CallAsync<BigInteger>());
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockNumber.SendRequestAsync());
        if (block.Value <= last + wait) throw new InvalidOperationException($"Blackwing withdrawal is locked until block {last + wait + 1}");
        var balance = await SwapExecution.Read(vault.GetFunction("balance").CallAsync<BigInteger>(p.AssetAddress!, p.Wallet));
        var output = balance * burn / shares;
        var tx = vault.GetFunction("withdraw").CreateTransactionInput(p.Wallet, p.AssetAddress!, burn);
        try
        {
            return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, output, tx, gasPercent, prices))
                with { InputAmountRaw = burn.ToString() };
        }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex)
        {
            var details = SwapExecution.ErrorDetails(ex);
            throw new InvalidOperationException("Blackwing withdrawal simulation failed. Contract codes: 7 = withdrawals disabled by protocol; " +
                "8 = deposit lock; 6 = deployed assets could not be returned; 1 = output too small. " + details, ex);
        }
    }
}
