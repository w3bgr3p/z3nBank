using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiStader
{
    private const string Native = "0x0000000000000000000000000000000000000000";
    public const string Token = "0xfa68fb4628dff1028cfec22b4162fccd0d45efb6";
    public const string Pool = "0xfd225c9e6601c9d38d8f98d8731bf59efcf8c0e3";
    public static bool Supported(DefiPosition p) => p.Chain == "matic" && p.Protocol == "Stader" && Token.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string target, string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, args)));
        if (!Token.Equals(DefiNftLiquidity.Address(await Read(Pool, "getContracts()"), 1), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stader pool underlying changed");
        var data = await Read(Pool, "getUserMaticXSwapRequests(address)", p.Wallet);
        var start = checked((int)DefiLending.Word(data, 0) / 32); var count = checked((int)DefiLending.Word(data, start));
        if (count > 1000) throw new InvalidDataException("Too many pending Stader requests");
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        // Claim the last mature slot because removal swaps the last request into its place.
        for (int i = count - 1; i >= 0; i--) {
            var amount = DefiLending.Word(data, start + 1 + i * 3); var release = DefiLending.Word(data, start + 3 + i * 3);
            if (amount <= 0 || release > block.Timestamp.Value) continue;
            var key = $"claim:{i}:{amount}";
            if (exact != null && exact != key) throw new InvalidOperationException("Stader request changed; preview again");
            var tx = DefiActionAbi.Build(p.Wallet, Pool, "claimMaticXSwap(uint256)", [i.ToString()]);
            return (await DefiVault.PrepareTransaction(web3, chainId, Native, amount, tx, boost, prices)) with {
                InputAmountRaw = key, CompletesPending = count == 1, Notice = "Claims one mature Stader request. Check again to claim any remaining requests." };
        }
        if (count > 0) throw new InvalidOperationException("Stader withdrawal is queued; its bonding period has not finished. Check withdrawal again to claim later.");
        var balance = DefiLending.Word(await Read(Token, "balanceOf(address)", p.Wallet), 0);
        if (balance <= 0) throw new InvalidOperationException("No MaticX balance or pending request remains");
        var output = DefiLending.Word(await Read(Pool, "convertMaticXToMatic(uint256)", balance.ToString()), 0);
        var input = "request:" + balance;
        if (exact != null && exact != input) throw new InvalidOperationException("MaticX balance changed; preview again");
        var request = DefiActionAbi.Build(p.Wallet, Pool, "requestMaticXSwap(uint256)", [balance.ToString()]);
        var action = new RabbyWithdrawAction { Approval = new JObject { ["token_id"] = Token, ["to"] = Pool, ["str_raw_amount"] = balance.ToString() } };
        var quote = await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, request, input, prices, action: action,
            futureOutputs: new[] { (Native, output) });
        return quote with { Stage = "request", Notice = "Requests the full MaticX balance for POL. No POL arrives now; after the bonding period, check withdrawal again to claim it." };
    }
}
