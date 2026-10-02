using Newtonsoft.Json;
using z3nSafe;

static class SwapCostChecks
{
    public static void Run(Action<string, bool> check)
    {
        check("Fees above output block the swap", !SwapCostGuard.Compare(1m, 1.01m).Allowed);
        check("Fees at output boundary follow the requested strict greater-than rule", SwapCostGuard.Compare(1m, 1m).Allowed);
        check("Zero output cannot authorize a swap", !SwapCostGuard.Compare(0, 0).Allowed);
        var lifi = JsonConvert.DeserializeObject<LiFiBridge.QuoteResponse>("""
            {"action":{"toToken":{"address":"0x0000000000000000000000000000000000000000","decimals":18,"priceUSD":"2000"}},
             "estimate":{"toAmount":"1000000000000000","toAmountMin":"500000000000000",
             "gasCosts":[{"type":"SEND","amountUSD":"0.4"},{"type":"APPROVE","amountUSD":"0.4"}],
             "feeCosts":[{"amountUSD":"0.1","included":true}]}}
            """)!;
        var result = SwapCostGuard.Evaluate(lifi);
        check("Minimum output and approval gas block an expensive LI.FI swap", !result.Allowed && result.ReceivedUSD == 1m && result.FeesUSD == 1.156m);
        lifi.Estimate.GasCosts.RemoveAt(1);
        check("Affordable LI.FI quotes are allowed", SwapCostGuard.Evaluate(lifi).Allowed);
        check("Higher current gas overrides the quote estimate", !SwapCostGuard.Evaluate(lifi, 1.1m).Allowed);
        lifi.Estimate.GasCosts[0].AmountUSD = "0,4";
        check("Malformed fee values fail closed", !SwapCostGuard.Evaluate(lifi).Allowed);
        lifi.Estimate.GasCosts = null!;
        check("Missing gas data cannot authorize transactions", !SwapCostGuard.Evaluate(lifi).Allowed);
        var relay = JsonConvert.DeserializeObject<RelayBridge.QuoteResponse>("""
            {"details":{"currencyIn":{"currency":{"chainId":1}},
             "currencyOut":{"currency":{"chainId":1,"address":"0x0000000000000000000000000000000000000000"},
              "amount":"1000","minimumAmount":"900","amountUsd":"2"}},
             "fees":{"gas":{"amountUsd":"0.1"},"relayer":{"amountUsd":"0.2"},
              "relayerGas":{"amountUsd":"0.1"},"relayerService":{"amountUsd":"0.1"},
              "app":{"amountUsd":"0.05"},"subsidized":{"amountUsd":"5"}}}
            """)!;
        result = SwapCostGuard.Evaluate(relay);
        check("Relay aggregate fees are not counted twice or inflated by subsidies", result.Allowed && result.FeesUSD == .382m && result.ReceivedUSD == 1.8m);
        relay.Fees["gas"] = Newtonsoft.Json.Linq.JObject.Parse("{\"amountUsd\":\"2\"}");
        check("Relay costly origin gas blocks the swap", !SwapCostGuard.Evaluate(relay).Allowed);
        relay.Details.Remove("currencyOut");
        check("Missing Relay output cannot authorize transactions", !SwapCostGuard.Evaluate(relay).Allowed);
    }
}
