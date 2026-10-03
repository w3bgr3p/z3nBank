
using Newtonsoft.Json;
using z3n;
using z3nSafe;
using System.Text.RegularExpressions;


public class Jumper : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://li.quest/";
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _nextRequestAt;
    private readonly TimeSpan _requestInterval;
    private readonly int _maxRateLimitRetries;

    public class JumperResponse
    {
        [JsonProperty("walletAddress")] public string WalletAddress { get; set; }

        [JsonProperty("balances")] public Dictionary<string, List<TokenInfo>> Balances { get; set; }

        [JsonProperty("chains")] public List<ChainInfo> Chains { get; set; }
        [JsonProperty("limit")] public int? Limit { get; set; }
    }

    public class ChainInfo
    {
        [JsonProperty("id")] public int Id { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("nativeToken")] public TokenInfo? NativeToken { get; set; }
    }

    public class TokenInfo
    {
        [JsonProperty("symbol")] public string Symbol { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; } // В блокчейне это строка-число (BigInt)

        [JsonProperty("decimals")] public int Decimals { get; set; }

        [JsonProperty("priceUSD")] public string PriceUSD { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("verificationStatus")] public string? VerificationStatus { get; set; }

        public decimal ValueUSD => BalanceMath.GetValueUsd(Amount, Decimals, PriceUSD);
        public bool IsStable => BalanceMath.IsStable(PriceUSD);
    }

    public async Task<Dictionary<int, string>> GetChainMapping()
        => (await GetChains()).ToDictionary(x => x.Id, x => x.Name);

    public async Task<List<ChainInfo>> GetChains()
    {
        try
        {
            // chainTypes=EVM отфильтрует только нужные нам сети
            using var response = await GetWithRateLimitAsync($"{_baseUrl}v1/chains?chainTypes=EVM");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JumperResponse>(content);

            if (data?.Chains == null || data.Chains.Count == 0)
                throw new InvalidDataException("LI.FI returned no EVM chain metadata.");

            // Превращаем список в словарь для удобного поиска: [1: "Ethereum", 56: "BSC", ...]
            return data.Chains;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке сетей: {ex.Message}");
            throw;
        }
    }

    private Logger _log;

    public Jumper(Logger log = null)
    {
        _ownsClient = true;
        _requestInterval = TimeSpan.FromSeconds(6.1); // Public LI.FI limit: 10 requests/minute.
        _maxRateLimitRetries = 3;
        _log = log;
        var baseHandler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate |
                                     System.Net.DecompressionMethods.Brotli
        };

// 2. Оборачиваем его в ваш отладочный хендлер
        var debugHandler = new HttpDebugHandler("z3nBank")
        {
            InnerHandler = baseHandler // Теперь DebugHandler использует настроенный baseHandler
        };

// 3. Передаем голову цепочки в клиент
        _httpClient = new HttpClient(debugHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var headers = new[]
        {
            "accept: */*",
            "accept-language: en-US,en;q=0.9",
            "origin: https://jumper.exchange",
            "priority: u=1, i",
            "referer: https://jumper.exchange/",
            "sec-ch-ua: \"Google Chrome\";v=\"143\", \"Chromium\";v=\"143\", \"Not A(Brand\";v=\"24\"",
            "sec-ch-ua-mobile: ?0",
            "sec-ch-ua-platform: \"Windows\"",
            "sec-fetch-dest: empty",
            "sec-fetch-mode: cors",
            "sec-fetch-site: same-site",
            "user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36",
            "x-lifi-integrator: jumper.exchange.earn",
            "x-lifi-sdk: 3.15.4",
        };

        foreach (var header in headers)
        {
            var separatorIndex = header.IndexOf(':');
            if (separatorIndex > 0)
            {
                var key = header.Substring(0, separatorIndex).Trim();
                var value = header.Substring(separatorIndex + 1).Trim();

                // Используем TryAddWithoutValidation, чтобы избежать ошибок с "нестандартными" или защищенными заголовками
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
        }
    }

    public Jumper(HttpClient httpClient, Logger? log = null, int maxRateLimitRetries = 0)
    {
        _httpClient = httpClient;
        _log = log;
        _maxRateLimitRetries = maxRateLimitRetries;
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
        _requestLock.Dispose();
    }

    private async Task<HttpResponseMessage> GetWithRateLimitAsync(string url)
    {
        await _requestLock.WaitAsync();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var wait = _nextRequestAt - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                _nextRequestAt = DateTimeOffset.UtcNow + _requestInterval;
                var response = await _httpClient.GetAsync(url);
                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt >= _maxRateLimitRetries)
                    return response;
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
                if (delay == null && response.Headers.TryGetValues("ratelimit-reset", out var reset) &&
                    double.TryParse(reset.FirstOrDefault(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    delay = TimeSpan.FromSeconds(seconds);
                response.Dispose();
                delay ??= TimeSpan.FromSeconds(60);
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                _log?.Send($"LI.FI rate limit: waiting {delay.Value.TotalSeconds:F0}s before retry", "WARNING");
                await Task.Delay(delay.Value);
            }
        }
        finally { _requestLock.Release(); }
    }

    public async Task<JumperResponse> GetBalances(string address)
    {
        if (!Regex.IsMatch(address ?? "", "^0x[0-9a-fA-F]{40}$"))
            throw new ArgumentException("A valid EVM wallet address is required.", nameof(address));
        try
        {
            using var response =
                await GetWithRateLimitAsync($"{_baseUrl}v1/wallets/{address}/balances?extended=true");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JumperResponse>(content);
            if (data?.Balances == null || !string.Equals(data.WalletAddress, address, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("LI.FI returned an invalid wallet balance snapshot.");
            var count = data.Balances.Values.Sum(tokens => tokens?.Count ?? 0);
            if (data.Limit is > 0 && count >= data.Limit)
                throw new InvalidDataException("LI.FI balance response reached its token limit; refusing to save a partial snapshot.");
            foreach (var chain in data.Balances)
            {
                if (!int.TryParse(chain.Key, out var chainId) || chain.Value == null ||
                    chain.Value.Any(t => t == null || t.ChainId != chainId ||
                        !Regex.IsMatch(t.Address ?? "", "^0x[0-9a-fA-F]{40}$")))
                    throw new InvalidDataException("LI.FI returned invalid chain or token data.");
                chain.Value.RemoveAll(t => string.Equals(t.VerificationStatus, "denied", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(t.VerificationStatus, "malicious", StringComparison.OrdinalIgnoreCase));
            }
            return data;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            throw;
        }
    }
}
