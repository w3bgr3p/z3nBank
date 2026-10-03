using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

// AngleProtocol/merkl-contracts Distributor.claim. Proofs are fetched freely and verified by eth_call.
public static class DefiMerkl
{
    public const string Distributor = "0x3ef3d8ba38ebe18db133cec108f4d14ce00dd9ae";
    public static bool Supported(DefiPosition p) => p.Protocol == "Merkl" && p.Type is "deposit" or "reward" &&
        Distributor.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase) && p.Chain is "eth" or "arb" or "base" or "matic" or "op";
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null, HttpClient? proofClient = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Merkl distributor");
        using var owned = proofClient == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) } : null;
        var http = proofClient ?? owned!;
        var body = JArray.Parse(await http.GetStringAsync($"https://api.merkl.xyz/v4/users/{p.Wallet.ToLowerInvariant()}/rewards?chainId={chainId}", SwapExecution.Token));
        var rewards = body.Where(c => (int?)c["chain"]?["id"] == chainId).SelectMany(c => c["rewards"] as JArray ?? [])
            .Where(r => (int?)r["distributionChainId"] == chainId &&
                string.Equals((string?)r["recipient"], p.Wallet, StringComparison.OrdinalIgnoreCase) &&
                (int?)r["token"]?["chainId"] == chainId && string.Equals((string?)r["token"]?["address"], p.AssetAddress, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rewards.Length != 1) throw new InvalidOperationException("No unique current Merkl proof for this reward; it may already be claimed or still pending");
        var reward = rewards[0];
        var amount = BigInteger.Parse((string)reward["amount"]!);
        if (amount <= 0) throw new InvalidOperationException("No Merkl reward is available");
        var proof = reward["proofs"] as JArray ?? throw new InvalidDataException("Merkl did not return a proof");
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("claimed", ["address", "address"], ["uint208", "uint48", "bytes32"]),
            ("claimRecipient", ["address", "address"], ["address"])), Distributor);
        var claimed = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(contract.GetFunction("claimed").CreateCallInput(p.Wallet, p.AssetAddress!)));
        if (DefiLending.Word(claimed, 0) >= amount) throw new InvalidOperationException("This Merkl reward is already claimed on-chain");
        // Existing recipient overrides must never redirect the user's claim.
        foreach (var token in new[] { p.AssetAddress!, "0x0000000000000000000000000000000000000000" }) {
            var recipient = await SwapExecution.Read(contract.GetFunction("claimRecipient").CallAsync<string>(p.Wallet, token));
            if (!TokenSelection.IsNative(recipient) && !recipient.Equals(p.Wallet, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Merkl has a different claim recipient configured; change it in the protocol first");
        }
        var tx = DefiActionAbi.Build(p.Wallet, Distributor, "claim(address[],address[],uint256[],bytes32[][])",
            [new JArray(p.Wallet).ToString(), new JArray(p.AssetAddress).ToString(), new JArray(amount.ToString()).ToString(), new JArray { proof.DeepClone() }.ToString()]);
        var key = amount.ToString();
        if (exact != null && exact != key) throw new InvalidDataException("Merkl claim amount changed; request a new preview");
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p with { Type = "reward" }, gasPercent, tx, key, prices);
    }
}
