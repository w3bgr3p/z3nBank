using Nethereum.RPC.Eth.DTOs;
using z3n;
using Nethereum.Web3;

namespace z3nSafe;

public static class ReceiptWaiter
{
    private static readonly Lazy<Web3> BscFallback = new(() => new Web3("https://bsc-dataseed1.bnbchain.org"));
    public static Func<Task<TransactionReceipt>>? Fallback(int chainId, string hash) => chainId == 56
        ? () => BscFallback.Value.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(hash) : null;

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
