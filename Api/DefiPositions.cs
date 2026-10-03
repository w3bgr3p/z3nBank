using Newtonsoft.Json.Linq;

namespace z3nSafe;

public sealed record DefiPosition(string Id, int AccountId, string Wallet, string Chain,
    string Protocol, string Type, string Symbol, string Amount, decimal? ValueUsd,
    string? AssetAddress, string? VaultAddress, string GroupId)
{
    public RabbyWithdrawAction[] WithdrawActions { get; init; } = [];
    public bool HasProxy { get; init; }
    public decimal DebtUsd { get; init; }
    public string? ProtocolId { get; init; }
    public string? AdapterId { get; init; }
    public string? Controller { get; init; }
    public string? PoolIndex { get; init; }
    public string? DetailJson { get; init; }
    public string? WithdrawalReason => DefiWithdrawal.Unavailable(this);
    public string? PriceSource { get; init; }
    public bool PendingWithdrawal { get; init; }
}

public sealed class DefiPositionsClient : IDisposable
{
    private readonly HttpClient _http;
    private bool _labPriceChecked;
    private decimal? _labPrice;
    public DefiPositionsClient(HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }
    public async Task<List<DefiPosition>> ReadForScanAsync(int accountId, string wallet, CancellationToken token,
        TimeSpan? accountTimeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(accountTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var rows = await ReadAsync(accountId, wallet, deadline.Token);
            try { return await FillRewardPrices(rows, deadline.Token); }
            catch (Exception) when (!token.IsCancellationRequested) { return rows; }
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("DeFi provider request timed out for this account; continuing with the next account.", ex); }
    }
    private async Task<List<DefiPosition>> FillRewardPrices(List<DefiPosition> rows, CancellationToken token)
    {
        if (!rows.Any(p => p.ValueUsd == null && DefiLayerBankRewards.IsSupported(p))) return rows;
        if (!_labPriceChecked)
        {
            _labPriceChecked = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _http.GetAsync($"https://li.quest/v1/token?chain=534352&token={DefiLayerBankRewards.Lab}", timeout.Token);
            response.EnsureSuccessStatusCode();
            var info = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if ((int?)info["chainId"] != 534352 || !string.Equals((string?)info["address"], DefiLayerBankRewards.Lab, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reward price response does not match LAB.s on Scroll");
            var price = Number(info["priceUSD"]); if (price > 0) _labPrice = price;
        }
        return rows.Select(p => p.ValueUsd == null && DefiLayerBankRewards.IsSupported(p) && Value(Number(new JValue(p.Amount)), _labPrice) is decimal value
            ? p with { ValueUsd = value, PriceSource = "LI.FI" } : p).ToList();
    }
    public async Task<List<DefiPosition>> ReadAsync(int accountId, string wallet, CancellationToken token)
    {
        if (!DefiVault.AddressValid(wallet)) throw new ArgumentException("Invalid wallet address");
        var url = $"https://api.rabby.io/v1/user/complex_protocol_list?id={wallet}";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await _http.GetAsync(url, token);
            if (((int)response.StatusCode == 503 || (int)response.StatusCode == 429) && attempt < 3)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 << attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 30)), token);
                continue;
            }
            if ((int)response.StatusCode is 401 or 403 or 429)
                throw new InvalidOperationException($"Rabby public API is unavailable ({(int)response.StatusCode}). No paid fallback is used.");
            response.EnsureSuccessStatusCode();
            return Parse(ParseJson(await response.Content.ReadAsStringAsync(token)), accountId, wallet);
        }
        throw new InvalidDataException("Rabby did not return positions");
    }
    // Preserve JSON number text: health_rate can exceed decimal, and amounts must not pass through double.
    public static JToken ParseJson(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        JToken Convert(System.Text.Json.JsonElement element) => element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => new JObject(element.EnumerateObject().Select(p => new JProperty(p.Name, Convert(p.Value)))),
            System.Text.Json.JsonValueKind.Array => new JArray(element.EnumerateArray().Select(Convert)),
            System.Text.Json.JsonValueKind.Number => new JValue(element.GetRawText()),
            System.Text.Json.JsonValueKind.String => new JValue(element.GetString()),
            System.Text.Json.JsonValueKind.True => new JValue(true),
            System.Text.Json.JsonValueKind.False => new JValue(false),
            _ => JValue.CreateNull()
        };
        return Convert(document.RootElement);
    }
    private static decimal? Number(JToken? token) => decimal.TryParse((string?)token,
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    private static decimal? Value(decimal? amount, decimal? price)
    {
        if (amount == null || price == null || price <= 0) return null;
        try { return amount * price; } catch (OverflowException) { return null; }
    }
    public static List<DefiPosition> Parse(JToken body, int accountId, string wallet)
    {
        if (body is not JArray protocols) throw new InvalidDataException("Missing Rabby protocols array; response is not a zero balance");
        var result = new List<DefiPosition>();
        foreach (var protocol in protocols)
        {
            var chain = (string?)protocol["chain"] ?? throw new InvalidDataException("Missing protocol network");
            var protocolId = (string?)protocol["id"] ?? throw new InvalidDataException("Missing protocol ID");
            if (protocol["portfolio_item_list"] is not JArray items) throw new InvalidDataException("Missing portfolio items");
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var group = $"{chain}:{protocolId}:{(string?)item["pool"]?["id"]}:{index}";
                var detail = item["detail"] ?? throw new InvalidDataException("Missing portfolio detail");
                var before = result.Count;
                var supplied = false;
                foreach (var (field, type) in new[] { ("supply_token_list", "deposit"), ("borrow_token_list", "loan"), ("reward_token_list", "reward"), ("collateral_token_list", "collateral"), ("token_list", "deposit"), ("token", "vesting") })
                {
                    var tokens = detail[field] as JArray ?? (field == "token" && detail[field] is JObject single ? new JArray(single) : null);
                    if (tokens == null) continue;
                    if (field == "token_list" && supplied) continue;
                    if (field == "supply_token_list") supplied = tokens.Count > 0;
                    var tokenIndex = 0;
                    foreach (var token in tokens)
                    {
                        var rawAmount = (string?)token["amount"] ?? throw new InvalidDataException("Missing protocol token amount");
                        var amount = Number(token["amount"]);
                        if (amount <= 0) continue;
                        var price = Number(token["price"]);
                        var name = ((string?)item["name"] ?? "").ToLowerInvariant();
                        var kind = type == "deposit" && name.Contains("reward") ? "reward" : type == "deposit" && name.Contains("stak") ? "staked" : type == "deposit" && name.Contains("lock") ? "locked" : type;
                        result.Add(new DefiPosition($"{group}:{field}:{tokenIndex++}", accountId, wallet, chain,
                            (string?)protocol["name"] ?? protocolId, kind, (string?)token["optimized_symbol"] ?? (string?)token["symbol"] ?? "?",
                            rawAmount, Value(amount, price),
                            AssetId((string?)token["id"], chain), PoolId((string?)item["pool"]?["id"], (string?)protocol["name"]), group) {
                                WithdrawActions = item["withdraw_actions"]?.ToObject<RabbyWithdrawAction[]>() ?? [],
                                DebtUsd = Number(item["stats"]?["debt_usd_value"]) ?? 0,
                                ProtocolId = protocolId,
                                AdapterId = (string?)item["pool"]?["adapter_id"], Controller = (string?)item["pool"]?["controller"],
                                PoolIndex = (string?)item["pool"]?["index"], DetailJson = detail.ToString(Newtonsoft.Json.Formatting.None),
                                HasProxy = !string.IsNullOrWhiteSpace((string?)item["proxy_detail"]?["proxy_contract_id"])
                            });
                    }
                }
                if (result.Count == before)
                    result.Add(new DefiPosition(group + ":unknown", accountId, wallet, chain,
                        (string?)protocol["name"] ?? protocolId, "unknown", (string?)item["name"] ?? "Position", "Unknown",
                        Number(item["stats"]?["net_usd_value"]), null, PoolId((string?)item["pool"]?["id"], (string?)protocol["name"]), group) {
                            WithdrawActions = item["withdraw_actions"]?.ToObject<RabbyWithdrawAction[]>() ?? [],
                            DebtUsd = Number(item["stats"]?["debt_usd_value"]) ?? 0,
                            ProtocolId = protocolId,
                            AdapterId = (string?)item["pool"]?["adapter_id"], Controller = (string?)item["pool"]?["controller"],
                            PoolIndex = (string?)item["pool"]?["index"], DetailJson = detail.ToString(Newtonsoft.Json.Formatting.None),
                            HasProxy = !string.IsNullOrWhiteSpace((string?)item["proxy_detail"]?["proxy_contract_id"])
                        });
            }
        }
        return result;
    }
    public void Dispose() => _http.Dispose();
    public static string? AssetId(string? id, string chain) => id == chain ? "0x0000000000000000000000000000000000000000" : id;
    public static string? PoolId(string? id, string? protocol)
    {
        if (protocol == "GMX" && string.Equals(id, DefiGmxGlp.Tracker + ":" + DefiGmxGlp.FeeTracker, StringComparison.OrdinalIgnoreCase)) return DefiGmxGlp.Tracker;
        if (protocol == "Sablier" && id?.EndsWith(":receiver", StringComparison.Ordinal) == true && DefiVault.AddressValid(id[..^9])) return id[..^9];
        if (protocol == "Compound V3" && id != null)
            foreach (var suffix in new[] { ":lending", ":yield" })
                if (id.EndsWith(suffix, StringComparison.Ordinal) && DefiVault.AddressValid(id[..^suffix.Length])) return id[..^suffix.Length];
        return id;
    }
}
