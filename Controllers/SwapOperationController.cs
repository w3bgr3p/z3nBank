using Microsoft.AspNetCore.Mvc;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    private static readonly Dictionary<Guid, CancellationTokenSource> SwapOperations = new();

    private static (Guid Id, CancellationTokenSource Cancellation) RegisterSwap()
    {
        lock (TokenSwapLock)
        {
            var source = new CancellationTokenSource();
            var id = Guid.NewGuid();
            SwapOperations.Add(id, source);
            return (id, source);
        }
    }
    private static void FinishSwap(Guid id)
    {
        lock (TokenSwapLock)
        {
            if (SwapOperations.Remove(id, out var source)) source.Dispose();
        }
    }

    [HttpGet("swaps/status")]
    public IActionResult ActiveSwaps()
    {
        lock (TokenSwapLock) return Ok(new { active = SwapOperations.Count,
            balanceRevision = TreasuryRpcBalances.Revision,
            stopping = SwapOperations.Values.Any(s => s.IsCancellationRequested) });
    }

    [HttpPost("swaps/stop")]
    public IActionResult StopSwaps()
    {
        lock (TokenSwapLock)
        {
            foreach (var source in SwapOperations.Values) source.Cancel();
            _log.Send("Stop requested: no further swap transactions will be started. Already broadcast transactions remain pending on-chain.", "WARNING");
            return Accepted(new { stopping = SwapOperations.Count });
        }
    }
}
