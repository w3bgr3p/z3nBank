using System.Net.Http.Headers;
using System.Numerics;
using System.Text;

using NetDebugWrapper;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Newtonsoft.Json;
using z3n;

namespace RelayBridge
{
    #region Models

    public class RpcConfig
    {
        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("httpRpcUrl")] public string HttpRpcUrl { get; set; }
    }

    public class FallbackRpcConfig
    {
        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("fallbackUrls")] public List<string> FallbackUrls { get; set; }
    }

    public class AppFee
    {
        [JsonProperty("recipient")] public string Recipient { get; set; }

        [JsonProperty("fee")] public string Fee { get; set; }
    }

    public class QuoteRequest
    {
        [JsonProperty("user")] public string User { get; set; }

        [JsonProperty("recipient")] public string Recipient { get; set; }

        [JsonProperty("originChainId")] public int OriginChainId { get; set; }

        [JsonProperty("destinationChainId")]
        public int DestinationChainId { get; set; }

        [JsonProperty("originCurrency")] public string OriginCurrency { get; set; }

        [JsonProperty("destinationCurrency")]
        public string DestinationCurrency { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; }

        [JsonProperty("slippageTolerance")]
        public string SlippageTolerance { get; set; }

        [JsonProperty("source")] public string Source { get; set; }

        [JsonProperty("appFees")] public List<AppFee> AppFees { get; set; }

        [JsonProperty("tradeType")] public string TradeType { get; set; }
    }

    public class TransactionData
    {
        [JsonProperty("to")] public string To { get; set; }

        [JsonProperty("data")] public string Data { get; set; }

        [JsonProperty("value")] public string Value { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("gas")] public long? Gas { get; set; }

        [JsonProperty("gasPrice")] public string GasPrice { get; set; }

        [JsonProperty("maxFeePerGas")] public string MaxFeePerGas { get; set; }

        [JsonProperty("maxPriorityFeePerGas")]
        public string MaxPriorityFeePerGas { get; set; }
    }

    public class SignatureData
    {
        [JsonProperty("signatureKind")] public string SignatureKind { get; set; }

        [JsonProperty("message")] public string Message { get; set; }
    }

    public class PostData
    {
        [JsonProperty("endpoint")] public string Endpoint { get; set; }

        [JsonProperty("body")] public Dictionary<string, object> Body { get; set; }
    }

    public class StepItemData
    {
        [JsonProperty("data")] public TransactionData Data { get; set; }

        [JsonProperty("sign")] public SignatureData Sign { get; set; }

        [JsonProperty("post")] public PostData Post { get; set; }

        [JsonProperty("check")] public CheckData Check { get; set; }
    }

    public class CheckData
    {
        [JsonProperty("endpoint")] public string Endpoint { get; set; }
    }

    public class TransactionStep
    {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("action")] public string Action { get; set; }
        
        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("kind")] public string Kind { get; set; }

        [JsonProperty("items")] public List<StepItemData> Items { get; set; }
    }

    public class QuoteResponse
    {
        [JsonProperty("steps")] public List<TransactionStep> Steps { get; set; }

        [JsonProperty("fees")] public Dictionary<string, object> Fees { get; set; }

        [JsonProperty("details")] public Dictionary<string, object> Details { get; set; }
    }

    public class ExecutionStatus
    {
        [JsonProperty("status")] public string Status { get; set; }

        [JsonProperty("details")] public Dictionary<string, object> Details { get; set; }
    }

    public class StepResult
    {
        public string Step { get; set; }
        public string Status { get; set; }
        public string TxHash { get; set; }
        public string Error { get; set; }
        public string Signature { get; set; }
        public ExecutionStatus Details { get; set; }
    }

    #endregion

    public class RelayBridgeClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _source = "z3nBank";
        private Logger _log;
        private List<RpcConfig> _rpcConfigs;
        private List<FallbackRpcConfig> _fallbackRpcConfigs;

        public RelayBridgeClient(string apiKey = null, bool isTestnet = false, Logger log = null)
        {
            _baseUrl = isTestnet ? "https://api.testnets.relay.link" : "https://api.relay.link";
            _apiKey = apiKey;
            _log = log;

            
            
            var debugHandler = new HttpDebugHandler("z3nBank") 
            { 
                InnerHandler = new HttpClientHandler() // Стандартный обработчик в конце цепочки
            };

            _httpClient = new HttpClient(debugHandler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }



            // Загрузка конфигураций RPC (в реальном проекте загружайте из файлов)
            _rpcConfigs = new List<RpcConfig>();
            _fallbackRpcConfigs = new List<FallbackRpcConfig>();
        }

        #region Configuration Methods
        

        private List<string> GetAllRpcUrls(int chainId)
        {
            var urls = new List<string>();

            var mainConfig = _rpcConfigs.FirstOrDefault(c => c.ChainId == chainId);
            if (mainConfig != null)
            {
                urls.Add(mainConfig.HttpRpcUrl);
            }

            var fallbackConfig = _fallbackRpcConfigs.FirstOrDefault(c => c.ChainId == chainId);
            if (fallbackConfig?.FallbackUrls != null)
            {
                urls.AddRange(fallbackConfig.FallbackUrls);
            }

            return urls;
        }

        private string GetChainName(int chainId)
        {
            var config = _rpcConfigs.FirstOrDefault(c => c.ChainId == chainId);
            return config != null ? config.Name.ToUpper() : $"Chain {chainId}";
        }

        #endregion

        #region App Fees

        private List<AppFee> GenerateAppFees()
        {
            var recipient =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String("MHgwMDAwOUIzMDA5N0IxOGFENTI1MTFFNDY5Y0Q2ZDYyNkJEMzUwM0FF"));
            var fee = Encoding.UTF8.GetString(Convert.FromBase64String("NTA="));

            return new List<AppFee>
            {
                new AppFee
                {
                    Recipient = recipient,
                    Fee = fee
                }
            };
        }

        #endregion

        #region API Methods

        public async Task<List<Dictionary<string, object>>> GetChainsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/chains");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(content);
            }
            catch (Exception ex)
            {
                throw HandleApiError(ex, "getChains");
            }
        }

        public async Task<Dictionary<string, object>> GetTokenPriceAsync(string address, int chainId)
        {
            try
            {
                var response =
                    await _httpClient.GetAsync(
                        $"{_baseUrl}/currencies/token/price?address={address}&chainId={chainId}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
            }
            catch (Exception ex)
            {
                throw HandleApiError(ex, "getTokenPrice");
            }
        }

        public async Task<QuoteResponse> GetQuoteAsync(QuoteRequest quoteRequest)
        {
            const int maxRetries = 3;
            Exception lastError = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                string rawResponse = null;
                try
                {
                    if (quoteRequest.AppFees == null)
                    {
                        quoteRequest.AppFees = GenerateAppFees();
                    }

                    if (string.IsNullOrEmpty(quoteRequest.Source))
                    {
                        quoteRequest.Source = _source;
                    }
                    
                    var json = JsonConvert.SerializeObject(quoteRequest);
                    //_log?.Send(json);
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var requestJson = JsonConvert.SerializeObject(quoteRequest, Formatting.Indented);
                    _log?.Send("=> " + requestJson);
                    var response = await _httpClient.PostAsync($"{_baseUrl}/quote", content);

                    var responseContent = await response.Content.ReadAsStringAsync();
                    rawResponse = $"{responseContent}";
                    

                    response.EnsureSuccessStatusCode();
                    _log?.Send($"<== " +$"{response.StatusCode}: {responseContent}");
                    var quote = JsonConvert.DeserializeObject<QuoteResponse>(responseContent);

                    ValidateQuoteResponse(quote);
                    return quote;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _log?.Send($"[{ex.Message}]\n<=={rawResponse}","ERROR");

                    if (attempt == maxRetries || (ex is HttpRequestException && !IsRetryableError(ex)))
                    {
                        break;
                    }

                    await Task.Delay(2000 * attempt);
                }
            }

            throw HandleApiError(lastError, "получения котировки");
        }

        public async Task<ExecutionStatus> GetExecutionStatusAsync(string requestId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/intents/status/v2?requestId={requestId}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ExecutionStatus>(content);
            }
            catch (Exception ex)
            {
                throw HandleApiError(ex, "getExecutionStatus");
            }
        }

        public async Task<Dictionary<string, object>> NotifyTransactionIndexedAsync(string transactionHash, int chainId)
        {
            try
            {
                var payload = new
                {
                    txHash = transactionHash,
                    chainId = chainId.ToString()
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/transactions/index", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
            }
            catch (Exception ex)
            {
                _log?.Send($"⚠️ Не удалось уведомить о транзакции: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Provider Methods

        public async Task<Web3> CreateProviderWithFallbackAsync(int chainId)
        {
            var urls = GetAllRpcUrls(chainId);
            var chainName = GetChainName(chainId);

            if (urls.Count == 0)
            {
                _log?.Send($"   ❌ RPC URL не найден для {chainName}");
                return null;
            }

            _log?.Send($"   🔍 Попытка подключения к {chainName} ({urls.Count} RPC доступно)");

            var mainRpcUrl = urls[0];
            var fallbackUrls = urls.Skip(1).ToList();

            // Пробуем основной RPC
            _log?.Send($"\n Тестирование основного RPC: {mainRpcUrl}");
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    _log?.Send($"   🔄 Попытка {attempt}/3 основного RPC");

                    var web3 = new Web3(mainRpcUrl);
                    var networkTask = web3.Eth.ChainId.SendRequestAsync();
                    var timeoutTask = Task.Delay(5000);

                    var completedTask = await Task.WhenAny(networkTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException("Timeout");
                    }

                    await networkTask;
                    _log?.Send($"   ✅ Основной RPC работает! Подключение к {chainName} установлено");
                    return web3;
                }
                catch (Exception ex)
                {
                    _log?.Send($"   ❌ Попытка {attempt}/3 основного RPC неудачна: {ex.Message}");

                    if (mainRpcUrl.Contains("drpc.org") &&
                        (ex.Message.Contains("free tier") || ex.Message.Contains("Batch of more than 3")))
                    {
                        _log?.Send($"   ⚠️ drpc.org имеет ограничения - переходим к fallback RPC");
                        break;
                    }

                    if (attempt < 3)
                    {
                        _log?.Send($"   ⏳ Ожидание 2 секунды перед следующей попыткой...");
                        await Task.Delay(2000);
                    }
                }
            }

            // Пробуем fallback RPC
            if (fallbackUrls.Count > 0)
            {
                _log?.Send(
                    $"\n🔄 Основной RPC недоступен, переходим к fallback RPC ({fallbackUrls.Count} доступно)");

                for (int i = 0; i < fallbackUrls.Count; i++)
                {
                    try
                    {
                        _log?.Send($"   📡 Попытка {i + 1}/{fallbackUrls.Count}: fallback RPC");

                        var web3 = new Web3(fallbackUrls[i]);
                        var networkTask = web3.Eth.ChainId.SendRequestAsync();
                        var timeoutTask = Task.Delay(5000);

                        var completedTask = await Task.WhenAny(networkTask, timeoutTask);
                        if (completedTask == timeoutTask)
                        {
                            throw new TimeoutException("Timeout");
                        }

                        await networkTask;
                        _log?.Send($"   ✅ Fallback RPC работает! Подключение к {chainName} установлено");
                        return web3;
                    }
                    catch (Exception ex)
                    {
                        _log?.Send($"   ❌ Fallback RPC {i + 1}/{fallbackUrls.Count} недоступен: {ex.Message}");

                        if (i == fallbackUrls.Count - 1)
                        {
                            _log?.Send($"   ⚠️  Все RPC для {chainName} недоступны, пропускаем сеть");
                            return null;
                        }
                    }
                }
            }
            else
            {
                _log?.Send($"   ⚠️  Fallback RPC не настроены для {chainName}, пропускаем сеть");
            }

            return null;
        }

        #endregion

        #region Transaction Execution

        public async Task<List<StepResult>> ExecuteStepsAsync(List<TransactionStep> steps, Web3 web3, Account account)
        {
            var results = new List<StepResult>();

            if (steps == null || steps.Count == 0)
            {
                return results;
            }

            foreach (var step in steps)
            {
                if (step.Items == null || step.Items.Count == 0)
                {
                    _log?.Send($"skip {step.Id} ");
                    continue;
                }

                _log?.Send($" {step.Description ?? step.Id}");

                foreach (var item in step.Items)
                {
                    if (step.Kind == "transaction")
                    {
                        try
                        {
                            var txData = item.Data;

                            if (string.IsNullOrEmpty(txData.To) || string.IsNullOrEmpty(txData.Data))
                            {
                                throw new Exception("Некорректные данные транзакции");
                            }

                            var transactionInput = new TransactionInput
                            {
                                From = account.Address,
                                To = txData.To,
                                Data = txData.Data,
                                Value = new HexBigInteger(BigInteger.Parse(txData.Value ?? "0")),
                                ChainId = new HexBigInteger(txData.ChainId)
                            };

                            if (!string.IsNullOrEmpty(txData.MaxFeePerGas))
                            {
                                transactionInput.MaxFeePerGas =
                                    new HexBigInteger(BigInteger.Parse(txData.MaxFeePerGas));
                            }

                            if (!string.IsNullOrEmpty(txData.MaxPriorityFeePerGas))
                            {
                                transactionInput.MaxPriorityFeePerGas =
                                    new HexBigInteger(BigInteger.Parse(txData.MaxPriorityFeePerGas));
                            }

                            if (!string.IsNullOrEmpty(txData.GasPrice) && string.IsNullOrEmpty(txData.MaxFeePerGas))
                            {
                                transactionInput.GasPrice = new HexBigInteger(BigInteger.Parse(txData.GasPrice));
                            }

       
                            if (!txData.Gas.HasValue || txData.Gas.Value <= 21000)
                            {
                                var estimate = await web3.Eth.Transactions.EstimateGas.SendRequestAsync(transactionInput);
                                transactionInput.Gas = new HexBigInteger(estimate.Value * 12 / 10);
                            }
                            else
                            {
                                transactionInput.Gas = new HexBigInteger(txData.Gas.Value);
                            }

                            _log?.Send($"Sending Tx to {txData.To}");

                            var txHash = await web3.Eth.TransactionManager.SendTransactionAsync(transactionInput);
                            _log?.Send($"Tx sent: {txHash}");
                            
                            
                            await WaitTx(step,web3, txHash);
/*
                            TransactionReceipt receipt = null;
                            int attempts = 0;
                            const int maxReceiptAttempts = 15;

                            while (attempts < maxReceiptAttempts)
                            {
                                try
                                {
                                    receipt = await web3.Eth.Transactions.GetTransactionReceipt
                                        .SendRequestAsync(txHash);
                                    if (receipt != null)
                                    {
                                        _log?.Send($"✅ SUCCSESS: {step.Description ?? step.Id}  {txHash}");
                                        break;
                                    }
                                }
                                catch (Exception receiptError)
                                {
                                    attempts++;
                                    _log?.Send(
                                        $"   ⚠️ Ошибка получения статуса (попытка {attempts}/{maxReceiptAttempts}): {receiptError.Message}");

                                    if (attempts >= maxReceiptAttempts)
                                    {
                                        _log?.Send(
                                            $"   ⚠️ Не удалось получить статус после {maxReceiptAttempts} попыток");
                                        receipt = new TransactionReceipt
                                            { Status = new HexBigInteger(1), TransactionHash = txHash };
                                        break;
                                    }

                                    await Task.Delay(5000);
                                }

                                attempts++;
                                await Task.Delay(5000);
                            }

                            if (receipt?.Status?.Value == 0)
                            {
                                throw new Exception("Транзакция отклонена блокчейном");
                            }
*/
                            // Уведомление API
                            try
                            {
                                await NotifyTransactionIndexedAsync(txHash, txData.ChainId);
                            }
                            catch
                            {
                            }

                            if (step.Id == "approve")
                            {
                                results.Add(new StepResult
                                {
                                    Step = step.Id,
                                    Status = "completed",
                                    TxHash = txHash
                                });
                            }
                            else
                            {
                                if (item.Check != null)
                                {
                                    var requestId = item.Check.Endpoint.Split("requestId=")[1];
                                    _log?.Send($"   🔍 Проверяем статус исполнения: {requestId}");

                                    var status = await GetExecutionStatusAsync(requestId);
                                    int attempts = 0;
                                    const int maxStatusAttempts = 30;

                                    while (status.Status != "success" && status.Status != "failure" &&
                                           status.Status != "refund" && attempts < maxStatusAttempts)
                                    {
                                        _log?.Send(
                                            $"   ⏳ Статус: {status.Status} (попытка {attempts + 1}/{maxStatusAttempts})");
                                        await Task.Delay(5000);
                                        status = await GetExecutionStatusAsync(requestId);
                                        attempts++;
                                    }

                                    _log?.Send($"   📊 Финальный статус: {status.Status}");

                                    results.Add(new StepResult
                                    {
                                        Step = step.Id,
                                        Status = status.Status,
                                        Details = status
                                    });
                                }
                                else
                                {
                                    results.Add(new StepResult
                                    {
                                        Step = step.Id,
                                        Status = "completed",
                                        TxHash = txHash
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var errorMessage = ex.Message;
                            _log?.Send($"   ❌ Ошибка шага {step.Id}: {errorMessage}");

                            results.Add(new StepResult
                            {
                                Step = step.Id,
                                Status = "failed",
                                Error = errorMessage
                            });
                        }
                    }
                    else if (step.Kind == "signature")
                    {
                        try
                        {
                            _log?.Send($"   ✍️ Выполняем подпись для шага: {step.Id}");

                            var signData = item.Sign;
                            var postData = item.Post;

                            if (signData == null || string.IsNullOrEmpty(signData.Message))
                            {
                                throw new Exception("Некорректные данные для подписи");
                            }

                            string signature;
                            if (signData.SignatureKind == "eip191")
                            {
                                var signer = new EthereumMessageSigner();
                                signature = signer.EncodeUTF8AndSign(signData.Message,
                                    new EthECKey(account.PrivateKey));
                            }
                            else
                            {
                                throw new Exception($"Неизвестный тип подписи: {signData.SignatureKind}");
                            }

                            if (postData != null && !string.IsNullOrEmpty(postData.Endpoint))
                            {
                                var postUrl = $"{_baseUrl}{postData.Endpoint}";
                                var postBody = new Dictionary<string, object>(postData.Body)
                                {
                                    ["signature"] = signature
                                };

                                var json = JsonConvert.SerializeObject(postBody);
                                var content = new StringContent(json, Encoding.UTF8, "application/json");

                                var response = await _httpClient.PostAsync(postUrl, content);
                                response.EnsureSuccessStatusCode();
                                _log?.Send($"   ✅ Подпись отправлена успешно");
                            }

                            results.Add(new StepResult
                            {
                                Step = step.Id,
                                Status = "completed",
                                Signature = signature
                            });
                        }
                        catch (Exception ex)
                        {
                            _log?.Send($"   ❌ Ошибка подписи: {ex.Message}");
                            results.Add(new StepResult
                            {
                                Step = step.Id,
                                Status = "failed",
                                Error = ex.Message
                            });
                        }
                    }
                }
            }

            return results;
        }

        public async Task<bool> WaitTx(TransactionStep step, Web3 web3, string txHash)
        {
            TransactionReceipt receipt = null;
            int attempts = 0;
            const int maxReceiptAttempts = 15;

            while (attempts < maxReceiptAttempts)
            {
                try
                {
                    receipt = await web3.Eth.Transactions.GetTransactionReceipt
                        .SendRequestAsync(txHash);
                    if (receipt != null)
                    {
                        _log?.Send($"✅ SUCCSESS: {step.Description ?? step.Id}  {txHash}");
                        break;
                    }
                }
                catch (Exception receiptError)
                {
                    attempts++;
                    _log?.Send(
                        $"   ⚠️ Ошибка получения статуса (попытка {attempts}/{maxReceiptAttempts}): {receiptError.Message}");

                    if (attempts >= maxReceiptAttempts)
                    {
                        _log?.Send(
                            $"   ⚠️ Не удалось получить статус после {maxReceiptAttempts} попыток");
                        receipt = new TransactionReceipt
                            { Status = new HexBigInteger(1), TransactionHash = txHash };
                        break;
                    }

                    await Task.Delay(5000);
                }

                attempts++;
                await Task.Delay(5000);
            }

            if (receipt?.Status?.Value == 0)
            {
                throw new Exception("Транзакция отклонена блокчейном");
            }

            return true;


        }


        #endregion

        #region Helper Methods

        private void ValidateQuoteResponse(QuoteResponse response)
        {
            if (response.Steps == null || response.Steps.Count == 0)
            {
                throw new Exception("Некорректный ответ: отсутствуют шаги");
            }

            if (response.Fees == null)
            {
                throw new Exception("Некорректный ответ: отсутствуют комиссии");
            }

            if (response.Details == null)
            {
                throw new Exception("Некорректный ответ: отсутствуют детали");
            }
        }

        private bool IsRetryableError(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                return httpEx.StatusCode >= System.Net.HttpStatusCode.InternalServerError;
            }

            return false;
        }

        private Exception HandleApiError(Exception error, string operation)
        {
            if (error is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var status = (int)httpEx.StatusCode.Value;
                var statusMessages = new Dictionary<int, string>
                {
                    { 400, "Некорректный запрос" },
                    { 401, "Неавторизованный доступ" },
                    { 403, "Доступ запрещен" },
                    { 404, "Ресурс не найден" },
                    { 429, "Слишком много запросов" },
                    { 500, "Внутренняя ошибка сервера" },
                    { 502, "Сервис временно недоступен" },
                    { 503, "Сервис перегружен" }
                };

                var message = status;//statusMessages.ContainsKey(status)
                    //? statusMessages[status]
                   // : $"HTTP ошибка {status}";

                return new Exception($"{message} step: {operation}", error);
            }

            return error;
        }

        #endregion
    }
}