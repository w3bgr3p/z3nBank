using System.Numerics;
using System.Globalization;

namespace z3nSafe;

public static class GasPricing
{
    public const decimal DefaultPercent = 20m;
    public static bool IsValid(decimal percent) => percent is >= 0 and <= 1000 &&
        decimal.Truncate(percent * 100) == percent * 100;
    public static decimal CostMultiplier => (1 + SwapExecution.GasBoostPercent / 100m) * 1.1m;
    public static string Describe(BigInteger networkPrice) => string.Create(CultureInfo.InvariantCulture,
        $"Gas price: {(decimal)networkPrice / 1000000000m:0.#########} Gwei +{SwapExecution.GasBoostPercent:0.##}% => {(decimal)Price(networkPrice) / 1000000000m:0.#########} Gwei");

    public static BigInteger Price(BigInteger networkPrice, decimal? percent = null)
    {
        var selected = percent ?? SwapExecution.GasBoostPercent;
        if (!IsValid(selected) || networkPrice < 0) throw new ArgumentOutOfRangeException(nameof(percent));
        var factor = 10000 + new BigInteger(selected * 100);
        return (networkPrice * factor + 9999) / 10000;
    }
}
