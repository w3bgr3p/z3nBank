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
        [JsonProperty("details")] public object Details { get; set; }  // <--- было Dictionary<string, object>


        // [JsonProperty("details")] public Dictionary<string, object> Details { get; set; }
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


        public RelayBridgeClient(string apiKey = null, bool isTestnet = false, Logger log = null)
        {
            _baseUrl = isTestnet ? "https://api.testnets.relay.link" : "https://api.relay.link";
            _apiKey = apiKey;
            _log = log;

            var debugHandler = new HttpDebugHandler("z3nBank") 
            { 
                InnerHandler = new HttpClientHandler()
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


        }


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

        #region Additional API Methods

        public async Task<List<Dictionary<string, object>>> GetChainsAsync()
        {
            string content = "no content";
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/chains");
                content = await response.Content.ReadAsStringAsync(); // Сначала читаем ответ

                if (!response.IsSuccessStatusCode)
                {
                    // Бросаем исключение со статусом, чтобы HandleApiError его поймал
                    throw new HttpRequestException(content, null, response.StatusCode);
                }
        
                return JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(content);
            }
            catch (Exception ex)
            {
                // Передаем сырой ответ API (content) в обработчик
                throw HandleApiError(ex, "GetChains", content);
            }
        }

        public async Task<Dictionary<string, object>> GetTokenPriceAsync(string address, int chainId)
        {
            string content = "no content";
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/currencies/token/price?address={address}&chainId={chainId}");
                content = await response.Content.ReadAsStringAsync(); // Сначала читаем ответ
                response.EnsureSuccessStatusCode();
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
            }
            catch (Exception ex)
            {
                throw HandleApiError(ex, "getTokenPrice", content);
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

        #region API Methods

        public async Task<QuoteResponse> GetQuoteAsync(QuoteRequest request)
        {
            var maxRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(2);

            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                string content = "no content";
                try
                {
                    if (request.AppFees == null)
                    {
                        request.AppFees = GenerateAppFees();
                    }

                    if (string.IsNullOrEmpty(request.Source))
                    {
                        request.Source = _source;
                    }

                    var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    });

                    var quoteBody = new StringContent(json, Encoding.UTF8, "application/json");
                    

                    var response = await _httpClient.PostAsync($"{_baseUrl}/quote", quoteBody);

                    content = await response.Content.ReadAsStringAsync();
                    
                    response.EnsureSuccessStatusCode();

                    _log?.Send($"<== {response.StatusCode}: Quote Acquired");
                    
                    var quoteResponse = JsonConvert.DeserializeObject<QuoteResponse>(content);

                    ValidateQuoteResponse(quoteResponse);

                    return quoteResponse;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries - 1 || !IsRetryableError(ex))
                    {
                        _log?.Send($"❌ Ошибка получения quote: {ex.Message}", "ERROR");
                        throw HandleApiError(ex, "GetQuote",content);
                    }
                    _log?.Send($"⚠️ Ошибка получения quote (попытка {attempt + 1}/{maxRetries}): {ex.Message}");
                    await Task.Delay(retryDelay);
                }
            }

            throw new Exception("Не удалось получить quote после нескольких попыток");
        }

        private async Task<ExecutionStatus> GetExecutionStatusAsync(string requestId)
        {
            string content = "no content";
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/intents/status/v2?requestId={requestId}");
                content = await response.Content.ReadAsStringAsync();
                response.EnsureSuccessStatusCode();

                return JsonConvert.DeserializeObject<ExecutionStatus>(content);
            }
            catch (Exception ex)
            {
                throw HandleApiError(ex, "GetExecutionStatus",content);
            }
        }

        #endregion

        #region Execution Methods

        public async Task<List<StepResult>> ExecuteQuoteAsync(QuoteResponse quote, Account account, Web3 web3)
        {
            if (web3 == null) throw new ArgumentNullException(nameof(web3), "Web3 instance is required for execution");
            var results = new List<StepResult>();

            _log?.Send($"🚀 Начинаем выполнение {quote.Steps.Count} шагов");

            foreach (var step in quote.Steps)
            {
                _log?.Send($"\n📋 Шаг: {step.Id} ({step.Kind})");
                if (!string.IsNullOrEmpty(step.Description))
                {
                    _log?.Send($"   📝 {step.Description}");
                }

                foreach (var item in step.Items)
                {
                    if (step.Kind == "transaction")
                    {
                        try
                        {
                            var txData = item.Data;
                            if (txData == null)
                            {
                                throw new Exception("Отсутствуют данные транзакции");
                            }
                            _log?.Send($"   >> Sending Tx by {Enum.GetName(typeof(RpcUrl), txData.ChainId)} (chainId: {txData.ChainId})");
                            
                            var txHash = await SendTxAsync(account, txData, web3);
                            _log?.Send($"   => TX sent: {txHash}");

                            var receipt = await WaitForTxReceipt(txHash, web3);

                            if (receipt?.Status?.Value == 0)
                            {
                                throw new Exception("Транзакция отклонена блокчейном");
                            }

                            _log?.Send($"   ✅ SUCCESS: {step.Description ?? step.Id} {txHash}");

                            if (item.Check != null && !string.IsNullOrEmpty(item.Check.Endpoint))
                            {
                                _log?.Send($"   🔍 Проверяем статус через API...");
                                var requestId = item.Check.Endpoint.Split("requestId=")[1];
               

                                if (!string.IsNullOrEmpty(requestId))
                                {
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
                                        TxHash = txHash,
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

                            // Прерываем выполнение при ошибке критичной транзакции
                            throw;
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

                            _log?.Send($"   🔑 Подпись создана");

                            // Отправляем подпись через POST, если требуется
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

                            // Подпись обычно критична, прерываем
                            throw;
                        }
                    }
                }
            }

            _log?.Send($"\n🎉 Все шаги выполнены успешно!");
            return results;
        }

        /// <summary>
        /// НОВЫЙ МЕТОД: Отправка транзакции с самостоятельным управлением газом (как в LiFi)
        /// Игнорируем параметры газа от API и рассчитываем их сами
        /// </summary>
        private async Task<string> SendTxAsync(Account account, TransactionData txData, Web3 web3)
        {
            // Получаем nonce
            
            var nonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                account.Address, 
                BlockParameter.CreatePending());

            // Создаем транзакцию
            var txInput = new TransactionInput
            {
                From = account.Address,
                To = txData.To,
                Data = txData.Data,
                Value = new HexBigInteger(ParseBigIntegerSafe(txData.Value ?? "0")),
                ChainId = new HexBigInteger(txData.ChainId),
                Nonce = nonce,
            };
            _log?.Send($"   ⛽ Calculating gas...");
            // САМОСТОЯТЕЛЬНЫЙ РАСЧЕТ ГАЗА (не доверяем API)
            // Получаем текущую цену газа из сети
            var networkGasPrice = await web3.Eth.GasPrice.SendRequestAsync();
            
            // Добавляем буфер 20% к цене газа для приоритета
            var boostedGasPrice = new HexBigInteger((networkGasPrice.Value * 120) / 100);
            txInput.GasPrice = boostedGasPrice;

            // Оцениваем лимит газа
            var estimatedGas = await web3.Eth.Transactions.EstimateGas.SendRequestAsync(txInput);
            
            // Добавляем буфер 10% к лимиту газа для безопасности
            txInput.Gas = new HexBigInteger((estimatedGas.Value * 110) / 100);

            _log?.Send($"   ⛽ Price={txInput.GasPrice.Value}, Limit={txInput.Gas.Value}");

            // Отправляем транзакцию
            var txHash = await web3.Eth.TransactionManager.SendTransactionAsync(txInput);
            
            // Уведомляем API об отправленной транзакции (для индексации)
            try
            {
                await NotifyTransactionIndexedAsync(txHash, txData.ChainId);
            }
            catch
            {
                // Игнорируем ошибки уведомления - это не критично
            }
            
            return txHash;
        }

        /// <summary>
        /// Парсинг BigInteger из различных форматов (hex/decimal)
        /// </summary>
        private BigInteger ParseBigIntegerSafe(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                    return BigInteger.Zero;
                    
                value = value.Trim();

                // Hex формат (0x...)
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return new HexBigInteger(value).Value;
                }

                // Decimal формат
                if (BigInteger.TryParse(value, out var result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось распарсить BigInteger из значения: {value}. Ошибка: {ex.Message}");
            }

            throw new Exception($"Не удалось распарсить BigInteger из значения: {value}");
        }

        /// <summary>
        /// Ожидание подтверждения транзакции в блокчейне
        /// </summary>
        private async Task<TransactionReceipt> WaitForTxReceipt(
            string txHash, 
            Web3 web3, 
            int maxAttempts = 60)
        {
            var attempts = 0;

            while (attempts < maxAttempts)
            {
                try
                {
                    var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
                    if (receipt != null)
                    {
                        return receipt;
                    }
                }
                catch (Exception ex)
                {
                    _log?.Send($"   ⚠️ Ошибка получения receipt (попытка {attempts + 1}/{maxAttempts}): {ex.Message}");
                }

                attempts++;
                await Task.Delay(5000);
            }

            throw new Exception($"Не удалось получить receipt для транзакции {txHash} после {maxAttempts} попыток");
        }

        /// <summary>
        /// Извлечение requestId из URL для проверки статуса
        /// </summary>
        private string ExtractRequestIdFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments;
                return segments.Length > 0 ? segments[^1].TrimEnd('/') : null;
            }
            catch
            {
                return null;
            }
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
                return httpEx.StatusCode >= System.Net.HttpStatusCode.InternalServerError ||
                       httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
            }

            return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase);
        }

        private Exception HandleApiError(Exception error, string operation, string body)
        {
            if (error is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var status = (int)httpEx.StatusCode.Value;
                return new Exception($"{error.Message} {operation} {body}");
            }
            return new Exception($"{error.Message} {operation} {body}", error);
        }

        #endregion
    }
}