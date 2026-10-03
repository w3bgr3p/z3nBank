using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;

namespace z3nSafe;

public static class DefiV2Liquidity
{
    public static string? Router(DefiPosition p) => (p.Chain, p.Protocol) switch {
        ("matic", "QuickSwap") => "0xa5e0829caced8ffdd4de3c43696c57f7d7a678ff",
        ("matic", "SushiSwap") => "0x1b02da8cb0d097eb8d57a175b88c7d8b47997506",
        ("eth", "SushiSwap") => "0xd9e1ce17f2641f24ae83637ab66a2cca9c378b9f", _ => null
    };
    public static bool Supported(DefiPosition p) => p.Type == "deposit" && Router(p) != null &&
        p.AdapterId is "uniswap2_liquidity" or "uniswap2_liquidity_proxy";
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal boost,
        string? exact = null, HttpClient? prices = null)
    {
        var router = Router(p)!; var pair = p.VaultAddress!;
        async Task<string> Read(string target, string method, params string[] args) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, method, args)));
        var factory = DefiNftLiquidity.Address(await Read(router, "factory()"), 0);
        var token0 = DefiNftLiquidity.Address(await Read(pair, "token0()"), 0);
        var token1 = DefiNftLiquidity.Address(await Read(pair, "token1()"), 0);
        if (!DefiNftLiquidity.Address(await Read(factory, "getPair(address,address)", token0, token1), 0).Equals(pair, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("LP is not a pair of the verified exchange factory");
        var balance = DefiLending.Word(await Read(pair, "balanceOf(address)", p.Wallet), 0);
        var shares = exact == null ? balance : BigInteger.Parse((string)JObject.Parse(exact)["shares"]!);
        if (shares <= 0 || balance < shares) throw new InvalidOperationException("No LP shares remain, or the balance decreased");
        var supply = DefiLending.Word(await Read(pair, "totalSupply()"), 0);
        if (supply <= 0) throw new InvalidDataException("LP total supply is zero");
        var amount0 = DefiLending.Word(await Read(token0, "balanceOf(address)", pair), 0) * shares / supply;
        var amount1 = DefiLending.Word(await Read(token1, "balanceOf(address)", pair), 0) * shares / supply;
        var block = await SwapExecution.Read(web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(BlockParameter.CreateLatest()));
        var data = exact == null ? new JObject { ["shares"] = shares.ToString(), ["min0"] = (amount0 * 995 / 1000).ToString(),
            ["min1"] = (amount1 * 995 / 1000).ToString(), ["deadline"] = (block.Timestamp.Value + 1200).ToString() } : JObject.Parse(exact);
        if (BigInteger.Parse((string)data["deadline"]!) <= block.Timestamp.Value) throw new InvalidOperationException("LP preview expired; preview again");
        var tx = DefiActionAbi.Build(p.Wallet, router, "removeLiquidity(address,address,uint256,uint256,uint256,address,uint256)",
            [token0, token1, shares.ToString(), (string)data["min0"]!, (string)data["min1"]!, p.Wallet, (string)data["deadline"]!]);
        var action = new RabbyWithdrawAction { Approval = new JObject { ["token_id"] = pair, ["to"] = router, ["str_raw_amount"] = shares.ToString() } };
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, boost, tx, data.ToString(Newtonsoft.Json.Formatting.None), prices, action: action);
    }
}
