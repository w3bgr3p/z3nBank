using System.Numerics;
using Nethereum.Web3;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiSablier
{
    public const string Lockup = "0xfdd9d122b451f549f48c4942c6fa6646d849e8c1";
    public static bool Supported(DefiPosition p) => p.Protocol == "Sablier" && p.Chain == "arb" && p.Type == "vesting" &&
        Lockup.Equals(p.Controller ?? p.VaultAddress, StringComparison.OrdinalIgnoreCase);
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p)) throw new InvalidOperationException("Unverified Sablier stream contract");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var nfts = JArray.Parse(await http.GetStringAsync($"https://api.rabby.io/v1/user/nft_list?id={p.Wallet}&is_all=true", SwapExecution.Token));
        var ids = new List<string>(); var amounts = new List<string>();
        var contract = web3.Eth.GetContract(DefiBlackwing.Abi(("ownerOf", ["uint256"], ["address"]),
            ("getAsset", ["uint256"], ["address"]), ("withdrawableAmountOf", ["uint256"], ["uint128"])), Lockup);
        foreach (var nft in nfts.Where(n => (string?)n["chain"] == p.Chain &&
            string.Equals((string?)n["contract_id"], Lockup, StringComparison.OrdinalIgnoreCase))) {
            var id = BigInteger.Parse((string)nft["inner_id"]!);
            if (!(await SwapExecution.Read(contract.GetFunction("ownerOf").CallAsync<string>(id))).Equals(p.Wallet, StringComparison.OrdinalIgnoreCase) ||
                !(await SwapExecution.Read(contract.GetFunction("getAsset").CallAsync<string>(id))).Equals(p.AssetAddress, StringComparison.OrdinalIgnoreCase)) continue;
            var amount = await SwapExecution.Read(contract.GetFunction("withdrawableAmountOf").CallAsync<BigInteger>(id));
            if (amount > 0) { ids.Add(id.ToString()); amounts.Add(amount.ToString()); }
        }
        if (ids.Count == 0) throw new InvalidOperationException("No vested Sablier amount can be withdrawn now");
        var state = new JObject { ["ids"] = new JArray(ids), ["amounts"] = new JArray(amounts) };
        if (exact != null) {
            var old = JObject.Parse(exact);
            if (!JToken.DeepEquals(old["ids"], state["ids"])) throw new InvalidDataException("Sablier stream ownership changed; preview again");
            var confirmed = ((JArray)old["amounts"]!).Select(x => BigInteger.Parse(x.ToString())).ToArray();
            if (confirmed.Length != amounts.Count || confirmed.Where((a, i) => a <= 0 || a > BigInteger.Parse(amounts[i])).Any())
                throw new InvalidOperationException("Sablier withdrawable amount decreased");
            state = old;
        }
        var tx = DefiActionAbi.Build(p.Wallet, Lockup, "withdrawMultiple(uint256[],address,uint128[])",
            [state["ids"]!.ToString(), p.Wallet, state["amounts"]!.ToString()]);
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, state.ToString(Newtonsoft.Json.Formatting.None), prices);
    }
}
