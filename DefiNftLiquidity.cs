using System.Numerics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;

namespace z3nSafe;

// Uniswap v3-periphery NonfungiblePositionManager and Pancake MasterChefV3.
// Liquidity removal, fee collection and NFT return are one atomic multicall.
public static class DefiNftLiquidity
{
    public const string UniManager = "0xc36442b4a4522e871399cd717abdd847ab11fe88";
    public const string CakeManager = "0x46a15b0b27311cedf172ab29e4f4766fbe7f4364";
    public const string CakeFarm = "0x556b9306565093c855aea9ae92a594704c2cd59e";
    public static bool Supported(DefiPosition p) =>
        p.Protocol == "Uniswap V3" && p.Chain == "arb" && p.Type is "deposit" or "reward" ||
        p.Protocol == "PancakeSwap V3" && p.Chain == "bsc" && p.Type is "deposit" or "reward" &&
            string.Equals(p.Controller ?? p.VaultAddress, CakeFarm, StringComparison.OrdinalIgnoreCase);
    public static BigInteger TokenId(DefiPosition p)
    {
        // Provider NFT descriptions carry the position ID; never infer it from an AMM pool address.
        var detail = JObject.Parse(p.DetailJson ?? "{}");
        var description = (string?)detail["description"] ?? "";
        var id = Regex.Match(description, @"(?:^|\s)#(\d+)(?:\s|$)");
        if (id.Success) return BigInteger.Parse(id.Groups[1].Value);
        foreach (var action in p.WithdrawActions) {
            var name = DefiActionAbi.Signature(action.Function).Name;
            if (name is "withdraw" or "harvest" && action.Parameters?.Length > 0 &&
                BigInteger.TryParse(action.Parameters[0], out var number) && number > 0) return number;
            if (name == "collect" && action.Parameters?.Length == 1) return BigInteger.Parse(JArray.Parse(action.Parameters[0])[0].ToString());
        }
        throw new InvalidDataException("The provider did not identify the liquidity NFT; refresh this account");
    }
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null)
    {
        if (!Supported(p) || Rpc.ChainId(p.Chain) != chainId || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId)
            throw new InvalidDataException("Unverified NFT liquidity network or contract");
        var cake = p.Protocol == "PancakeSwap V3";
        var manager = cake ? CakeManager : UniManager;
        var destination = cake ? CakeFarm : manager;
        var id = TokenId(p);
        async Task<string> Read(string target, string signature, params string[] values) => await SwapExecution.Read(
            web3.Eth.Transactions.Call.SendRequestAsync(DefiActionAbi.Build(p.Wallet, target, signature, values)));
        var owner = Address(await Read(manager, "ownerOf(uint256)", id.ToString()), 0);
        if (cake) {
            if (!owner.Equals(CakeFarm, StringComparison.OrdinalIgnoreCase) ||
                !Address(await Read(CakeFarm, "nonfungiblePositionManager()"), 0).Equals(manager, StringComparison.OrdinalIgnoreCase) ||
                !Address(await Read(CakeFarm, "userPositionInfos(uint256)", id.ToString()), 6).Equals(p.Wallet, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This farming NFT is not owned by the account");
        } else if (!owner.Equals(p.Wallet, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This liquidity NFT is no longer owned by the account");
        var position = await Read(manager, "positions(uint256)", id.ToString());
        var liquidity = DefiLending.Word(position, 7);
        if (p.Type == "reward" && cake && p.Symbol.Equals("CAKE", StringComparison.OrdinalIgnoreCase)) {
            var harvest = DefiActionAbi.Build(p.Wallet, destination, "harvest(uint256,address)", [id.ToString(), p.Wallet]);
            var key = "harvest:" + id;
            if (exact != null && exact != key) throw new InvalidDataException("NFT reward input changed");
            return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, harvest, key, prices);
        }
        var calls = new List<string>();
        var frozen = exact == null ? null : JObject.Parse(exact);
        if (frozen != null && ((string?)frozen["id"] != id.ToString() || (string?)frozen["type"] != p.Type))
            throw new InvalidDataException("NFT withdrawal input changed");
        var state = frozen ?? new JObject { ["id"] = id.ToString(), ["type"] = p.Type };
        if (p.Type == "deposit") {
            var input = frozen == null ? liquidity : BigInteger.Parse((string)state["liquidity"]!);
            if (input <= 0 || input > liquidity) throw new InvalidOperationException("NFT liquidity is empty or decreased");
            var deadline = frozen == null ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200 : (long)state["deadline"]!;
            if (deadline <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidOperationException("Liquidity preview expired; check withdrawal again");
            string Tuple(BigInteger min0, BigInteger min1) => new JArray(id.ToString(), input.ToString(), min0.ToString(), min1.ToString(), deadline.ToString()).ToString(Newtonsoft.Json.Formatting.None);
            var simulated = await Read(destination, "decreaseLiquidity((uint256,uint128,uint256,uint256,uint256))", Tuple(0, 0));
            var min0 = frozen == null ? DefiLending.Word(simulated, 0) * 995 / 1000 : BigInteger.Parse((string)state["min0"]!);
            var min1 = frozen == null ? DefiLending.Word(simulated, 1) * 995 / 1000 : BigInteger.Parse((string)state["min1"]!);
            state["liquidity"] = input.ToString(); state["deadline"] = deadline; state["min0"] = min0.ToString(); state["min1"] = min1.ToString();
            calls.Add(DefiActionAbi.Build(p.Wallet, destination, "decreaseLiquidity((uint256,uint128,uint256,uint256,uint256))", [Tuple(min0, min1)]).Data);
        }
        var max = ((BigInteger.One << 128) - 1).ToString();
        var collect = new JArray(id.ToString(), p.Wallet, max, max).ToString(Newtonsoft.Json.Formatting.None);
        calls.Add(DefiActionAbi.Build(p.Wallet, destination, "collect((uint256,address,uint128,uint128))", [collect]).Data);
        if (cake && p.Type == "deposit") calls.Add(DefiActionAbi.Build(p.Wallet, destination, "withdraw(uint256,address)", [id.ToString(), p.Wallet]).Data);
        var tx = DefiActionAbi.Build(p.Wallet, destination, "multicall(bytes[])", [new JArray(calls).ToString(Newtonsoft.Json.Formatting.None)]);
        return await DefiRabbyActions.PrepareBuilt(web3, chainId, p, gasPercent, tx, state.ToString(Newtonsoft.Json.Formatting.None), prices);
    }
    internal static string Address(string data, int word)
    {
        var number = DefiLending.Word(data, word);
        if (number < 0 || number >= (BigInteger.One << 160)) throw new InvalidDataException("Invalid contract address response");
        return "0x" + number.ToString("x").PadLeft(40, '0')[^40..];
    }
}
