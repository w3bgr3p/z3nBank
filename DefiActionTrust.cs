namespace z3nSafe;

public static class DefiActionTrust
{
    // Additional action destinations observed in Rabby's public protocol actions. Direct pool actions
    // still bind to the discovered controller; these routes bind protocol, chain and exact function family.
    public static bool Destination(DefiPosition p, RabbyWithdrawAction action)
    {
        if (action.Contract.Equals(p.VaultAddress, StringComparison.OrdinalIgnoreCase) ||
            action.Contract.Equals(p.Controller, StringComparison.OrdinalIgnoreCase)) return true;
        var method = DefiActionAbi.Signature(action.Function).Name;
        if (p.Protocol == "Curve" && method == "remove_liquidity")
            return CurvePools.TryGetValue(p.Chain + ":" + p.VaultAddress?.ToLowerInvariant(), out var target) && target.Equals(action.Contract, StringComparison.OrdinalIgnoreCase);
        var key = p.Chain + ":" + p.Protocol + ":" + action.Contract.ToLowerInvariant();
        return key switch {
            "eth:Balancer V2:0xba12222222228d8ba445958a75a0704d566bf2c8" => method == "exitPool",
            "arb:Pendle V2:0x888888888889758f76e7103c6cbf23abbf58f946" => method is "removeLiquidityDualSyAndPt" or "exitPostExpToToken",
            "eth:SushiSwap:0xd9e1ce17f2641f24ae83637ab66a2cca9c378b9f" => method == "removeLiquidity",
            "arb:LFJ:0xb4315e873dbcf96ffd0acd8ea43f689d8c20fb30" => method == "removeLiquidity",
            "arb:Uniswap V3:0xc36442b4a4522e871399cd717abdd847ab11fe88" => method is "collect" or "multicall",
            "matic:Curve:0x92215849c439e1f8612b6646060b4e3e5ef822cc" => method == "remove_liquidity",
            "arb:Compound V3:0x88730d254a2f7e6ac8388c3198afd694ba9f7fae" or
            "base:Compound V3:0x123964802e6ababbe1bc9547d72ef1b69b00a6b1" or
            "matic:Compound V3:0x45939657d1ca34a8fa39a924b71d28fe8431e581" => method == "claim",
            "eth:Compound:0x3d9819210a31b4961b30ef54be2aed79b9c9cd3b" => method == "claimComp",
            "arb:EYWA:0xd2532ff3b05182a8d47bfca1807c654871ce1238" => method == "release",
            "metis:Aave V3:0x30c1b8f0490fa0908863d6cbd2e36400b4310a6b" => method == "claimAllRewardsToSelf",
            "eth:LIDO:0x889edc2edab5f40e902b864ad4d7ade8e412f9b1" => method is "requestWithdrawals" or "claimWithdrawals",
            "matic:Stader:0xfd225c9e6601c9d38d8f98d8731bf59efcf8c0e3" => method is "requestMaticXSwap" or "claimMaticXSwap",
            _ => false
        };
    }
    internal static readonly Dictionary<string, string> CurvePools = new(StringComparer.OrdinalIgnoreCase) {
        ["matic:0xe7a24ef0c5e95ffb0f6684b813a78f2a3ad7d171"] = "0x445fe580ef8d70ff569ab36e80c647af338db351",
        ["eth:0xc4c319e2d4d66cca4464c0c2b32c9bd23ebe784e"] = "0xc4c319e2d4d66cca4464c0c2b32c9bd23ebe784e",
        ["matic:0xdad97f7713ae9437fa9249920ec8507e5fbb23d3"] = "0x92215849c439e1f8612b6646060b4e3e5ef822cc",
        ["eth:0x06325440d014e39736583c165c2963ba99faf14e"] = "0xdc24316b9ae028f1497c275eb9192a3ea0f67022",
        ["eth:0x29059568bb40344487d62f7450e78b8e6c74e0e5"] = "0xc26b89a667578ec7b3f11b2f98d6fd15c07c54ba",
        ["eth:0x53a901d48795c58f485cbb38df08fa96a24669d5"] = "0xf9440930043eb3997fc70e1339dbb11f341de7a8",
        ["eth:0x6c38ce8984a890f5e46e6df6117c26b3f1ecfc9c"] = "0x0f3159811670c117c372428d4e69ac32325e4d0f",
        ["eth:0xa3d87fffce63b53e0d54faa1cc983b7eb0b74a9c"] = "0xc5424b857f758e906013f3555dad202e4bdb4567",
        ["eth:0xaa17a236f2badc98ddc0cf999abb47d47fc0a6cf"] = "0xa96a65c051bf88b4095ee1f2451c2a9d43f53ae2",
        ["eth:0xb79565c01b7ae53618d9b847b9443aaf4f9011e7"] = "0x9409280dc1e6d33ab7a8c6ec03e5763fb61772b5",
        ["eth:0xc4ad29ba4b3c580e6d59105fff484999997675ff"] = "0xd51a44d3fae010294c616388b506acda1bfaae46",
        ["eth:0xf43211935c781d5ca1a41d2041f397b8a7366c7a"] = "0xa1f8a6807c402e4a15ef4eba36528a3fed24e577"
    };
    // From Rabby's DappActions WHITELIST_SPENDER; only routes needed by the discovered portfolio.
    public static bool Approval(string chain, string spender) => (chain + ":" + spender.ToLowerInvariant()) is
        "eth:0xba12222222228d8ba445958a75a0704d566bf2c8" or
        "arb:0x888888888889758f76e7103c6cbf23abbf58f946" or
        "arb:0x7452c558d45f8afc8c83dae62c3f8a5be19c71f6" or
        "eth:0xd9e1ce17f2641f24ae83637ab66a2cca9c378b9f" or
        "matic:0x1b02da8cb0d097eb8d57a175b88c7d8b47997506" or
        "matic:0xa5e0829caced8ffdd4de3c43696c57f7d7a678ff" or
        "eth:0x889edc2edab5f40e902b864ad4d7ade8e412f9b1" or
        "matic:0xfd225c9e6601c9d38d8f98d8731bf59efcf8c0e3" or
        "mode:0x4af97f73343b226c5a5872dcd2d1c4944bdb3e77";
}
