using System.Numerics;
using z3nSafe;

static class GasPricingChecks
{
    public static async Task Run(Action<string, bool> check)
    {
        var network = new BigInteger(60000000);
        check("Gas +0% retains 0.06 Gwei", GasPricing.Price(network, 0) == 60000000);
        check("Gas +1% produces 0.0606 Gwei", GasPricing.Price(network, 1) == 60600000);
        check("Gas +2% produces 0.0612 Gwei", GasPricing.Price(network, 2) == 61200000);
        check("Fractional gas boosts preserve exact wei", GasPricing.Price(network, .01m) == 60006000);
        check("Gas arithmetic does not overflow uint256 amounts", GasPricing.Price(BigInteger.Pow(10, 60), 2) == BigInteger.Pow(10, 60) * 102 / 100);
        check("Negative, excessive and over-precise percentages are rejected", !GasPricing.IsValid(-1) && !GasPricing.IsValid(1001) && !GasPricing.IsValid(.001m));
        await SwapExecution.Run(CancellationToken.None, () => {
            check("Transaction price and fee guard share the selected boost", GasPricing.Price(network) == 61200000 && GasPricing.CostMultiplier == 1.122m);
            check("Gas log reports network and actual selected prices", GasPricing.Describe(network).Contains("0.06 Gwei +2% => 0.0612 Gwei"));
            return Task.CompletedTask;
        }, 2m);
        check("Operation gas settings are restored after completion", SwapExecution.GasBoostPercent == GasPricing.DefaultPercent);
        decimal first = 0, second = 0;
        await Task.WhenAll(
            SwapExecution.Run(CancellationToken.None, async () => { await Task.Yield(); first = SwapExecution.GasBoostPercent; }, 1m),
            SwapExecution.Run(CancellationToken.None, async () => { await Task.Yield(); second = SwapExecution.GasBoostPercent; }, 2m));
        check("Concurrent operations retain their own gas settings", first == 1m && second == 2m);
    }
}
