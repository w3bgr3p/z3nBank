using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiLending
{
    // Aave V3 official market addresses; asset amounts come from Pool.withdraw simulation, not cached prices.
    private static readonly Dictionary<int, string> Pools = new()
    {
        [1] = "0x87870bca3f3fd6335c3f4ce8392d69350b4fa4e2",
        [137] = "0x794a61358d6845594f94dc1db02a252b5b4814ad",
        [8453] = "0xa238dd80c259a72e81d7e4664a9801593f98d1c5",
        [1088] = "0x90df02551bb792286e8d4f13e0e357b4bf1d6a57",
        [100] = "0xb50201558b00496a145fe76f7424749556e326d8",
        [534352] = "0x11fCfe756c05AD438e312a7fd934381537D3cFfe"
    };
    private const string Abi = "[{\"type\":\"function\",\"name\":\"withdraw\",\"inputs\":[{\"type\":\"address\"},{\"type\":\"uint256\"},{\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"nonpayable\"},{\"type\":\"function\",\"name\":\"getUserAccountData\",\"inputs\":[{\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"},{\"type\":\"uint256\"},{\"type\":\"uint256\"},{\"type\":\"uint256\"},{\"type\":\"uint256\"},{\"type\":\"uint256\"}],\"stateMutability\":\"view\"}]";
    public static bool IsAave(DefiPosition p)
    {
        if (p.Type != "deposit") return false;
        try {
            var id = Rpc.ChainId(p.Chain);
            var pool = p.Protocol switch {
                "Aave V3" => Pools.GetValueOrDefault(id),
                "Aave" when id == 137 => "0x8dff5e27ea6b7ac08ebfdf9eb090f32ee9a30fcf",
                "Seamless Protocol" when id == 8453 => "0x8f44fd754285aa6a2b8b9b97739b79746e0475a7",
                "Hana Finance" when id == 167000 => "0x4ab85bf9ea548410023b25a13031e91b4c4f3b91",
                _ => null
            };
            return pool != null && pool.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }
    public static BigInteger Word(string data, int index)
    {
        var start = 2 + index * 64;
        if (!data.StartsWith("0x") || data.Length < start + 64) throw new InvalidDataException("Contract returned incomplete data");
        return new BigInteger(Convert.FromHexString(data.Substring(start, 64)), isUnsigned: true, isBigEndian: true);
    }
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exactAmount = null, HttpClient? priceClient = null)
    {
        if (!IsAave(p) || Rpc.ChainId(p.Chain) != chainId || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidOperationException("Aave market or RPC chain does not match the position");
        if (p.Type != "deposit" || !DefiVault.AddressValid(p.AssetAddress)) throw new InvalidOperationException("Only supplied Aave assets can be withdrawn");
        var pool = web3.Eth.GetContract(Abi, p.VaultAddress!);
        var accountCall = pool.GetFunction("getUserAccountData").CreateCallInput(p.Wallet); accountCall.From = p.Wallet;
        var accountData = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(accountCall));
        if (Word(accountData, 1) > 0) throw new InvalidOperationException("Aave account has active debt. Automatic withdrawal is blocked to protect collateral; repay or manage the position first.");
        var withdraw = pool.GetFunction("withdraw");
        var maxTx = withdraw.CreateTransactionInput(p.Wallet, p.AssetAddress!, (BigInteger.One << 256) - 1, p.Wallet);
        BigInteger available;
        try { available = Word(await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(maxTx)), 0); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvalidOperationException("Aave withdrawal simulation failed: reserve may be paused, illiquid or restricted. " + SwapExecution.ErrorDetails(ex), ex); }
        var amount = exactAmount == null ? available : BigInteger.Parse(exactAmount);
        if (amount <= 0 || available < amount) throw new InvalidOperationException("No Aave withdrawal available or supplied balance decreased");
        var tx = withdraw.CreateTransactionInput(p.Wallet, p.AssetAddress!, amount, p.Wallet);
        return await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount, tx, gasPercent, priceClient);
    }
}
