using System.Globalization;
using System.Numerics;

namespace z3nSafe;

public static class BalanceMath
{
    // Parse raw blockchain integers before scaling: uint256 may exceed decimal.
    public static decimal GetValueUsd(string? rawAmount, int decimals, string? priceUsd)
    {
        if (decimals < 0 || decimals > 255 ||
            !BigInteger.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ||
            raw.Sign < 0 ||
            !decimal.TryParse(priceUsd, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price < 0)
            return 0m;

        var digits = raw.ToString(CultureInfo.InvariantCulture);
        if (decimals > 0)
        {
            digits = digits.PadLeft(decimals + 1, '0');
            digits = digits.Insert(digits.Length - decimals, ".");
        }
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return 0m;
        try { return amount * price; }
        catch (OverflowException) { return 0m; }
    }

    public static bool IsStable(string? priceUsd) =>
        decimal.TryParse(priceUsd, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) &&
        price >= 0.99m && price <= 1.01m;
}
