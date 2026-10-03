using Nethereum.RPC.Eth.DTOs;
using z3n;
using Nethereum.Web3;

namespace z3nSafe;

public static class ReceiptWaiter
{
    public static Func<Task<TransactionReceipt>>? Fallback(int chainId, string hash)
    {
        var urls = chainId switch
        {
            56 => new[] { "https://bsc-dataseed1.bnbchain.org", "https://bsc-rpc.publicnode.com" },
            42161 => new[] { "https://arb1.arbitrum.io/rpc", "https://arbitrum.drpc.org" },
            _ => Array.Empty<string>()
        };
        if (urls.Length == 0) return null;
        var providers = urls.Select(url => new Web3(url)).ToArray();
        return VerifiedFallback(chainId, hash,
            providers.Select(web3 => (Func<Task<System.Numerics.BigInteger>>)(async () =>
                (await web3.Eth.ChainId.SendRequestAsync()).Value)).ToArray(),
            providers.Select(web3 => (Func<Task<TransactionReceipt>>)(() =>
                web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(hash))).ToArray());
    }

    public static Func<Task<TransactionReceipt>> VerifiedFallback(int chainId, string hash,
        Func<Task<System.Numerics.BigInteger>>[] chains, Func<Task<TransactionReceipt>>[] reads)
    {
        var selected = 0;
        var verified = new bool[reads.Length];
        return async () =>
        {
            var failures = new List<Exception>();
            for (var offset = 0; offset < reads.Length; offset++)
            {
                var index = (selected + offset) % reads.Length;
                try
                {
                    if (!verified[index])
                    {
                        var actual = await SwapExecution.Read(chains[index]());
                        if (actual != chainId) throw new InvalidOperationException($"Receipt RPC chain mismatch: expected {chainId}, received {actual}");
                        verified[index] = true;
                    }
                    var receipt = await SwapExecution.Read(reads[index]());
                    selected = index;
                    return receipt;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failures.Add(ex); }
            }
            throw new AggregateException($"All fallback receipt RPCs failed | chain {chainId} | TX: {hash}", failures);
        };
    }

    public static async Task<TransactionReceipt> WaitAsync(Func<Task<TransactionReceipt>> read, string txHash,
        Logger? log, int maxAttempts = 60, int delayMs = 5000, Func<Task<TransactionReceipt>>? fallback = null)
    {
        var errors = 0;
        var useFallback = false;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            SwapExecution.Check();
            try
            {
                TransactionReceipt receipt;
                try { receipt = await SwapExecution.Read(useFallback ? fallback!() : read()); }
                catch (Exception ex) when (ex is not OperationCanceledException && fallback != null && !useFallback)
                {
                    log?.Send($"Switching receipt RPC to fallback | TX: {txHash} | {SwapExecution.ErrorDetails(ex)}", "WARNING");
                    useFallback = true;
                    receipt = await SwapExecution.Read(fallback());
                }
                errors = 0;
                if (receipt != null) return receipt;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors++;
                var details = SwapExecution.ErrorDetails(ex);
                log?.Send($"Receipt RPC error {errors}/3 | TX: {txHash} | {details}", "WARNING");
                if (errors >= 3)
                    throw new ReceiptUnavailableException($"RPC receipt checks stopped after 3 consecutive errors. TX: {txHash}. " +
                        $"Transaction outcome is unknown; check it on-chain before restarting. {details}", ex);
            }
            if (attempt < maxAttempts) await SwapExecution.Delay(delayMs);
        }
        throw new ReceiptUnavailableException($"Receipt wait timed out. TX: {txHash}. Transaction may still be pending; check it on-chain before restarting.");
    }
}
