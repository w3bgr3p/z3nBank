using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

// Verified Sonne StakedDistributor. burn starts a cooldown; withdraw claims the pending amount.
public static class DefiSonne
{
    public const string Staking = "0xdc05d85069dc4aba65954008ff99f2d73ff12618";
    public static bool Supported(DefiPosition p) => p.Protocol == "Sonne Finance" && p.Chain == "op" &&
        p.Type is "staked" or "deposit" or "reward" && Staking.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Sonne staking contract");
        if (p.Type == "reward") {
            var tx = DefiActionAbi.Build(p.Wallet, Staking, "claim(address)", [p.AssetAddress!]);
            var key = "claim:" + p.AssetAddress;
            if (exact != null && exact != key) throw new InvalidOperationException("Sonne reward token changed");
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, key, prices);
        }
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("sonne", [], ["address"]), ("balanceOf", ["address"], ["uint256"]),
            ("withdrawal", ["address"], ["uint256", "uint256"]), ("withdrawalPendingTime", [], ["uint256"])), Staking);
        var token = await SwapExecution.Read(contract.GetFunction("sonne").CallAsync<string>());
        if (!token.Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Sonne underlying token mismatch");
        var pending = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(contract.GetFunction("withdrawal").CreateCallInput(p.Wallet)));
        var amount = DefiLending.Word(pending, 0); var release = DefiLending.Word(pending, 1);
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        if (amount > 0) {
            if (release > block.Timestamp.Value) throw new InvalidOperationException($"Sonne withdrawal is pending until {DateTimeOffset.FromUnixTimeSeconds((long)release):u}; do not restart its cooldown");
            var key = "withdraw:" + amount;
            if (exact != null && exact != key) throw new InvalidOperationException("Sonne pending withdrawal changed");
            var tx = DefiActionAbi.Build(p.Wallet, Staking, "withdraw()", []);
            return (await DefiVault.PrepareTransaction(web3, chainId, token, amount, tx, gasPercent, prices)) with { InputAmountRaw = key };
        }
        var shares = await SwapExecution.Read(contract.GetFunction("balanceOf").CallAsync<BigInteger>(p.Wallet));
        if (shares <= 0) throw new InvalidOperationException("No Sonne stake or pending withdrawal remains");
        var input = "request:" + shares;
        if (exact != null && exact != input) throw new InvalidOperationException("Sonne stake changed; preview again");
        var delay = await SwapExecution.Read(contract.GetFunction("withdrawalPendingTime").CallAsync<BigInteger>());
        var request = DefiActionAbi.Build(p.Wallet, Staking, "burn(uint256)", [shares.ToString()]);
        return (await DefiVault.PrepareTransaction(web3, chainId, token, shares, request, gasPercent, prices)) with {
            InputAmountRaw = input, Stage = "request", Notice = $"Starts Sonne's {delay}-second withdrawal cooldown. No SONNE is received by this transaction. After it expires, check withdrawal again to claim."
        };
    }
}
