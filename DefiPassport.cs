using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

// Verified original Human Passport IDStaking round contract, not its incompatible v2 replacement.
public static class DefiPassport
{
    public const string Staking = "0x0e3efd5be54cc0f4c64e0d186b0af4b7f2a0e95f";
    public static bool Supported(DefiPosition p) => p.Protocol == "Human Passport" && p.Chain == "eth" && p.Type == "staked" &&
        Staking.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Passport staking contract");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("token", [], ["address"]), ("latestRound", [], ["uint256"]),
            ("stakes", ["uint256", "address"], ["uint256"]), ("unstake", ["uint256", "uint256"], [])), Staking);
        if (!(await SwapExecution.Read(contract.GetFunction("token").CallAsync<string>())).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Passport staking token mismatch");
        var latest = await SwapExecution.Read(contract.GetFunction("latestRound").CallAsync<BigInteger>());
        if (latest > 1000) throw new InvalidOperationException("Passport round discovery exceeds its bounded scan");
        BigInteger round = -1, amount = 0;
        for (BigInteger i = 0; i <= latest; i++) {
            var stake = await SwapExecution.Read(contract.GetFunction("stakes").CallAsync<BigInteger>(i, p.Wallet));
            if (stake > 0) { round = i; amount = stake; break; }
        }
        if (amount <= 0) throw new InvalidOperationException("No Passport stake remains in the original contract");
        var key = round + ":" + amount;
        if (exact != null && exact != key) throw new InvalidOperationException("Passport round or stake changed; preview again");
        return (await DefiVault.PrepareTransaction(web3, chainId, p.AssetAddress!, amount,
            contract.GetFunction("unstake").CreateTransactionInput(p.Wallet, round, amount), gasPercent, prices)) with { InputAmountRaw = key,
                Notice = $"Withdraws the complete stake from Passport round {round}. Check again if another round also holds a stake." };
    }
}
