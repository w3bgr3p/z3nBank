using Nethereum.RPC.Eth.DTOs;
using z3nSafe;

static class CancellationChecks
{
    public static async Task Run(Action<string, bool> check)
    {
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextTokenStarted = false;
        var waiting = SwapExecution.Run(stop.Token, async () => {
            started.SetResult();
            await ReceiptWaiter.WaitAsync(() => new TaskCompletionSource<TransactionReceipt>().Task, "0xpending", null);
            nextTokenStarted = true;
        });
        await started.Task;
        stop.Cancel();
        try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); check("Stopping interrupts an in-flight receipt RPC", false); }
        catch (OperationCanceledException) { check("Stopping interrupts an in-flight receipt RPC", true); }
        check("A stopped wait cannot advance to another token", !nextTokenStarted);
        check("Operation cancellation does not leak to other operations", !SwapExecution.Token.IsCancellationRequested);

        var reads = 0;
        try
        {
            await ReceiptWaiter.WaitAsync(() => {
                reads++;
                return Task.FromException<TransactionReceipt>(new Exception("RPC wrapper", new Exception("HTTP 429 rate limit")));
            }, "0xfailed", null, delayMs: 0);
            check("Consecutive RPC errors halt receipt checks", false);
        }
        catch (ReceiptUnavailableException ex)
        {
            check("Consecutive RPC errors halt receipt checks", reads == 3);
            check("RPC failures retain the underlying reason and transaction hash", ex.Message.Contains("429") && ex.Message.Contains("0xfailed"));
        }
        reads = 0;
        var expected = new TransactionReceipt();
        var receipt = await ReceiptWaiter.WaitAsync(() => {
            reads++;
            return Task.FromResult(reads < 3 ? null! : expected);
        }, "0xmining", null, delayMs: 0);
        check("Pending receipts remain eligible for normal mining waits", receipt == expected && reads == 3);

        var primaryReads = 0;
        var fallbackReads = 0;
        receipt = await ReceiptWaiter.WaitAsync(() => {
            primaryReads++;
            return Task.FromException<TransactionReceipt>(new Exception("HTTP 403 archive access denied"));
        }, "0xfallback", null, delayMs: 0, fallback: () => {
            fallbackReads++;
            return Task.FromResult(fallbackReads < 2 ? null! : expected);
        });
        check("Restricted RPC switches to fallback without resubmitting a transaction", primaryReads == 1 && fallbackReads == 2 && receipt == expected);

        using var delayStop = new CancellationTokenSource();
        var delaying = SwapExecution.Run(delayStop.Token, () => SwapExecution.Delay(60000));
        delayStop.Cancel();
        try { await delaying.WaitAsync(TimeSpan.FromSeconds(2)); check("Stopping interrupts retry backoff", false); }
        catch (OperationCanceledException) { check("Stopping interrupts retry backoff", true); }
    }
}
