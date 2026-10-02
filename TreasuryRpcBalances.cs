using System.Numerics;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using z3n;

namespace z3nSafe;

public static class TreasuryRpcBalances
{
    private static long _revision;
    public static long Revision => Interlocked.Read(ref _revision);
    public static Jumper.TokenInfo NativeFromQuote(object quote) => quote switch
    {
        LiFiBridge.QuoteResponse lifi => JObject.FromObject(lifi.Action.ToToken).ToObject<Jumper.TokenInfo>()!,
        RelayBridge.QuoteResponse relay => JObject.FromObject(relay.Details)["currencyOut"]!["currency"]!.ToObject<Jumper.TokenInfo>()!,
        _ => throw new InvalidDataException("Missing native output metadata")
    };

    public static async Task<List<Jumper.TokenInfo>> Read(Web3 web3, int chainId, string wallet,
        IEnumerable<Jumper.TokenInfo> tokens, BigInteger minimumBlock = default, CancellationToken cancellation = default)
    {
        var actualChain = await web3.Eth.ChainId.SendRequestAsync().WaitAsync(cancellation);
        if (actualChain.Value != chainId) throw new InvalidDataException($"RPC chain mismatch: expected {chainId}, received {actualChain.Value}");
        var head = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().WaitAsync(cancellation);
        if (head.Value < minimumBlock) throw new InvalidDataException($"RPC is behind the confirmed transaction: block {head.Value} < {minimumBlock}");
        var block = new BlockParameter(head);
        var result = new List<Jumper.TokenInfo>();
        foreach (var token in tokens.GroupBy(t => TokenSelection.IsNative(t.Address) ? "native" : t.Address, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var amount = TokenSelection.IsNative(token.Address)
                ? (await web3.Eth.GetBalance.SendRequestAsync(wallet, block).WaitAsync(cancellation)).Value
                : await web3.Eth.GetContractQueryHandler<DeFi.BalanceOfFunction>().QueryAsync<BigInteger>(
                    token.Address, new DeFi.BalanceOfFunction { Owner = wallet }, block).WaitAsync(cancellation);
            if (amount < 0) throw new InvalidDataException("RPC returned a negative balance");
            var updated = JObject.FromObject(token).ToObject<Jumper.TokenInfo>()!;
            updated.Amount = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            updated.ChainId = chainId;
            result.Add(updated);
        }
        return result;
    }

    public static async Task RefreshAfterSwap(Db db, int id, string chain, int chainId, string wallet,
        Web3 web3, IEnumerable<Jumper.TokenInfo> discovered, object quote, string hashes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var saved = db.GetTableColumns("_treasury").Contains(chain, StringComparer.Ordinal)
            ? db.Get(chain, "_treasury", id: id, log: true, thrw: true) : null;
        var tokens = new List<Jumper.TokenInfo> { NativeFromQuote(quote) };
        if (!TokenSelection.IsNative(tokens[0].Address) || tokens[0].ChainId != chainId)
            throw new InvalidDataException("Quote native output does not match the refreshed network");
        tokens.AddRange(discovered);
        if (!string.IsNullOrWhiteSpace(saved)) tokens.AddRange(JsonConvert.DeserializeObject<List<Jumper.TokenInfo>>(saved) ?? []);
        BigInteger confirmedBlock = 0;
        if (string.IsNullOrWhiteSpace(hashes)) throw new InvalidDataException("Confirmed transaction hash is missing");
        foreach (var hash in hashes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(hash).WaitAsync(timeout.Token);
            if (receipt?.BlockNumber == null) throw new InvalidDataException($"Confirmed transaction receipt unavailable: {hash}");
            confirmedBlock = BigInteger.Max(confirmedBlock, receipt.BlockNumber.Value);
        }
        var balances = await Read(web3, chainId, wallet, tokens, confirmedBlock, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        db.ReplaceTreasurySnapshot(id, new Dictionary<string, string> { [chain] = JsonConvert.SerializeObject(balances) }, complete: false);
        Interlocked.Increment(ref _revision);
    }
}
