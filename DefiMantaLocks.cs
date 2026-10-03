using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

// layerbank-foundation/v2-contracts staking/xLAB.sol: slot-based locks, paid after expiry.
public static class DefiMantaLocks
{
    public const string Locker = "0x69e38d781183a52de5e7506cd57bc6c55bb74467";
    public static bool Supported(DefiPosition p) => p.Protocol == "LayerBank" && p.Chain == "manta" && p.Type == "locked" &&
        Locker.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified LayerBank xLAB locker");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("LAB", [], ["address"])), Locker);
        if (!(await SwapExecution.Read(contract.GetFunction("LAB").CallAsync<string>())).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("LayerBank lock underlying token mismatch");
        var call = DefiActionAbi.Build(p.Wallet, Locker, "locksOf(address)", [p.Wallet]);
        var data = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(call));
        var offset = (int)(DefiLending.Word(data, 0) / 32);
        var count = (int)DefiLending.Word(data, offset);
        if (count > 1000) throw new InvalidDataException("LayerBank lock list exceeds its bounded scan");
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        var slot = -1; BigInteger amount = 0, nextUnlock = 0;
        for (int i = count - 1; i >= 0; i--) {
            var end = DefiLending.Word(data, offset + 1 + i * 3);
            var balance = DefiLending.Word(data, offset + 2 + i * 3);
            if (balance > 0 && end <= block.Timestamp.Value) { slot = i; amount = balance; break; }
            if (balance > 0 && (nextUnlock == 0 || end < nextUnlock)) nextUnlock = end;
        }
        if (slot < 0) throw new InvalidOperationException(nextUnlock > 0 ?
            $"LayerBank xLAB is locked until {DateTimeOffset.FromUnixTimeSeconds((long)nextUnlock):u}" : "No LayerBank xLAB locks remain");
        var key = slot + ":" + amount;
        if (exact != null && key != exact) throw new InvalidOperationException("LayerBank lock slot changed; preview again");
        var tx = DefiActionAbi.Build(p.Wallet, Locker, "unlock(uint256)", [slot.ToString()]);
        return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount, tx, gasPercent, prices)) with {
            InputAmountRaw = key, Notice = "Withdraws one complete expired xLAB lock. Check again if the account has further lock slots."
        };
    }
}
