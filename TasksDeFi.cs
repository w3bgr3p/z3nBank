using System.Numerics;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using RelayBridge;
using LiFiBridge;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using z3n;

namespace z3nSafe;

/// <summary>
/// Тип bridge клиента
/// </summary>
public enum Protocol
{
    Relay,
    LiFi
}

/// <summary>
/// Интерфейс для унифицированной работы с разными bridge клиентами
/// </summary>
public interface IBridgeClient
{
    Task<object> GetQuoteAsync(BridgeQuoteRequest request);
    Task<BridgeExecutionResult> ExecuteAsync(Account account, object quote, Web3 web3 = null);
}

/// <summary>
/// Унифицированный запрос quote для разных клиентов
/// </summary>
public class BridgeQuoteRequest
{
    public string WalletAddress { get; set; }
    public int FromChainId { get; set; }
    public int ToChainId { get; set; }
    public string FromTokenAddress { get; set; }
    public string ToTokenAddress { get; set; }
    public string Amount { get; set; }
    public decimal Slippage { get; set; }
    public string Integrator { get; set; }
    public string Fee { get; set; }
}

/// <summary>
/// Унифицированный результат выполнения
/// </summary>
public class BridgeExecutionResult
{
    public bool Success { get; set; }
    public string Status { get; set; }
    public string TxHash { get; set; }
    public string Error { get; set; }
    public string Details { get; set; }
}




/// <summary>
/// Адаптер для Relay клиента
/// </summary>
public class RelayBridgeAdapter : IBridgeClient
{
    private readonly RelayBridgeClient _client;
    private readonly Logger _log;

    public RelayBridgeAdapter(Logger log = null)
    {
        _client = new RelayBridgeClient(apiKey: null, isTestnet: false, log: log);
        _log = log;
    }



    public async Task<object> GetQuoteAsync(BridgeQuoteRequest request)
    {
        var quoteRequest = new RelayBridge.QuoteRequest
        {
            User = request.WalletAddress,
            Recipient = request.WalletAddress,
            OriginChainId = request.FromChainId,
            DestinationChainId = request.ToChainId,
            OriginCurrency = request.FromTokenAddress,
            DestinationCurrency = request.ToTokenAddress,
            Amount = request.Amount,
            SlippageTolerance = ((int)(request.Slippage * 100)).ToString(),
            TradeType = "EXACT_INPUT"
        };

        return await _client.GetQuoteAsync(quoteRequest);
    }

    public async Task<BridgeExecutionResult> ExecuteAsync(Account account, object quote, Web3 web3)
    {
        
        
        var relayQuote = quote as RelayBridge.QuoteResponse;
        if (relayQuote == null)
            throw new ArgumentException("Invalid quote type for Relay");

        try
        {
            var results = await _client.ExecuteQuoteAsync(relayQuote, account, web3);
            
            var stepsSummary = string.Join(" | ", results.Select(r => $"{r.Step}:{r.Status}"));
            
            bool isOk = results.All(r => 
                r.Status == "completed" || 
                r.Status == "success");

            var allTxHashes = results
                .Where(r => !string.IsNullOrEmpty(r.TxHash))
                .Select(r => r.TxHash)
                .ToList();

            var txHashSummary = allTxHashes.Count > 0 
                ? string.Join(", ", allTxHashes) 
                : null;

            return new BridgeExecutionResult
            {
                Success = isOk,
                Status = isOk ? "completed" : "failed",
                TxHash = txHashSummary, 
                Details = stepsSummary
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not ReceiptUnavailableException)
        {
            _log?.Send($"❌ Ошибка выполнения Relay bridge: {ex.Message}");
            
            return new BridgeExecutionResult
            {
                Success = false,
                Status = "failed",
                TxHash = null,
                Error = SwapExecution.ErrorDetails(ex),
                Details = $"Error: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Адаптер для LiFi клиента
/// </summary>
public class LiFiBridgeAdapter : IBridgeClient
{
    private readonly LiFiBridgeClient _client;
    private readonly Logger _log;

    public LiFiBridgeAdapter(string integrator = null, decimal? fee = null, Logger log = null)
    {
        _client = new LiFiBridgeClient(log: log);
        _log = log;
    }

    public async Task<object> GetQuoteAsync(BridgeQuoteRequest request)
    {
        var quoteRequest = new LiFiBridge.QuoteRequest
        {
            FromChain = request.FromChainId,
            ToChain = request.ToChainId,
            FromToken = request.FromTokenAddress,
            ToToken = request.ToTokenAddress,
            FromAmount = request.Amount,
            FromAddress = request.WalletAddress,
            Slippage = request.Slippage,
            Order = "RECOMMENDED",
            Integrator = request.Integrator,
            Fee = request.Fee
        };

        return await _client.GetQuoteAsync(quoteRequest);
    }

    public async Task<BridgeExecutionResult> ExecuteAsync(Account account, object quote, Web3 web3 = null)
    {
        var lifiQuote = quote as LiFiBridge.QuoteResponse;
        if (lifiQuote == null)
            throw new ArgumentException("Invalid quote type for LiFi");

        bool isCrossChain = lifiQuote.Action.FromChainId != lifiQuote.Action.ToChainId;
        var result = await _client.ExecuteTransferAsync(account, lifiQuote, waitForCompletion: isCrossChain, web3: web3);

        return new BridgeExecutionResult
        {
            Success = result.Status == "DONE" || result.Status == "success",
            Status = result.Status,
            TxHash = result.TxHash,
            Error = result.Error,
            Details = result.StatusDetails != null 
                ? $"Status: {result.StatusDetails.Status}, Substatus: {result.StatusDetails.Substatus}" 
                : null
        };
    }
}

/// <summary>
/// Фабрика для создания bridge клиентов
/// </summary>
public static class BridgeClientFactory
{
    public static IBridgeClient Create(Protocol type, Logger log = null, string integrator = null, decimal? fee = null)
    {
        return type switch
        {
            Protocol.Relay => new RelayBridgeAdapter(log),
            Protocol.LiFi => new LiFiBridgeAdapter(integrator, fee, log),
            _ => throw new ArgumentException($"Unknown bridge client type: {type}")
        };
    }
}

/// <summary>
/// Унифицированный класс для работы с разными bridge клиентами
/// </summary>
public class DeFi
{
    

    /// <summary>
    /// Обменивает все токены на нативную валюту в каждой сети
    /// </summary>
    public sealed record SwapResult(int Succeeded, int Failed, string? Error = null)
    {
        public string? RefreshError { get; init; }
        public IReadOnlyList<SwapSkip> SkipReasons { get; init; } = [];
    }
    public sealed record SwapSkip(string Chain, string Symbol, string Address, string Reason);

    public static async Task<SwapResult> SwapAllTokensNative(Db db, int id, decimal minValue, string pin,
        bool excludeStables = false, Protocol clientType = Protocol.LiFi, int delayMs = 108,
         string chains = null, Logger log = null, string integrator = null, decimal? fee = null,
         IReadOnlySet<string>? tokenTargets = null, string? expectedWallet = null,
         IReadOnlyList<TokenSelection.Target>? confirmedTargets = null)
    {
        int successCount = 0;
        int failCount = 0;
        var refreshErrors = new List<string>();
        var skips = new List<SwapSkip>();
        void Skip(string chain, Jumper.TokenInfo token, string reason)
        {
            skips.Add(new(chain, token.Symbol, token.Address, reason));
            log?.Send($"Skip | account #{id} | {chain} | {token.Symbol} | {token.Address} | {reason}", "WARNING");
        }
        
        try
        {
            SwapExecution.Check();
            var key = await GetKey(db, id, pin);
            if (string.IsNullOrEmpty(key))
            {
                log?.Send("❌ Error: Private key not found", "ERROR");
                return new SwapResult(0, 0, "Private key not found");
            }

            using var jumper = new Jumper(log);
            var chainNames = confirmedTargets == null ? await SwapExecution.Read(jumper.GetChainMapping()) :
                confirmedTargets.GroupBy(t => t.ChainId).ToDictionary(g => g.Key, g => g.First().Chain);
            chainNames = FilterChains(chainNames, filter: chains);
            
            var account = new Account(key);
            if (expectedWallet != null && !account.Address.Equals(expectedWallet, StringComparison.OrdinalIgnoreCase))
                return new SwapResult(0, 0, "Private key does not match the confirmed wallet");
            var bal = confirmedTargets == null ? await SwapExecution.Read(jumper.GetBalances(account.Address)) :
                new Jumper.JumperResponse { WalletAddress = account.Address, Balances =
                    TokenSelection.ExecutionTokens(confirmedTargets) };
            
            if (bal?.Balances == null || !bal.Balances.Any())
            {
                log?.Send($"👛 {account.Address} | ⚠️ No balances found", "WARNING");
                return new SwapResult(0, 0, "No live balances found");
            }

            log?.Send($"Checking {account.Address} | Client: {clientType} | Chains: {bal.Balances.Count}/{string.Join(", ", chainNames.Values)}");

            var bridgeClient = BridgeClientFactory.Create(clientType, log, integrator, fee);

            foreach (var chain in bal.Balances)
            {
                SwapExecution.Check();
                if (!int.TryParse(chain.Key, out int chainIdInt)) continue;
                if (!chainNames.ContainsKey(chainIdInt)) continue;
                
                string chainName = chainNames.GetValueOrDefault(chainIdInt, $"ID:{chain.Key}");
                
                var tokensToSwap = chain.Value.Where(t => 
                    (confirmedTargets != null || t.ValueUSD > minValue) &&
                    !TokenSelection.IsNative(t.Address) &&
                    (tokenTargets == null || tokenTargets.Contains(TokenSelection.Key(chainIdInt, t.Address)))
                ).ToList();

                if (!tokensToSwap.Any()) continue;

                var rpcUrl = Rpc.Get(chainIdInt);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log?.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    failCount += tokensToSwap.Count;
                    continue;
                }

                var web3 = CreateWeb3(key, rpcUrl, clientType);
                var actualChain = await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync());
                if (actualChain.Value != chainIdInt) throw new InvalidDataException($"RPC chain mismatch for {chainName}: expected {chainIdInt}, received {actualChain.Value}");
                

                foreach (var token in tokensToSwap)
                {
                    SwapExecution.Check();
                    
                    try
                    {
                        if (token.IsStable && excludeStables) { Skip(chainName, token, "Stablecoins are excluded"); continue; }

                        var tokenService = web3.Eth.GetContractHandler(token.Address);
                        BigInteger actualBalanceRaw;
                        if (token.Address == "0x0000000000000000000000000000000000000000" || 
                            token.Address.ToLower() == "0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")
                        {
                            var balance = await web3.Eth.GetBalance.SendRequestAsync(account.Address);
                            actualBalanceRaw = balance.Value;
                        }
                        else
                        {
                            actualBalanceRaw = await SwapExecution.Read(web3.Eth.GetContractQueryHandler<BalanceOfFunction>()
                                .QueryAsync<BigInteger>(token.Address, new BalanceOfFunction { Owner = account.Address }));
                        }
                        
                        token.Amount = actualBalanceRaw.ToString();
                        string opInfo = $"[{chainName}] {token.Symbol} ({token.ValueUSD:F2} USD)";
                        if (actualBalanceRaw <= 0)
                        {
                            Skip(chainName, token, "On-chain balance is zero; the displayed snapshot was outdated");
                            try
                            {
                                await TreasuryRpcBalances.RefreshKnown(db, id, chainName, chainIdInt, account.Address, web3, chain.Value);
                                log?.Send($"Zero balance verified and saved | #{id} | {chainName} | {token.Symbol}", "INFO");
                            }
                            catch (Exception ex) { refreshErrors.Add(ex.Message); log?.Send($"Zero balance snapshot refresh failed | #{id} | {chainName} | {ex.Message}", "ERROR"); }
                            continue;
                        }
                        if (confirmedTargets != null && BalanceMath.GetValueUsd(actualBalanceRaw.ToString(), token.Decimals, token.PriceUSD) <= minValue)
                        {
                            Skip(chainName, token, $"Live RPC balance value is at or below Min. USD {minValue}");
                            continue;
                        }
                        
                        await SwapExecution.Delay(delayMs);

                        var quoteRequest = new BridgeQuoteRequest
                        {
                            WalletAddress = account.Address,
                            FromChainId = chainIdInt,
                            ToChainId = chainIdInt,
                            FromTokenAddress = token.Address,
                            ToTokenAddress = "0x0000000000000000000000000000000000000000",
                            Amount = actualBalanceRaw.ToString(),
                            Slippage = 0.03m,
                            Integrator = integrator,
                            Fee = fee?.ToString()
                        };
                        
                        var quote = await SwapExecution.Read(bridgeClient.GetQuoteAsync(quoteRequest));
                        var costs = await SwapExecution.Read(SwapCostGuard.CheckAsync(quote, web3, account.Address));
                        if (!costs.Allowed)
                        {
                            Skip(chainName, token, costs.Reason);
                            continue;
                        }
                        log?.Send($"{opInfo} | {costs.Reason}", "INFO");
                        SwapExecution.Check();
                        var result = await bridgeClient.ExecuteAsync(account, quote, web3);

                        if (result.Success)
                        {
                            successCount++;
                            log?.Send($"✅ {opInfo} | TX: {result.TxHash} | {result.Details}", "SUCCESS");
                            try
                            {
                                var updatedNative = await TreasuryRpcBalances.RefreshAfterSwap(db, id, chainName, chainIdInt,
                                    account.Address, web3, chain.Value, quote, result.TxHash ?? "");
                                log?.Send($"Balance refreshed from RPC | account #{id} | {chainName} | {updatedNative.Symbol}: {BalanceMath.GetValueUsd(updatedNative.Amount, updatedNative.Decimals, "1")} | price USD: {updatedNative.PriceUSD} | estimated USD: {updatedNative.ValueUSD} | native and token amounts saved", "SUCCESS");
                            }
                            catch (Exception refreshException)
                            {
                                var message = $"{chainName}: {SwapExecution.ErrorDetails(refreshException)}";
                                refreshErrors.Add(message);
                                log?.Send($"Swap confirmed, but RPC balance refresh failed | account #{id} | {message} | previous snapshot kept", "ERROR");
                            }
                        }
                        else
                        {
                            failCount++;
                            log?.Send($"❌ {opInfo} | Error: {result.Error}", "ERROR");
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not ReceiptUnavailableException)
                    {
                        failCount++;
                        log?.Send($"❌ {chainName} {token.Symbol} | {ex.Message}", "ERROR");
                    }
                }
            }
            
            log?.Send($"🏁 Done | Total: {successCount + failCount + skips.Count} | Success: {successCount} | Fail: {failCount} | Skipped: {skips.Count}");
            return new SwapResult(successCount, failCount) { RefreshError = refreshErrors.Count == 0 ? null : string.Join("; ", refreshErrors), SkipReasons = skips };
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not ReceiptUnavailableException)
        {
            log?.Send($"🚨 Critical: {ex.Message}", "CRITICAL");
            return new SwapResult(successCount, failCount, ex.Message) { RefreshError = refreshErrors.Count == 0 ? null : string.Join("; ", refreshErrors), SkipReasons = skips };
        }
    }

    /// <summary>
    /// Мостит все нативные токены в целевую сеть
    /// </summary>
    public static async Task BridgeAllNative(Db db, int id, decimal minValue, string destinationChain, string pin,
        Protocol clientType = Protocol.LiFi, int delayMs = 108, Logger log = null, string chains = null, string integrator = null, decimal? fee = null)
    {
        int successCount = 0;
        int failCount = 0;
        log = log ?? new Logger(true, acc: id.ToString());
        
        try
        {
            var key = await GetKey(db, id, pin);
            if (string.IsNullOrEmpty(key))
            {
                log?.Send("❌ Error: Private key not found", "ERROR");
                return;
            }

            var jumper = new Jumper(log);
            var chainNames = await jumper.GetChainMapping();
            chainNames = FilterChains(chainNames, filter: chains);
            
            var account = new Account(key);
            var bal = await jumper.GetBalances(account.Address);
            
            int destinationId;
            try
            {
                destinationId = Rpc.ChainId(destinationChain);
            }
            catch (ArgumentException ex)
            {
                log?.Send($"❌ Invalid destination chain: {destinationChain} | {ex.Message}", "ERROR");
                return;
            }
            
            log?.Send($"Checking {account.Address} | Client: {clientType} | Target: {destinationChain} | Chains: {string.Join(", ", chainNames.Values)}");
            
            if (bal?.Balances == null || !bal.Balances.Any())
            {
                log?.Send($"👛 {account.Address} | ⚠️ No balances found", "WARNING");
                return;
            }

            var bridgeClient = BridgeClientFactory.Create(clientType, log, integrator, fee);
            

            foreach (var chain in bal.Balances)
            {
                if (!int.TryParse(chain.Key, out int chainIdInt)) continue;
                if (!chainNames.ContainsKey(chainIdInt)) continue;

                if (chainIdInt == destinationId)
                {
                    log?.Send($"⏭️ Skip {chainNames.GetValueOrDefault(chainIdInt, chain.Key)} - same as destination", "INFO");
                    continue;
                }
                
                string chainName = chainNames.GetValueOrDefault(chainIdInt, $"ID:{chain.Key}");
                var nativeAddresses = new[] 
                { 
                    "0x0000000000000000000000000000000000000000", 
                    "0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE" 
                };

                var tokensToBridge = chain.Value.Where(t => 
                    t.ValueUSD > minValue && 
                    nativeAddresses.Contains(t.Address)
                ).ToList();

                if (!tokensToBridge.Any()) continue;

                var rpcUrl = Rpc.Get(chainName);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log?.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    continue;
                }
                
                var web3 = CreateWeb3(key, rpcUrl, clientType);

                // Для Relay нужен Web3 с HttpDebugHandler


                foreach (var token in tokensToBridge)
                {
                    try
                    {
                        string opInfo = $"[{chainName} → {destinationChain}] {token.Symbol} ({token.ValueUSD:F2} USD)";
                        
                        BigInteger amountToBridge = BigInteger.Parse(token.Amount);
                        
                        // Резервируем газ для нативных токенов
                        if (nativeAddresses.Contains(token.Address.ToLower()))
                        {
                            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
                            BigInteger gasReserve = GasPricing.Price(gasPrice.Value) * 400000;
                            
                            var balance = await web3.Eth.GetBalance.SendRequestAsync(account.Address);
                            
                            if (balance.Value <= gasReserve) 
                            {
                                log?.Send($"⏭️ {opInfo} | Skip: Balance too low ({balance.Value} < {gasReserve})", "INFO");
                                continue;
                            }
                            amountToBridge = balance.Value - gasReserve;
                        }
                        
                        await Task.Delay(delayMs);

                        var quoteRequest = new BridgeQuoteRequest
                        {
                            WalletAddress = account.Address,
                            FromChainId = chainIdInt,
                            ToChainId = destinationId,
                            FromTokenAddress = token.Address,
                            ToTokenAddress = "0x0000000000000000000000000000000000000000",
                            Amount = amountToBridge.ToString(),
                            Slippage = 0.03m,
                            Integrator = integrator,
                            Fee = fee?.ToString()
                        };

                        var quote = await bridgeClient.GetQuoteAsync(quoteRequest);
                        var result = await bridgeClient.ExecuteAsync(account, quote, web3);

                        if (result.Success)
                        {
                            successCount++;
                            log?.Send($"✅ {opInfo} | TX: {result.TxHash} | {result.Details}", "SUCCESS");
                        }
                        else
                        {
                            failCount++;
                            log?.Send($"❌ {opInfo} | Status: {result.Status} | Error: {result.Error}", "ERROR");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        log?.Send($"❌ {chainName} {token.Symbol} | {ex.Message}", "ERROR");
                    }
                }
            }
            
            log?.Send($"🏁 Done | Total: {successCount + failCount} | Success: {successCount} | Fail: {failCount}");
        }
        catch (Exception ex)
        {
            log?.Send($"🚨 Critical: {ex.Message}", "CRITICAL");
        }
    }



    #region Helper Methods

    private static Dictionary<int, string> FilterChains(Dictionary<int, string> toFilter, string filter)
    {
        if (string.IsNullOrEmpty(filter)) 
            return toFilter;

        var filterArray = filter.Split(',').Select(s => s.Trim().ToLower()).ToHashSet();

        return toFilter
            .Where(item => filterArray.Contains(item.Value.ToLower()))
            .ToDictionary(item => item.Key, item => item.Value);
    }

    internal static async Task<string> GetKey(Db db, int id, string pin)
    {
        try
        {
            var key = db.Get("secp256k1", "_wallets", where: $"id = {id}");
            
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }
            
            if (string.IsNullOrEmpty(pin))
            {
            }
            key = SAFU.Decode(key, pin.FromBase64(), id.ToString());
            return key;
        }
        catch (Exception ex)
        {
            return string.Empty;
        }
    }

    public static Web3 CreateWeb3(string key, string rpcUrl, Protocol clientType)
    {
        var account = new Account(key); // Без фиксации ChainId в конструкторе
        var httpClient = new HttpClient(new HttpDebugHandler(clientType.ToString()) 
        { 
            InnerHandler = new HttpClientHandler() 
        });
    
        var rpcClient = new Nethereum.JsonRpc.Client.RpcClient(new Uri(rpcUrl), httpClient);
        return new Web3(account, rpcClient);
    }


    private static bool IsStableCoin(Jumper.TokenInfo t, double threshold = 0.01)
    {
        if (decimal.TryParse(t.PriceUSD, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out decimal price)) 
        {
            return Math.Abs((double)price - 1.0) <= threshold;
        }
        return false;
    }


    #endregion
    
    
    [Function("balanceOf", "uint256")]
    public class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "_owner", 1)]
        public string Owner { get; set; }
    }
}

