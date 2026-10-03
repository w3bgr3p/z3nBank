using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

// Verified Curve VotingEscrow deployment used by Shell; early exits are not allowed.
public static class DefiShellEscrow
{
    public const string Escrow = "0x68313f1a6a60d05dd767811643406339e8e8d034";
    public static bool Supported(DefiPosition p) => p.Protocol == "Shell V3" && p.Chain == "arb" && p.Type == "locked" &&
        Escrow.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Shell escrow");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("token", [], ["address"]), ("locked", ["address"], ["int128", "uint256"]),
            ("withdraw", [], [])), Escrow);
        if (!(await SwapExecution.Read(contract.GetFunction("token").CallAsync<string>())).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Shell escrow token mismatch");
        var locked = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(contract.GetFunction("locked").CreateCallInput(p.Wallet)));
        var amount = DefiLending.Word(locked, 0); var end = DefiLending.Word(locked, 1);
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        if (amount <= 0 || amount >= (BigInteger.One << 127)) throw new InvalidOperationException("No Shell locked balance remains");
        if (end > block.Timestamp.Value) throw new InvalidOperationException($"Shell tokens are locked until {DateTimeOffset.FromUnixTimeSeconds((long)end):u}");
        if (exact != null && exact != amount.ToString()) throw new InvalidOperationException("Shell locked amount changed; preview again");
        return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount,
            contract.GetFunction("withdraw").CreateTransactionInput(p.Wallet), gasPercent, prices)) with { InputAmountRaw = amount.ToString() };
    }
}
