
using Newtonsoft.Json;
using z3n;


public class Jumper
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://api.jumper.exchange/";

    public class JumperResponse
    {
        [JsonProperty("walletAddress")] public string WalletAddress { get; set; }

        [JsonProperty("balances")] public Dictionary<string, List<TokenInfo>> Balances { get; set; }

        [JsonProperty("chains")] public List<ChainInfo> Chains { get; set; }
    }

    public class ChainInfo
    {
        [JsonProperty("id")] public int Id { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("key")] public string Key { get; set; }
    }

    public class TokenInfo
    {
        [JsonProperty("symbol")] public string Symbol { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; } // В блокчейне это строка-число (BigInt)

        [JsonProperty("decimals")] public int Decimals { get; set; }

        [JsonProperty("priceUSD")] public string PriceUSD { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("address")] public string Address { get; set; }

        public decimal ValueUSD
        {
            get
            {
                if (decimal.TryParse(Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) && 
                    decimal.TryParse(PriceUSD, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    var realAmount = amount / (decimal)Math.Pow(10, Decimals);
                    return realAmount * price;
                }
                return 0;
            }
        }
        public bool IsStable => decimal.TryParse(PriceUSD, out var p) && Math.Abs(p - 1.0m) <= 0.01m;
    }

    public async Task<Dictionary<int, string>> GetChainMapping()
    {
        try
        {
            // chainTypes=EVM отфильтрует только нужные нам сети
            var response = await _httpClient.GetAsync($"{_baseUrl}pipeline/v1/chains?chainTypes=EVM");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JumperResponse>(content);

            if (data?.Chains == null) return new Dictionary<int, string>();

            // Превращаем список в словарь для удобного поиска: [1: "Ethereum", 56: "BSC", ...]
            return data.Chains.ToDictionary(x => x.Id, x => x.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке сетей: {ex.Message}");
            return new Dictionary<int, string>();
        }
    }

    private Logger _log;

    public Jumper(Logger log = null)
    {
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

    public async Task<JumperResponse> GetBalances(string address)
    {
        try
        {
            var response =
                await _httpClient.GetAsync($"{_baseUrl}pipeline/v1/wallets/{address}/balances?extended=true");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _log?.Send(content);
            var data = JsonConvert.DeserializeObject<JumperResponse>(content);
            return data;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }
}