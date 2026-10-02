using System.Numerics;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiSyncSwap
{
    // SyncSwap official Era v1 deployments and core-contracts/SyncSwapRouter.sol, ClassicPool.sol.
    public const string Router = "0x2da10A1e27bF85cEdD8FFb1AbBe97e53391C0295";
    public const string Vault = "0x621425a1Ef6abE91058E9712575dcc4258F8d091";
    public const string Master = "0xbB05918E9B4bA9Fe2c8384d223f0844867909Ffb";
    public const string Factory = "0xf2DAd89f2788a8CD54625C60b55cD3d2D0ACa7Cb";
    private const string Zero = "0x0000000000000000000000000000000000000000";
    public static bool IsSupported(DefiPosition p) => p.Protocol == "SyncSwap" && Rpc.Normalize(p.Chain) == "zksync";
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exactAmount = null, HttpClient? prices = null)
    {
        if (!IsSupported(p) || chainId != 324 || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != 324)
            throw new InvalidOperationException("SyncSwap RPC chain does not match zkSync Era");
        var abi = DefiBlackwing.Abi(("poolType", [], ["uint16"]), ("master", [], ["address"]), ("vault", [], ["address"]),
            ("token0", [], ["address"]), ("token1", [], ["address"]), ("balanceOf", ["address"], ["uint256"]),
            ("allowance", ["address", "address"], ["uint256"]), ("approve", ["address", "uint256"], ["bool"]),
            ("totalSupply", [], ["uint256"]), ("invariantLast", [], ["uint256"]), ("getProtocolFee", [], ["uint24"]));
        var pool = web3.Eth.GetContract(abi, p.VaultAddress!);
        async Task<T> Read<T>(string name, params object[] args) => await SwapExecution.Read(pool.GetFunction(name).CallAsync<T>(args));
        if (await Read<BigInteger>("poolType") != 1 || !Same(await Read<string>("master"), Master) || !Same(await Read<string>("vault"), Vault))
            throw new InvalidOperationException("Only verified SyncSwap Era classic pools are supported; stable/staked pools require their own adapter");
        var tokens = new[] { await Read<string>("token0"), await Read<string>("token1") };
        if (!tokens.Any(t => Same(t, p.AssetAddress!))) throw new InvalidOperationException("Position asset does not belong to SyncSwap pool");
        var factory = web3.Eth.GetContract(DefiBlackwing.Abi(("getPool", ["address", "address"], ["address"])), Factory);
        if (!Same(await SwapExecution.Read(factory.GetFunction("getPool").CallAsync<string>(tokens[0], tokens[1])), p.VaultAddress!))
            throw new InvalidOperationException("SyncSwap pool is not registered in the official classic factory");
        var available = await Read<BigInteger>("balanceOf", p.Wallet);
        var liquidity = exactAmount == null ? available : BigInteger.Parse(exactAmount);
        if (liquidity <= 0 || available < liquidity) throw new InvalidOperationException("No wallet LP shares available or LP balance decreased");
        if (await Read<BigInteger>("balanceOf", p.VaultAddress!) != 0) throw new InvalidOperationException("Pool has unburned LP shares; preview again after the pending pool operation");
        var allowance = await Read<BigInteger>("allowance", p.Wallet, Router);
        var router = web3.Eth.GetContract(DefiBlackwing.Abi(("burnLiquidity", ["address", "uint256", "bytes", "uint256[]", "address", "bytes"], [])), Router);
        // Mode 2 sends both ERC20 tokens back to this wallet, including wrapped ETH. No external callback.
        var data = Convert.FromHexString(p.Wallet[2..].PadLeft(64, '0') + "2".PadLeft(64, '0'));
        TransactionInput Burn(BigInteger[] mins) => router.GetFunction("burnLiquidity").CreateTransactionInput(p.Wallet,
            p.VaultAddress!, liquidity, data, mins, Zero, Array.Empty<byte>());
        BigInteger[] amounts; DefiVault.Approval? approval = null;
        if (allowance >= liquidity)
        {
            var result = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(Burn([0, 0])));
            if (DefiLending.Word(result, 0) != 32 || DefiLending.Word(result, 1) != 2 ||
                !Same("0x" + result.Substring(2 + 2 * 64 + 24, 40), tokens[0]) || !Same("0x" + result.Substring(2 + 4 * 64 + 24, 40), tokens[1]))
                throw new InvalidDataException("SyncSwap simulation returned unexpected output tokens");
            amounts = [DefiLending.Word(result, 3), DefiLending.Word(result, 5)];
        }
        else
        {
            amounts = await EstimateOutputs(web3, pool, tokens, p.VaultAddress!, liquidity);
            var tx = pool.GetFunction("approve").CreateTransactionInput(p.Wallet, Router, liquidity);
            await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx));
            var gas = await SwapExecution.Read(web3.Eth.Transactions.EstimateGas.SendRequestAsync(tx));
            var price = GasPricing.Price((await SwapExecution.Read(web3.Eth.GasPrice.SendRequestAsync())).Value, gasPercent);
            approval = new(p.VaultAddress!, tx.Data, ((gas.Value * 110 + 99) / 100).ToString(), price.ToString());
        }
        var minima = amounts.Select(a => a * 995 / 1000).ToArray();
        if (minima.Any(a => a <= 0)) throw new InvalidOperationException("LP output is too small for protected withdrawal");
        var quote = await DefiVault.PrepareTransaction(web3, chainId, tokens[0], minima[0], Burn(minima), gasPercent, prices,
            [(tokens[0], minima[0]), (tokens[1], minima[1])], approval == null ? null : new BigInteger(3000000), approval);
        return quote with { InputAmountRaw = liquidity.ToString() };
    }
    private static async Task<BigInteger[]> EstimateOutputs(Web3 web3, Nethereum.Contracts.Contract pool, string[] tokens, string poolAddress, BigInteger liquidity)
    {
        // Match ClassicPool._balances and _mintProtocolFee. Cached Rabby amounts are never used for signing.
        var vault = web3.Eth.GetContract(DefiBlackwing.Abi(("balanceOf", ["address", "address"], ["uint256"])), Vault);
        var balances = new[] {
            await SwapExecution.Read(vault.GetFunction("balanceOf").CallAsync<BigInteger>(tokens[0], poolAddress)),
            await SwapExecution.Read(vault.GetFunction("balanceOf").CallAsync<BigInteger>(tokens[1], poolAddress)) };
        var supply = await SwapExecution.Read(pool.GetFunction("totalSupply").CallAsync<BigInteger>());
        if (supply <= 0 || balances.Any(b => b <= 0 || b >= (BigInteger.One << 128))) throw new InvalidOperationException("Invalid SyncSwap pool liquidity");
        var master = web3.Eth.GetContract(DefiBlackwing.Abi(("getFeeRecipient", [], ["address"])), Master);
        var feeRecipient = await SwapExecution.Read(master.GetFunction("getFeeRecipient").CallAsync<string>());
        var last = await SwapExecution.Read(pool.GetFunction("invariantLast").CallAsync<BigInteger>());
        if (!Same(feeRecipient, Zero) && last > 0)
        {
            var invariant = Sqrt(balances[0] * balances[1]);
            if (invariant > last)
            {
                var fee = await SwapExecution.Read(pool.GetFunction("getProtocolFee").CallAsync<BigInteger>());
                if (fee < 0 || fee >= 100000) throw new InvalidOperationException("Invalid SyncSwap protocol fee");
                supply += supply * (invariant - last) * fee / ((100000 - fee) * invariant + fee * last);
            }
        }
        return balances.Select(b => liquidity * b / supply).ToArray();
    }
    internal static BigInteger Sqrt(BigInteger value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value < 2) return value;
        var x = value; var y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + value / x) / 2; }
        return x;
    }
    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
