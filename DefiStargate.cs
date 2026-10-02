using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

public static class DefiStargate
{
    // Official stargate-dao deployments and Stargate token contract addresses.
    private static readonly Dictionary<int, (string Escrow, string Token)> Markets = new()
    {
        [1] = ("0x0e42acbd23faee03249daff896b78d7e79fbd58e", "0xaf5191b0de278c7286d6c7cc6ab6bb8a73ba2cd6"),
        [56] = ("0xd4888870c8686c748232719051b677791dbda26d", "0xb0d502e938ed5f4df2e681fe6e419ff29631d62b"),
        [10] = ("0x43d2761ed16c89a2c4342e2b16a3c61ccf88f05b", "0x296f55f8fb28e498b858d0bcda06d955b2cb3f97"),
        [42161] = ("0xfbd849e6007f9bc3cc2d6eb159c045b8dc660268", "0x6694340fc020c5e6b96567843da2df01b2ce1eb6")
    };
    public static bool IsSupported(DefiPosition p)
    {
        if (p.Protocol != "Stargate" || p.Type != "locked") return false;
        try { return Markets.TryGetValue(Rpc.ChainId(Rpc.Normalize(p.Chain)), out var market) &&
            string.Equals(p.VaultAddress, market.Escrow, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.AssetAddress, market.Token, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
    public static void ValidateLock(BigInteger amount, BigInteger end, BigInteger now, bool unlocked, string? exact)
    {
        if (amount <= 0 || amount >= (BigInteger.One << 127)) throw new InvalidOperationException("No locked STG balance available");
        if (!unlocked && end > now) throw new InvalidOperationException($"STG is locked until {DateTimeOffset.FromUnixTimeSeconds((long)end):u}. Stargate does not allow an early withdrawal.");
        if (exact != null && amount != BigInteger.Parse(exact)) throw new InvalidOperationException("STG lock amount changed; request a new preview");
    }
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!IsSupported(p) || !Markets.ContainsKey(chainId) || chainId != Rpc.ChainId(Rpc.Normalize(p.Chain)) ||
            (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId) throw new InvalidOperationException("Stargate escrow or RPC chain does not match");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("token", [], ["address"]), ("locked", ["address"], ["int128", "uint256"]),
            ("unlocked", [], ["bool"]), ("withdraw", [], [])), p.VaultAddress!);
        if (!string.Equals(await SwapExecution.Read(contract.GetFunction("token").CallAsync<string>()), p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stargate escrow underlying token mismatch");
        var locked = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(contract.GetFunction("locked").CreateCallInput(p.Wallet)));
        var amount = DefiLending.Word(locked, 0); var end = DefiLending.Word(locked, 1);
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        ValidateLock(amount, end, block.Timestamp.Value, await SwapExecution.Read(contract.GetFunction("unlocked").CallAsync<bool>()), exact);
        return await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount, contract.GetFunction("withdraw").CreateTransactionInput(p.Wallet), gasPercent, prices);
    }
}
