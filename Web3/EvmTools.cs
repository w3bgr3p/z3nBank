using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Класс для работы с EVM RPC
/// Использует HttpClient для HTTP запросов
/// </summary>
public class EvmTools
{
    private readonly HttpClient _httpClient;
    private readonly bool _log;

    public EvmTools(bool log = false, HttpClient httpClient = null)
    {
        _log = log;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Выполняет POST запрос к RPC
    /// </summary>
    private async Task<string> PostRpcAsync(string rpc, string jsonBody)
    {
        try
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(rpc, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            if (_log) Console.WriteLine($"HTTP Request error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ожидает выполнения транзакции с расширенной информацией о pending статусе
    /// </summary>
    public async Task<bool> WaitTxExtended(string rpc, string hash, int deadline = 60)
    {
        string jsonReceipt = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_getTransactionReceipt"", ""params"": [""{hash}""], ""id"": 1 }}";
        string jsonRaw = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_getTransactionByHash"", ""params"": [""{hash}""], ""id"": 1 }}";

        var startTime = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(deadline);

        while (true)
        {
            if (DateTime.Now - startTime > timeout)
                throw new Exception($"Timeout {deadline}s");

            try
            {
                string body = await PostRpcAsync(rpc, jsonReceipt);

                if (string.IsNullOrEmpty(body))
                {
                    if (_log) Console.WriteLine($"Empty response (receipt)");
                    await Task.Delay(2000);
                    continue;
                }

                var json = JObject.Parse(body);

                if (json["result"] == null || json["result"].Type == JTokenType.Null)
                {
                    body = await PostRpcAsync(rpc, jsonRaw);

                    if (string.IsNullOrEmpty(body))
                    {
                        if (_log) Console.WriteLine($"Empty response (raw)");
                        await Task.Delay(2000);
                        continue;
                    }

                    var rawJson = JObject.Parse(body);

                    if (rawJson["result"] == null || rawJson["result"].Type == JTokenType.Null)
                    {
                        if (_log) Console.WriteLine($"[{rpc} {hash}] not found");
                    }
                    else
                    {
                        if (_log)
                        {
                            string gas = (rawJson["result"]?["maxFeePerGas"]?.ToString() ?? "0").Replace("0x", "");
                            string gasPrice = (rawJson["result"]?["gasPrice"]?.ToString() ?? "0").Replace("0x", "");
                            string nonce = (rawJson["result"]?["nonce"]?.ToString() ?? "0").Replace("0x", "");
                            string value = (rawJson["result"]?["value"]?.ToString() ?? "0").Replace("0x", "");
                            
                            if (string.IsNullOrEmpty(gas)) gas = "0";
                            if (string.IsNullOrEmpty(gasPrice)) gasPrice = "0";
                            if (string.IsNullOrEmpty(nonce)) nonce = "0";
                            if (string.IsNullOrEmpty(value)) value = "0";
                            
                            Console.WriteLine($"[{rpc} {hash}] pending  gasLimit:[{BigInteger.Parse(gas, NumberStyles.AllowHexSpecifier)}] gasNow:[{BigInteger.Parse(gasPrice, NumberStyles.AllowHexSpecifier)}] nonce:[{BigInteger.Parse(nonce, NumberStyles.AllowHexSpecifier)}] value:[{BigInteger.Parse(value, NumberStyles.AllowHexSpecifier)}]");
                        }
                    }
                }
                else
                {
                    string status = json["result"]?["status"]?.ToString().Replace("0x", "") ?? "0";
                    string gasUsed = json["result"]?["gasUsed"]?.ToString().Replace("0x", "") ?? "0";
                    string gasPrice = json["result"]?["effectiveGasPrice"]?.ToString().Replace("0x", "") ?? "0";

                    bool success = status == "1";
                    if (_log)
                    {
                        Console.WriteLine($"[{rpc} {hash}] {(success ? "SUCCESS" : "FAIL")} gasUsed: {BigInteger.Parse(gasUsed, NumberStyles.AllowHexSpecifier)}");
                    }
                    return success;
                }
            }
            catch (Exception ex)
            {
                if (_log) Console.WriteLine($"Request error: {ex.Message}");
                await Task.Delay(2000);
                continue;
            }

            await Task.Delay(3000);
        }
    }

    /// <summary>
    /// Ожидает выполнения транзакции (упрощенная версия)
    /// </summary>
    public async Task<bool> WaitTx(string rpc, string hash, int deadline = 60)
    {
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_getTransactionReceipt"", ""params"": [""{hash}""], ""id"": 1 }}";

        var startTime = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(deadline);

        while (true)
        {
            if (DateTime.Now - startTime > timeout)
                throw new Exception($"Timeout {deadline}s");

            try
            {
                string body = await PostRpcAsync(rpc, jsonBody);

                if (string.IsNullOrEmpty(body))
                {
                    if (_log) Console.WriteLine($"Empty response");
                    await Task.Delay(2000);
                    continue;
                }

                var json = JObject.Parse(body);

                if (json["result"] == null || json["result"].Type == JTokenType.Null)
                {
                    if (_log) Console.WriteLine($"[{rpc} {hash}] not found");
                    await Task.Delay(2000);
                    continue;
                }

                string status = json["result"]?["status"]?.ToString().Replace("0x", "") ?? "0";
                bool success = status == "1";
                if (_log) Console.WriteLine($"[{rpc} {hash}] {(success ? "SUCCESS" : "FAIL")}");
                return success;
            }
            catch (Exception ex)
            {
                if (_log) Console.WriteLine($"Request error: {ex.Message}");
                await Task.Delay(2000);
                continue;
            }
        }
    }

    /// <summary>
    /// Получает баланс нативной монеты
    /// </summary>
    public async Task<string> Native(string rpc, string address)
    {
        address = address.NormalizeAddress();
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_getBalance"", ""params"": [""{address}"", ""latest""], ""id"": 1 }}";

        string body = await PostRpcAsync(rpc, jsonBody);
        var json = JObject.Parse(body);
        string hexBalance = json["result"]?.ToString().Replace("0x", "") ?? "0";
        return hexBalance;
    }

    /// <summary>
    /// Получает баланс ERC20 токена
    /// </summary>
    public async Task<string> Erc20(string tokenContract, string rpc, string address)
    {
        tokenContract = tokenContract.NormalizeAddress();
        address = address.NormalizeAddress();
        string data = "0x70a08231000000000000000000000000" + address.Replace("0x", "");
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_call"", ""params"": [{{ ""to"": ""{tokenContract}"", ""data"": ""{data}"" }}, ""latest""], ""id"": 1 }}";

        string body = await PostRpcAsync(rpc, jsonBody);
        var json = JObject.Parse(body);
        string hexBalance = json["result"]?.ToString().Replace("0x", "") ?? "0";
        return hexBalance;
    }

    public async Task<decimal> Erc20(string tokenContract, string rpc, string address, int decimals)
    {
        var balanceHex = await Erc20(tokenContract, rpc, address);
        BigInteger number = BigInteger.Parse("0" + balanceHex, NumberStyles.AllowHexSpecifier);
        return ToDecimal(number, decimals);
        
    }
    private static decimal ToDecimal( BigInteger balanceWei, int decimals = 18)
    {
        BigInteger divisor = BigInteger.Pow(10, decimals);
        BigInteger integerPart = balanceWei / divisor;
        BigInteger fractionalPart = balanceWei % divisor;

        decimal result = (decimal)integerPart + ((decimal)fractionalPart / (decimal)divisor);
        return result;
    }
    private static decimal ToDecimal( string balanceHex, int decimals = 18)
    {
        BigInteger number = BigInteger.Parse("0" + balanceHex, NumberStyles.AllowHexSpecifier);
        return ToDecimal(number, decimals);
    }
    

   
    
    /// <summary>
    /// Получает баланс ERC721 NFT
    /// </summary>
    public async Task<string> Erc721(string tokenContract, string rpc, string address)
    {
        tokenContract = tokenContract.NormalizeAddress();
        address = address.NormalizeAddress();
        string data = "0x70a08231000000000000000000000000" + address.Replace("0x", "").ToLower();
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_call"", ""params"": [{{ ""to"": ""{tokenContract}"", ""data"": ""{data}"" }}, ""latest""], ""id"": 1 }}";

        string body = await PostRpcAsync(rpc, jsonBody);
        var json = JObject.Parse(body);
        string hexBalance = json["result"]?.ToString().Replace("0x", "") ?? "0";
        return hexBalance;
    }

    /// <summary>
    /// Получает баланс ERC1155 токена
    /// </summary>
    public async Task<string> Erc1155(string tokenContract, string tokenId, string rpc, string address)
    {
        tokenContract = tokenContract.NormalizeAddress();
        address = address.NormalizeAddress();
        string data = "0x00fdd58e" + address.Replace("0x", "").ToLower().PadLeft(64, '0') + BigInteger.Parse(tokenId).ToString("x").PadLeft(64, '0');
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_call"", ""params"": [{{ ""to"": ""{tokenContract}"", ""data"": ""{data}"" }}, ""latest""], ""id"": 1 }}";

        string body = await PostRpcAsync(rpc, jsonBody);
        var json = JObject.Parse(body);
        string hexBalance = json["result"]?.ToString().Replace("0x", "") ?? "0";
        return hexBalance;
    }

    /// <summary>
    /// Получает nonce (количество транзакций) для адреса
    /// </summary>
    public async Task<string> Nonce(string rpc, string address)
    {
        address = address.NormalizeAddress();
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_getTransactionCount"", ""params"": [""{address}"", ""latest""], ""id"": 1 }}";

        try
        {
            string body = await PostRpcAsync(rpc, jsonBody);
            var json = JObject.Parse(body);
            string hexResult = json["result"]?.ToString()?.Replace("0x", "") ?? "0";
            return hexResult;
        }
        catch (Exception ex)
        {
            if (_log) Console.WriteLine($"Request error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Получает Chain ID сети
    /// </summary>
    public async Task<string> ChainId(string rpc)
    {
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_chainId"", ""params"": [], ""id"": 1 }}";

        try
        {
            string body = await PostRpcAsync(rpc, jsonBody);
            var json = JObject.Parse(body);
            return json["result"]?.ToString() ?? "0x0";
        }
        catch (Exception ex)
        {
            if (_log) Console.WriteLine($"Request error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Получает текущую цену газа
    /// </summary>
    public async Task<string> GasPrice(string rpc)
    {
        string jsonBody = $@"{{ ""jsonrpc"": ""2.0"", ""method"": ""eth_gasPrice"", ""params"": [], ""id"": 1 }}";

        try
        {
            string body = await PostRpcAsync(rpc, jsonBody);
            var json = JObject.Parse(body);
            return json["result"]?.ToString()?.Replace("0x", "") ?? "0";
        }
        catch (Exception ex)
        {
            if (_log) Console.WriteLine($"Request error: {ex.Message}");
            throw;
        }
    }
}

