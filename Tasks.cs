using System.Numerics;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using RelayBridge;
namespace z3nSafe;

using z3n;

public class Tasks
{

    public static async Task<bool> SwapTokenBalanceToNative(string privateKey, string rpcUrl, string originCurrency)
    {
        try
        {
            var relay = new RelayBridgeClient(apiKey: null, isTestnet: false);
           
            var account = new Account(privateKey);
            string walletAddress = account.Address;
            
            var web3 = new Web3(account, rpcUrl);
            
            var evmTools = new EvmTools(log: true);
            
            // Получаем chainId
            string chainIdHex = await evmTools.ChainId(rpcUrl);
            int chainId = Convert.ToInt32(chainIdHex.Replace("0x", ""), 16);
            
            // Получаем баланс токена
            string balanceHex = await evmTools.Erc20(originCurrency, rpcUrl, walletAddress);
            BigInteger balanceBigInt = BigInteger.Parse(balanceHex, System.Globalization.NumberStyles.HexNumber);
            
            // ✅ Проверка баланса
            if (balanceBigInt == 0)
            {
                Console.WriteLine($"⚠️ Баланс токена {originCurrency} = 0, обмен невозможен");
                return false;
            }
            
            Console.WriteLine($"💰 Баланс токена: {balanceBigInt}");
            
            var quoteRequest = new QuoteRequest
            {
                User = walletAddress,
                Recipient = walletAddress,
                OriginChainId = chainId,
                DestinationChainId = chainId,
                OriginCurrency = originCurrency,
                DestinationCurrency = "0x0000000000000000000000000000000000000000",
                Amount = balanceBigInt.ToString(),
                SlippageTolerance = "100"
            };

            Console.WriteLine($"🔄 Получение котировки...");
            var quote = await relay.GetQuoteAsync(quoteRequest);

            Console.WriteLine($"⚙️ Выполнение обмена...");
            var results = await relay.ExecuteStepsAsync(quote.Steps, web3, account);

            bool allSuccess = true;
            foreach (var r in results)
            {
                Console.WriteLine($"Шаг: {r.Step}, Статус: {r.Status}, TX: {r.TxHash}");
                if (r.Status != "completed" && r.Status != "success")
                {
                    allSuccess = false;
                }
            }
            
            return allSuccess;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка SwapAllToNative: {ex.Message}");
            return false;
        }
    }
    public static async Task<bool> SwapTokenBalanceToNative(Db dbConnection, int id, string pin, string rpcUrl, string originCurrency)
    {
        try
        {
            var key  = await Key(dbConnection, id, pin);
            if (string.IsNullOrEmpty(key)) return false;
            return await SwapTokenBalanceToNative(key, rpcUrl, originCurrency);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка SwapAllToNative (DB): {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Массовый обмен для диапазона кошельков
    /// </summary>
    public static async Task SwapAllToNativeBatch(Db dbConnection, int startId, int endId, string pin, string rpcUrl, string originCurrency, int delayMs = 108)
    {
        int successCount = 0;
        int failCount = 0;
        
        for (int id = startId; id <= endId; id++)
        {
            Console.WriteLine($"\n{'='*60}");
            Console.WriteLine($"Обработка кошелька {id}/{endId}");
            Console.WriteLine($"{'='*60}");
            
            bool success = await SwapTokenBalanceToNative(dbConnection, id, pin, rpcUrl, originCurrency);
            
            if (success)
            {
                successCount++;
                Console.WriteLine($"✅ Успешно: {id}");
            }
            else
            {
                failCount++;
                Console.WriteLine($"❌ Ошибка: {id}");
            }
            
            if (id < endId)
            {
                await Task.Delay(delayMs);
            }
        }
        
        Console.WriteLine($"\n{'='*60}");
        Console.WriteLine($"📊 Итоги:");
        Console.WriteLine($"✅ Успешно: {successCount}");
        Console.WriteLine($"❌ Ошибок: {failCount}");
        Console.WriteLine($"📈 Всего: {endId - startId + 1}");
        Console.WriteLine($"{'='*60}");
    }

    private static Dictionary<int, string> FilterChains(Dictionary<int, string> toFilter, string filter)
    {
        if (string.IsNullOrEmpty(filter)) 
            return toFilter;

        // Разбиваем и сразу обрезаем пробелы + приводим к нижнему регистру
        var filterArray = filter.Split(',').Select(s => s.Trim().ToLower()).ToHashSet();

        return toFilter
            .Where(item => filterArray.Contains(item.Value.ToLower()))
            .ToDictionary(item => item.Key, item => item.Value);
    }

    public static async Task SwapAllTokensNative(Db dbConnection, int id, decimal minValue, string pin, int delayMs = 108, Logger log = null, string chains = null)
    {
        int successCount = 0;
        int failCount = 0;
        log = log ?? new Logger(true, acc: id.ToString());
        
        try
        {
            var key = await Key(dbConnection, id, pin);
            if (string.IsNullOrEmpty(key))
            {
                log.Send("❌ Error: Private key not found", "ERROR");
                return;
            }

            var jumper = new Jumper(log);
            var chainNames = await jumper.GetChainMapping();
            chainNames = FilterChains(chainNames, filter: chains);
            
            var account = new Account(key);
            var bal = await jumper.GetBalances(account.Address);
            
            if (bal?.Balances == null || !bal.Balances.Any())
            {
                log.Send($"👛 {account.Address} | ⚠️ No balances found", "WARNING");
                return;
            }

            log.Send($"Checking {account.Address} | Chains to scan: {bal.Balances.Count}/{string.Join(", ", chainNames.Values.ToArray())}");

            foreach (var chain in bal.Balances)
            {
                if (!int.TryParse(chain.Key, out int chainIdInt)) continue;
                if (!chainNames.ContainsKey(chainIdInt)) continue;
 
                
                
                string chainName = chainNames.GetValueOrDefault(chainIdInt, $"ID:{chain.Key}");
                var tokensToSwap = chain.Value.Where(t => t.ValueUSD > minValue && 
                                                         t.Address != "0x0000000000000000000000000000000000000000" && 
                                                         t.Address != "0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE").ToList();

                if (!tokensToSwap.Any()) continue;

                // Инициализация Web3 для чейна
                var rpcUrl = Rpc.Get(chainName);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    continue;
                }

                var web3 = new Nethereum.Web3.Web3(account, new Nethereum.JsonRpc.Client.RpcClient(new Uri(rpcUrl), new HttpClient(new HttpDebugHandler("Relay") { InnerHandler = new HttpClientHandler() })));
                var relay = new RelayBridgeClient(null, false, log);

                foreach (var token in tokensToSwap)
                {
                    try
                    {
                        // Лог начала операции сразу со всеми вводными
                        string opInfo = $"[{chainName}] {token.Symbol} ({token.ValueUSD:F2} USD)";
                        
                        await Task.Delay(delayMs);

                        var quote = await relay.GetQuoteAsync(new QuoteRequest
                        {
                            User = account.Address,
                            Recipient = account.Address,
                            OriginChainId = chainIdInt,
                            DestinationChainId = chainIdInt,
                            OriginCurrency = token.Address,
                            DestinationCurrency = "0x0000000000000000000000000000000000000000",
                            Amount = token.Amount,
                            SlippageTolerance = "100",
                            TradeType = "EXACT_INPUT"
                        });

                        var results = await relay.ExecuteStepsAsync(quote.Steps, web3, account);
                        
                        // Собираем статусы шагов в одну строку
                        var stepsSummary = string.Join(" | ", results.Select(r => $"{r.Step}:{r.Status}"));
                        bool isOk = results.All(r => r.Status == "completed" || r.Status == "success");

                        if (isOk)
                        {
                            successCount++;
                            log.Send($"✅ {opInfo} | Steps: {stepsSummary}", "SUCCESS");
                        }
                        else
                        {
                            failCount++;
                            log.Send($"❌ {opInfo} | Failed at: {stepsSummary}", "ERROR");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        log.Send($"{chainName} ${token.Symbol} {ex.Message}", "ERROR");
                    }
                }
            }
            log.Send($"🏁 Done | Total: {successCount + failCount} | Success: {successCount} | Fail: {failCount}");
        }
        catch (Exception ex)
        {
            log?.Send($"🚨 Critical: {ex.Message}", "CRITICAL");
        }
    }
    public static async Task BridgeAllNative(Db dbConnection, int id, decimal minValue, string destinationChain, string pin, int delayMs = 108, Logger log = null, string chains = null)
    {
        int successCount = 0;
        int failCount = 0;
        log = log ?? new Logger(true, acc: id.ToString());
        
        try
        {
            var key = await Key(dbConnection, id, pin);
            if (string.IsNullOrEmpty(key))
            {
                log.Send("❌ Error: Private key not found", "ERROR");
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
                log.Send($"❌ Invalid destination chain: {destinationChain} | {ex.Message}", "ERROR");
                return;
            }
            
            log.Send($"checking {account.Address} Chains : {string.Join(", ", chainNames.Values.ToArray())}");
            
            
            if (bal?.Balances == null || !bal.Balances.Any())
            {
                log.Send($"👛 {account.Address} | ⚠️ No balances found", "WARNING");
                return;
            }

            log.Send($"[BridgeAllNative] | Wallet: {account.Address} | Chains to scan: {bal.Balances.Count}");
            
            foreach (var chain in bal.Balances)
            {
                var currentAccount = new Account(key);
                
                
                
                if (!int.TryParse(chain.Key, out int chainIdInt)) continue;
                if (!chainNames.ContainsKey(chainIdInt)) continue;

                if (chainIdInt == destinationId)
                {
                    log.Send($"⏭️ Skip {chainNames.GetValueOrDefault(chainIdInt, chain.Key)} - same as destination", "INFO");
                    continue;
                }
                string chainName = chainNames.GetValueOrDefault(chainIdInt, $"ID:{chain.Key}");
                var nativeAddresses = new[] 
                { 
                    "0x0000000000000000000000000000000000000000", 
                    "0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE" 
                };

                var tokensToSwap = chain.Value.Where(t => t.ValueUSD > minValue && 
                                                          nativeAddresses.Contains(t.Address)).ToList();

                if (!tokensToSwap.Any()) continue;

                var rpcUrl = Rpc.Get(chainName);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    continue;
                }

                var web3 = new Nethereum.Web3.Web3(currentAccount, new Nethereum.JsonRpc.Client.RpcClient(new Uri(rpcUrl), 
                    new HttpClient(new HttpDebugHandler("Relay") { InnerHandler = new HttpClientHandler() })));
                var relay = new RelayBridgeClient(null, false, log);

                foreach (var token in tokensToSwap)
                {
                    try
                    {
                        string opInfo = $"[{chainName} → {destinationChain}] {token.Symbol} ({token.ValueUSD:F2} USD)";

                        BigInteger amountToBridge = BigInteger.Parse(token.Amount);
                        if (nativeAddresses.Contains(token.Address.ToLower()))
                        {
                            
                            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
                            BigInteger gasReserve = gasPrice.Value * 400000;
            
                            if (amountToBridge <= gasReserve) {
                                log.Send($"⏭️ {opInfo} | Skip: Balance too low to cover gas", "INFO");
                                continue;
                            }
                            amountToBridge -= gasReserve;
                        }
                        
                        
                        await Task.Delay(delayMs);

                        var quote = await relay.GetQuoteAsync(new QuoteRequest
                        {
                            User = account.Address,
                            Recipient = account.Address,
                            OriginChainId = chainIdInt,
                            DestinationChainId = destinationId,
                            OriginCurrency = token.Address,
                            DestinationCurrency = "0x0000000000000000000000000000000000000000",
                            Amount = amountToBridge.ToString(),
                            SlippageTolerance = "100",
                            TradeType = "EXACT_INPUT"
                        });

                        var results = await relay.ExecuteStepsAsync(quote.Steps, web3, account);
                        
                        // Собираем статусы шагов в одну строку
                        var stepsSummary = string.Join(" | ", results.Select(r => $"{r.Step}:{r.Status}"));
                        bool isOk = results.All(r => r.Status == "completed" || r.Status == "success");

                        if (isOk)
                        {
                            successCount++;
                            log.Send($"✅ {opInfo} | Steps: {stepsSummary}", "SUCCESS");
                        }
                        else
                        {
                            failCount++;
                            log.Send($"❌ {opInfo} | Failed at: {stepsSummary}", "ERROR");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        log.Send($"{chainName} ${token.Symbol} {ex.Message}", "ERROR");
                    }
                }
            }
            log.Send($"🏁 Done | Total: {successCount + failCount} | Success: {successCount} | Fail: {failCount}");
        }
        catch (Exception ex)
        {
            log?.Send($"🚨 Critical: {ex.Message}", "CRITICAL");
        }
    }

    public static async Task<string> Key(Db dbConnection, int id, string pin)
    {
        
        try
        {
            var db = dbConnection;
            
            var key = db.Get("secp256k1", "_wallets", where: $"id = {id}");
            
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine($"❌ Не найден ключ для id = {id}");
                return string.Empty;
            }
            
            key = SAFU.Decode(key, pin, id.ToString());
            return key;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌: {ex.Message}");
            return string.Empty;
        }
        
    }
    
}