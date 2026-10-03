using z3nSafe;

static class TokenChecks
{
    public static void Run(Action<string, bool> check)
    {
        const string contract = "0x00000000000000000000000000000000000000a1";
        const string other = "0x00000000000000000000000000000000000000a2";
        HeatmapGenerator.TokenInfo Token(int chain, string address, string raw = "1000000") => new()
            { Symbol = "Q", ChainId = chain, Address = address, Amount = raw, Decimals = 6, PriceUSD = "1" };
        var accounts = new[] {
            new HeatmapGenerator.AccountData { Id = 1, Address = "wallet1", ChainData = new() {
                ["Ethereum"] = new() { Token(1, contract), Token(1, other),
                    Token(1, "0x0000000000000000000000000000000000000000") },
                ["Base"] = new() { Token(8453, contract) }
            } },
            new HeatmapGenerator.AccountData { Id = 2, Address = "wallet2", ChainData = new() {
                ["Ethereum"] = new() { Token(1, contract.ToUpperInvariant().Replace("0X", "0x"), "1000") }
            } }
        };
        var assets = new[] { new TokenSelection.Asset(1, contract) };
        var plan = TokenSelection.Plan(accounts, assets, 0m);
        check("Plans match contract case-insensitively across accounts", plan.Count == 2);
        check("Plans exclude same-symbol contracts and other chains", plan.All(t => t.ChainId == 1 && t.Address.Equals(contract, StringComparison.OrdinalIgnoreCase)));
        check("Plans preserve the wallet bound to each account", plan.Single(t => t.Id == 2).Wallet == "wallet2");
        var execution = TokenSelection.ExecutionTokens(plan.Where(t => t.Id == 1));
        check("Confirmed contracts remain executable without an indexer response", execution["1"].Single().Address == contract && execution["1"].Single().Decimals == 6 && execution["1"].Single().PriceUSD == "1");
        check("Execution never trusts the cached token amount", execution["1"].Single().Amount == "0");
        check("Plans apply minimum value", TokenSelection.Plan(accounts, assets, .1m).Count == 1);
        check("Account selection limits swaps on the server", TokenSelection.Plan(accounts, assets, 0m, [2]).Single().Id == 2);
        check("Network contract IDs and account whitelist jointly restrict the swap", TokenSelection.Plan(accounts,
            [new(8453, contract)], 0m, [1]).All(t => t.Id == 1 && t.ChainId == 8453));
        check("All-token mode honors the stablecoin exclusion on the server", TokenSelection.Plan(accounts, assets, 0m, [1], true).Count == 0);
        check("Empty account whitelist never swaps all accounts", TokenSelection.Plan(accounts, assets, 0m, []).Count == 0);
        check("Unknown selected account does not fall back to another wallet", TokenSelection.Plan(accounts, assets, 0m, [99]).Count == 0);
        check("Native tokens cannot be queued", TokenSelection.Plan(accounts,
            new[] { new TokenSelection.Asset(1, "0x0000000000000000000000000000000000000000") }, 0m).Count == 0);
        check("Empty selection never means swap all tokens", TokenSelection.Plan(accounts, Array.Empty<TokenSelection.Asset>(), 0m).Count == 0);
        check("Native aliases are case insensitive", TokenSelection.IsNative("0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE"));
    }
}
