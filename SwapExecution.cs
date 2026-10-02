namespace z3nSafe;

public sealed class ReceiptUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public static class SwapExecution
{
    private static readonly AsyncLocal<CancellationToken> Current = new();
    private static readonly AsyncLocal<decimal?> CurrentGasBoost = new();
    public static decimal GasBoostPercent => CurrentGasBoost.Value ?? GasPricing.DefaultPercent;
    public static CancellationToken Token => Current.Value;
    public static void Check() => Token.ThrowIfCancellationRequested();
    public static Task Delay(int milliseconds) => Task.Delay(milliseconds, Token);
    public static Task Delay(TimeSpan duration) => Task.Delay(duration, Token);
    public static async Task<T> Read<T>(Task<T> task) => await task.WaitAsync(Token);
    public static async Task Run(CancellationToken token, Func<Task> work, decimal gasBoostPercent = GasPricing.DefaultPercent)
    {
        if (!GasPricing.IsValid(gasBoostPercent)) throw new ArgumentOutOfRangeException(nameof(gasBoostPercent));
        var previous = Current.Value;
        var previousGasBoost = CurrentGasBoost.Value;
        Current.Value = token;
        CurrentGasBoost.Value = gasBoostPercent;
        try { Check(); await work(); }
        finally { Current.Value = previous; CurrentGasBoost.Value = previousGasBoost; }
    }

    public static string ErrorDetails(Exception error)
    {
        var messages = new List<string>();
        for (var current = error; current != null; current = current.InnerException)
            if (!messages.Contains(current.Message)) messages.Add(current.Message);
        return string.Join(" | ", messages);
    }
}
