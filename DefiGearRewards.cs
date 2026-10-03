using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiGearRewards
{
    public const string Distributor = "0xa7df60785e556d65292a2c9a077bb3a8fbf048bc";
    public static bool Supported(DefiPosition p) => p.Chain == "eth" && p.Protocol == "Gearbox" && p.Type == "reward" && Distributor.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        async Task<string> Read(string signature, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, Distributor, signature, args)));
        var root = await Read("merkleRoot()");
        var token = DefiNftLiquidity.Address(await Read("token()"), 0);
        if (!token.Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GEAR distributor token mismatch");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // The DAO publishes a shard for each wallet prefix under the current on-chain Merkle root.
        var url = $"https://raw.githubusercontent.com/Gearbox-protocol/airdrop/master/merkle/mainnet_{root[2..].ToLowerInvariant()}/{p.Wallet[2..4].ToLowerInvariant()}.json";
        using var response = await http.GetAsync(url, SwapExecution.Token); response.EnsureSuccessStatusCode();
        var tree = JObject.Parse(await response.Content.ReadAsStringAsync(SwapExecution.Token));
        if (!root.Equals((string?)tree["merkleRoot"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GEAR proof root mismatch");
        var entry = ((JObject?)tree["claims"])?.Properties().SingleOrDefault(x => x.Name.Equals(p.Wallet, StringComparison.OrdinalIgnoreCase))?.Value;
        if (entry == null) throw new InvalidOperationException("No GEAR entitlement exists under the current distribution root");
        var amountString = (string)entry["amount"]!;
        var amount = amountString.StartsWith("0x") ? new Nethereum.Hex.HexTypes.HexBigInteger(amountString).Value : BigInteger.Parse(amountString);
        var claimed = DefiLending.Word(await Read("claimed(address)", p.Wallet), 0);
        if (amount <= claimed) throw new InvalidOperationException("All GEAR rewards from this distribution have already been claimed");
        var key = root + ":" + amount;
        if (exact != null && exact != key) throw new InvalidOperationException("GEAR distribution changed; preview again");
        var tx = DefiActionAbi.Build(p.Wallet, Distributor, "claim(uint256,address,uint256,bytes32[])",
            [entry["index"]!.ToString(), p.Wallet, amount.ToString(), entry["proof"]!.ToString(Newtonsoft.Json.Formatting.None)]);
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, key, prices);
    }
}
