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

namespace LiFiBridge
{
    #region Models



    public class TokenInfo
    {
        [JsonProperty("address")] public string Address { get; set; }

        [JsonProperty("symbol")] public string Symbol { get; set; }

        [JsonProperty("decimals")] public int Decimals { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("priceUSD")] public string PriceUSD { get; set; }
    }

    public class ActionInfo
    {
        [JsonProperty("fromChainId")] public int FromChainId { get; set; }

        [JsonProperty("toChainId")] public int ToChainId { get; set; }

        [JsonProperty("fromToken")] public TokenInfo FromToken { get; set; }

        [JsonProperty("toToken")] public TokenInfo ToToken { get; set; }

        [JsonProperty("fromAmount")] public string FromAmount { get; set; }

        [JsonProperty("toAmount")] public string ToAmount { get; set; }

        [JsonProperty("slippage")] public decimal Slippage { get; set; }
    }

    public class EstimateInfo
    {
        [JsonProperty("fromAmount")] public string FromAmount { get; set; }

        [JsonProperty("toAmount")] public string ToAmount { get; set; }

        [JsonProperty("toAmountMin")] public string ToAmountMin { get; set; }

        [JsonProperty("approvalAddress")] public string ApprovalAddress { get; set; }

        [JsonProperty("executionDuration")] public double ExecutionDuration { get; set; }

        [JsonProperty("feeCosts")] public List<FeeCost> FeeCosts { get; set; }

        [JsonProperty("gasCosts")] public List<GasCost> GasCosts { get; set; }
    }

    public class FeeCost
    {
        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("token")] public TokenInfo Token { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; }

        [JsonProperty("amountUSD")] public string AmountUSD { get; set; }

        [JsonProperty("percentage")] public string Percentage { get; set; }

        [JsonProperty("included")] public bool Included { get; set; }
    }

    public class GasCost
    {
        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("price")] public string Price { get; set; }

        [JsonProperty("estimate")] public string Estimate { get; set; }

        [JsonProperty("limit")] public string Limit { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; }

        [JsonProperty("amountUSD")] public string AmountUSD { get; set; }

        [JsonProperty("token")] public TokenInfo Token { get; set; }
    }

    public class TransactionRequest
    {
        [JsonProperty("from")] public string From { get; set; }

        [JsonProperty("to")] public string To { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("data")] public string Data { get; set; }

        [JsonProperty("value")] public string Value { get; set; }

        [JsonProperty("gasPrice")] public string GasPrice { get; set; }

        [JsonProperty("gasLimit")] public string GasLimit { get; set; }

        [JsonProperty("maxFeePerGas")] public string MaxFeePerGas { get; set; }

        [JsonProperty("maxPriorityFeePerGas")]
        public string MaxPriorityFeePerGas { get; set; }
    }

    public class QuoteRequest
    {
        [JsonProperty("fromChain")] public int FromChain { get; set; }

        [JsonProperty("toChain")] public int ToChain { get; set; }

        [JsonProperty("fromToken")] public string FromToken { get; set; }

        [JsonProperty("toToken")] public string ToToken { get; set; }

        [JsonProperty("fromAmount")] public string FromAmount { get; set; }

        [JsonProperty("fromAddress")] public string FromAddress { get; set; }

        [JsonProperty("toAddress")] public string ToAddress { get; set; }

        [JsonProperty("slippage")] public decimal? Slippage { get; set; }

        [JsonProperty("order")] public string Order { get; set; }

        [JsonProperty("allowBridges")] public List<string> AllowBridges { get; set; }

        [JsonProperty("denyBridges")] public List<string> DenyBridges { get; set; }

        [JsonProperty("allowExchanges")] public List<string> AllowExchanges { get; set; }

        [JsonProperty("denyExchanges")] public List<string> DenyExchanges { get; set; }

        [JsonProperty("integrator")] public string Integrator { get; set; }
        [JsonProperty("fee")] public string Fee { get; set; }
    }

    public class QuoteResponse
    {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("tool")] public string Tool { get; set; }

        [JsonProperty("toolDetails")] public Dictionary<string, object> ToolDetails { get; set; }

        [JsonProperty("action")] public ActionInfo Action { get; set; }

        [JsonProperty("estimate")] public EstimateInfo Estimate { get; set; }

        [JsonProperty("includedSteps")] public List<StepInfo> IncludedSteps { get; set; }

        [JsonProperty("transactionRequest")]
        public TransactionRequest TransactionRequest { get; set; }
    }

    public class StepInfo
    {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("tool")] public string Tool { get; set; }

        [JsonProperty("action")] public ActionInfo Action { get; set; }

        [JsonProperty("estimate")] public EstimateInfo Estimate { get; set; }
    }

    public class StatusRequest
    {
        [JsonProperty("bridge")] public string Bridge { get; set; }

        [JsonProperty("fromChain")] public int FromChain { get; set; }

        [JsonProperty("toChain")] public int ToChain { get; set; }

        [JsonProperty("txHash")] public string TxHash { get; set; }
    }

    public class StatusResponse
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("substatus")] public string Substatus { get; set; }

        [JsonProperty("substatusMessage")] public string SubstatusMessage { get; set; }

        [JsonProperty("fromChain")] public int FromChain { get; set; }

        [JsonProperty("toChain")] public int ToChain { get; set; }

        [JsonProperty("tool")] public string Tool { get; set; }

        [JsonProperty("sending")] public TransactionStatus Sending { get; set; }

        [JsonProperty("receiving")] public TransactionStatus Receiving { get; set; }

        [JsonProperty("lifiExplorerLink")] public string LifiExplorerLink { get; set; }
    }

    public class TransactionStatus
    {
        [JsonProperty("txHash")] public string TxHash { get; set; }

        [JsonProperty("txLink")] public string TxLink { get; set; }

        [JsonProperty("amount")] public string Amount { get; set; }

        [JsonProperty("token")] public TokenInfo Token { get; set; }

        [JsonProperty("chainId")] public int ChainId { get; set; }

        [JsonProperty("gasPrice")] public string GasPrice { get; set; }

        [JsonProperty("gasUsed")] public string GasUsed { get; set; }

        [JsonProperty("gasToken")] public TokenInfo GasToken { get; set; }

        [JsonProperty("timestamp")] public long Timestamp { get; set; }
    }

    public class ExecutionResult
    {
        public string Status { get; set; }
        public string TxHash { get; set; }
        public string Error { get; set; }
        public StatusResponse StatusDetails { get; set; }
    }

    #endregion

    public class LiFiBridgeClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://li.quest/v1";
        private readonly string _integrator = "z3nBank";
        private readonly decimal? _fee;
        private Logger _log;
        public LiFiBridgeClient( Logger log = null)
        {
            _fee = 0.005m;
            _log = log;

            var debugHandler = new HttpDebugHandler(_integrator)
            {
                InnerHandler = new HttpClientHandler()
            };

            _httpClient = new HttpClient(debugHandler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
        }
        
        #region API Methods
        public async Task<QuoteResponse> GetQuoteAsync(QuoteRequest request)
        {
            _log?.Send($"🔍 Запрашиваем quote от {request.FromChain} к {request.ToChain}");

            var queryParams = new Dictionary<string, string>
            {
                ["fromChain"] = request.FromChain.ToString(),
                ["toChain"] = request.ToChain.ToString(),
                ["fromToken"] = request.FromToken,
                ["toToken"] = request.ToToken,
                ["fromAmount"] = request.FromAmount,
                ["fromAddress"] = request.FromAddress
            };

            if (!string.IsNullOrEmpty(request.ToAddress))
            {
                queryParams["toAddress"] = request.ToAddress;
            }

            if (request.Slippage.HasValue)
            {
                queryParams["slippage"] = request.Slippage.Value.ToString();
            }

            if (!string.IsNullOrEmpty(request.Order))
            {
                queryParams["order"] = request.Order;
            }

            if (!string.IsNullOrEmpty(_integrator))
            {
                queryParams["integrator"] = _integrator;
            }
            
            if (_fee.HasValue)
            {
                queryParams["fee"] = _fee.Value.ToString("0.####"); 
            }
            
            if (!string.IsNullOrEmpty(request.Fee))
            {
                queryParams["fee"] = request.Fee;
            }
         

            if (request.AllowBridges?.Count > 0)
            {
                queryParams["allowBridges"] = string.Join(",", request.AllowBridges);
            }

            if (request.DenyBridges?.Count > 0)
            {
                queryParams["denyBridges"] = string.Join(",", request.DenyBridges);
            }

            if (request.AllowExchanges?.Count > 0)
            {
                queryParams["allowExchanges"] = string.Join(",", request.AllowExchanges);
            }

            if (request.DenyExchanges?.Count > 0)
            {
                queryParams["denyExchanges"] = string.Join(",", request.DenyExchanges);
            }

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var url = $"{_baseUrl}/quote?{queryString}";

            var attempts = 0;
            const int maxAttempts = 3;

            while (attempts < maxAttempts)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var quote = JsonConvert.DeserializeObject<QuoteResponse>(jsonResponse);

                    ValidateQuoteResponse(quote);

                    _log?.Send($"✅ Quote получен: {quote.Tool}");
                    _log?.Send($"   От: {quote.Estimate.FromAmount} {quote.Action.FromToken.Symbol}");
                    _log?.Send($"   К: {quote.Estimate.ToAmount} {quote.Action.ToToken.Symbol}");

                    return quote;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _log?.Send($"❌ Попытка {attempts}/{maxAttempts}: {ex.Message}");

                    if (attempts >= maxAttempts || !IsRetryableError(ex))
                    {
                        throw HandleApiError(ex, "GetQuote");
                    }

                    await Task.Delay(2000 * attempts);
                }
            }

            throw new Exception("Не удалось получить quote после нескольких попыток");
        }
        public async Task<StatusResponse> GetStatusAsync(StatusRequest request)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["bridge"] = request.Bridge,
                ["fromChain"] = request.FromChain.ToString(),
                ["toChain"] = request.ToChain.ToString(),
                ["txHash"] = request.TxHash
            };

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var url = $"{_baseUrl}/status?{queryString}";

            var attempts = 0;
            const int maxAttempts = 3;

            while (attempts < maxAttempts)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var status = JsonConvert.DeserializeObject<StatusResponse>(jsonResponse);

                    return status;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _log?.Send($"❌ Ошибка получения статуса (попытка {attempts}/{maxAttempts}): {ex.Message}");

                    if (attempts >= maxAttempts || !IsRetryableError(ex))
                    {
                        throw HandleApiError(ex, "GetStatus");
                    }

                    await Task.Delay(2000 * attempts);
                }
            }

            throw new Exception("Не удалось получить статус после нескольких попыток");
        }

        #endregion

        #region Execution Methods

        public async Task<ExecutionResult> ExecuteTransferAsync(Account account, QuoteResponse quote, bool waitForCompletion = true, Web3 web3 = null)
        {
            _log?.Send($"🚀 Начинаем выполнение трансфера");
            _log?.Send($"   Маршрут: {quote.Action.FromToken.Symbol} ({quote.Action.FromChainId}) → {quote.Action.ToToken.Symbol} ({quote.Action.ToChainId})");
            _log?.Send($"   Инструмент: {quote.Tool}");

            try
            {
                // Проверяем и устанавливаем allowance если нужно
                var fromTokenAddress = quote.Action.FromToken.Address;
                var approvalAddress = quote.Estimate.ApprovalAddress;
                var fromAmount = quote.Action.FromAmount;

                if (!string.IsNullOrEmpty(approvalAddress) &&
                    fromTokenAddress.ToLower() != "0x0000000000000000000000000000000000000000")
                {
                    _log?.Send($"📝 Проверяем allowance для токена {quote.Action.FromToken.Symbol}");
                    await CheckAndSetAllowanceAsync(account, fromTokenAddress, approvalAddress, fromAmount,
                        quote.Action.FromChainId, web3);
                }

                // Отправляем транзакцию
                _log?.Send($"💸 Отправляем транзакцию...");
                var txHash = await SendTxAsync(account, quote.TransactionRequest,web3);
                _log?.Send($"✅ Транзакция отправлена: {txHash}");

                // Ждем подтверждения транзакции
                _log?.Send($"⏳ Ожидаем подтверждения транзакции...");
                var receipt = await WaitForTxReceipt(txHash, web3);

                if (receipt?.Status?.Value == 0)
                {
                    throw new Exception("Транзакция отклонена блокчейном");
                }

                _log?.Send($"✅ Транзакция подтверждена в блоке {receipt.BlockNumber}");

                // Если это кросс-чейн трансфер, ждем завершения
                if (waitForCompletion && quote.Action.FromChainId != quote.Action.ToChainId)
                {
                    _log?.Send($"🔄 Отслеживаем кросс-чейн трансфер...");
                    var status = await WaitForTransferCompletionAsync(
                        quote.Tool,
                        quote.Action.FromChainId,
                        quote.Action.ToChainId,
                        txHash);

                    return new ExecutionResult
                    {
                        Status = status.Status,
                        TxHash = txHash,
                        StatusDetails = status
                    };
                }

                return new ExecutionResult
                {
                    Status = "DONE",
                    TxHash = txHash
                };
            }
            catch (Exception ex)
            {
                //_log?.Send($"❌ Ошибка выполнения: {ex.Message}");
                return new ExecutionResult
                {
                    Status = "FAILED",
                    Error = ex.Message
                };
            }
        }
        private async Task CheckAndSetAllowanceAsync(Account account, string tokenAddress, string approvalAddress, string amount, int chainId, Web3 web3)
        {
            //var web3 = await GetWorkingWeb3(chainId, account);

            var erc20Abi = @"[
                {
                    ""name"": ""approve"",
                    ""inputs"": [
                        {""internalType"": ""address"", ""name"": ""spender"", ""type"": ""address""},
                        {""internalType"": ""uint256"", ""name"": ""amount"", ""type"": ""uint256""}
                    ],
                    ""outputs"": [{""internalType"": ""bool"", ""name"": """", ""type"": ""bool""}],
                    ""stateMutability"": ""nonpayable"",
                    ""type"": ""function""
                },
                {
                    ""name"": ""allowance"",
                    ""inputs"": [
                        {""internalType"": ""address"", ""name"": ""owner"", ""type"": ""address""},
                        {""internalType"": ""address"", ""name"": ""spender"", ""type"": ""address""}
                    ],
                    ""outputs"": [{""internalType"": ""uint256"", ""name"": """", ""type"": ""uint256""}],
                    ""stateMutability"": ""view"",
                    ""type"": ""function""
                }
            ]";

            var contract = web3.Eth.GetContract(erc20Abi, tokenAddress);
            var allowanceFunction = contract.GetFunction("allowance");
            var approveFunction = contract.GetFunction("approve");

            var currentAllowance = await allowanceFunction.CallAsync<BigInteger>(account.Address, approvalAddress);
            var requiredAmount = BigInteger.Parse(amount);

            if (currentAllowance < requiredAmount)
            {
                _log?.Send($"   ⚠️ Недостаточный allowance. Устанавливаем approve...");
                
                var approveData = approveFunction.GetData(approvalAddress, requiredAmount);
                
                var approveTxRequest = new TransactionRequest
                {
                    From = account.Address,
                    To = tokenAddress,
                    ChainId = chainId,
                    Data = approveData,
                    Value = "0",
                    GasLimit = null, 
                    GasPrice = null 
                };
                var approveTxHash = await SendTxAsync(account, approveTxRequest, web3);
                

                var approveReceipt = await WaitForTxReceipt(approveTxHash, web3);

                if (approveReceipt?.Status?.Value == 0)
                {
                    throw new Exception("Approve транзакция отклонена");
                }

                _log?.Send($"   ✅ Approve успешно установлен");
                await Task.Delay(3000);
            }
            else
            {
                _log?.Send($"   ✅ Allowance достаточный");
            }
        }
        private async Task<string> SendTxAsync(Account account, TransactionRequest txRequest, Web3 web3)
        {
            var nonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(account.Address, BlockParameter.CreatePending());
            var txInput = new TransactionInput
            {
                From = account.Address,
                To = txRequest.To,
                Data = txRequest.Data,
                Value = new HexBigInteger(ParseBigIntegerSafe(txRequest.Value ?? "0")),
                ChainId = new HexBigInteger(txRequest.ChainId),
                Nonce = nonce,
            };


            var networkGasPrice = await web3.Eth.GasPrice.SendRequestAsync();
            var boostedGasPrice = new HexBigInteger((networkGasPrice.Value * 120) / 100);
            txInput.GasPrice = boostedGasPrice;
            var estimate = await web3.Eth.Transactions.EstimateGas.SendRequestAsync(txInput);
            txInput.Gas = new HexBigInteger((estimate.Value * 110) / 100);

            _log?.Send($"   ⛽ Газ: Price={txInput.GasPrice.Value}, Limit={txInput.Gas.Value}");

            var txHash = await web3.Eth.TransactionManager.SendTransactionAsync(txInput);
            return txHash;
        }
        private BigInteger ParseBigIntegerSafe(string value)
        {
           
            try
            {
                if (string.IsNullOrEmpty(value))
                    return BigInteger.Zero;
                value = value.Trim();

                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return new Nethereum.Hex.HexTypes.HexBigInteger(value).Value;
                }

                // Парсим как decimal
                if (BigInteger.TryParse(value, out var result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                //_log?.Send($"{ex.Message} {value}");
                throw new Exception($"{ex.Message} {value}");
            }

            throw new Exception($"Cannot parse BigInteger from value: {value}");
        }

        private async Task<TransactionReceipt> WaitForTxReceipt(string txHash,  Web3 web3, int maxAttempts = 60)
        {
            //var web3 = await GetWorkingWeb3(chainId, account);
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

            throw new Exception($"Не удалось получить receipt для транзакции {txHash}");
        }

        private async Task<StatusResponse> WaitForTransferCompletionAsync(string bridge, int fromChain, int toChain, string txHash, int maxAttempts = 120)
        {
            var attempts = 0;
            StatusResponse status = null;

            while (attempts < maxAttempts)
            {
                try
                {
                    status = await GetStatusAsync(new StatusRequest
                    {
                        Bridge = bridge,
                        FromChain = fromChain,
                        ToChain = toChain,
                        TxHash = txHash
                    });

                    _log?.Send($"   📊 Статус: {status.Status} - {status.Substatus} (попытка {attempts + 1}/{maxAttempts})");

                    if (status.Status == "DONE" || status.Status == "FAILED")
                    {
                        _log?.Send($"   🏁 Финальный статус: {status.Status}");
                        return status;
                    }
                }
                catch (Exception ex)
                {
                    _log?.Send($"   ⚠️ Ошибка проверки статуса: {ex.Message}");
                }

                attempts++;
                await Task.Delay(5000);
            }

            if (status != null)
            {
                _log?.Send($"   ⏱️ Превышено время ожидания. Последний статус: {status.Status}");
                return status;
            }

            throw new Exception("Не удалось получить статус трансфера");
        }

        #endregion

        #region Helper Methods

        private void ValidateQuoteResponse(QuoteResponse response)
        {
            if (response == null)
            {
                throw new Exception("Некорректный ответ: quote пуст");
            }

            if (response.TransactionRequest == null)
            {
                throw new Exception("Некорректный ответ: отсутствует transactionRequest");
            }

            if (response.Action == null)
            {
                throw new Exception("Некорректный ответ: отсутствует action");
            }

            if (response.Estimate == null)
            {
                throw new Exception("Некорректный ответ: отсутствует estimate");
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
                    { 404, "Маршрут не найден" },
                    { 429, "Слишком много запросов" },
                    { 500, "Внутренняя ошибка сервера" },
                    { 502, "Сервис временно недоступен" },
                    { 503, "Сервис перегружен" }
                };

                var message = statusMessages.ContainsKey(status)
                    ? statusMessages[status]
                    : $"HTTP ошибка {status}";

                return new Exception($"{message} при операции: {operation}", error);
            }

            return new Exception($"Ошибка при операции {operation}: {error.Message}", error);
        }

        #endregion
    }
}