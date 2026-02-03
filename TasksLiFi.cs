using System.Numerics;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using LiFiBridge;
using z3n;

namespace z3nSafe;

public class TasksLiFi
{
    /// <summary>
    /// Обменивает все токены на нативную валюту в каждой сети через LiFi
    /// </summary>
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

            // Инициализация LiFi клиента
            var lifi = new LiFiBridgeClient(log: log);

            foreach (var chain in bal.Balances)
            {
                if (!int.TryParse(chain.Key, out int chainIdInt)) continue;
                if (!chainNames.ContainsKey(chainIdInt)) continue;
                
                string chainName = chainNames.GetValueOrDefault(chainIdInt, $"ID:{chain.Key}");
                
                // Фильтруем токены для обмена (исключаем нативные токены)
                var tokensToSwap = chain.Value.Where(t => 
                    t.ValueUSD > minValue && 
                    t.Address != "0x0000000000000000000000000000000000000000" && 
                    t.Address != "0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE"
                ).ToList();

                if (!tokensToSwap.Any()) continue;

                // Проверка наличия RPC URL
                var rpcUrl = Rpc.Get(chainName);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    continue;
                }

                foreach (var token in tokensToSwap)
                {
                    try
                    {
                        string opInfo = $"[{chainName}] {token.Symbol} ({token.ValueUSD:F2} USD)";
                        
                        await Task.Delay(delayMs);

                        // Создаем запрос quote через LiFi
                        var quoteRequest = new QuoteRequest
                        {
                            FromChain = chainIdInt,
                            ToChain = chainIdInt,
                            FromToken = token.Address,
                            ToToken = "0x0000000000000000000000000000000000000000", // Native token
                            FromAmount = token.Amount,
                            FromAddress = account.Address,
                            Slippage = 0.03m, // 3% slippage
                            Order = "RECOMMENDED"
                        };

                        var quote = await lifi.GetQuoteAsync(quoteRequest);
                        
                        // Выполняем обмен
                        var result = await lifi.ExecuteTransferAsync(
                            account,
                            quote,
                            waitForCompletion: false // Не ждем подтверждения, т.к. это swap в одной сети
                        );

                        if (result.Status == "DONE" || result.Status == "success")
                        {
                            successCount++;
                            log.Send($"✅ {opInfo} | TX: {result.TxHash}", "SUCCESS");
                        }
                        else
                        {
                            failCount++;
                            log.Send($"❌ {opInfo} | Error: {result.Error}", "ERROR");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        log.Send($"❌ {chainName} {token.Symbol} | {ex.Message}", "ERROR");
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

    /// <summary>
    /// Мостит все нативные токены в целевую сеть через LiFi
    /// </summary>
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
            
            log.Send($"Checking {account.Address} | Chains: {string.Join(", ", chainNames.Values.ToArray())}");
            
            if (bal?.Balances == null || !bal.Balances.Any())
            {
                log.Send($"👛 {account.Address} | ⚠️ No balances found", "WARNING");
                return;
            }

            log.Send($"[BridgeAllNative] | Wallet: {account.Address} | Target: {destinationChain} | Chains to scan: {bal.Balances.Count}");
            
            var lifi = new LiFiBridgeClient(log: log);

            foreach (var chain in bal.Balances)
            {
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

                var tokensToBridge = chain.Value.Where(t => 
                    t.ValueUSD > minValue && 
                    nativeAddresses.Contains(t.Address)
                ).ToList();

                if (!tokensToBridge.Any()) continue;

                var rpcUrl = Rpc.Get(chainName);
                if (string.IsNullOrEmpty(rpcUrl))
                {
                    log.Send($"❌ {chainName} | Skip: No RPC URL", "ERROR");
                    continue;
                }

                var web3 = new Web3(account, rpcUrl);

                foreach (var token in tokensToBridge)
                {
                    try
                    {
                        string opInfo = $"[{chainName} → {destinationChain}] {token.Symbol} ({token.ValueUSD:F2} USD)";

                        BigInteger balance = BigInteger.Parse(token.Amount);
                        BigInteger amountToBridge = balance;
                        
                        
                        
                        if (nativeAddresses.Contains(token.Address.ToLower()))
                        {
                            var gasPriceResponse = await web3.Eth.GasPrice.SendRequestAsync();
                            var gasPrice = gasPriceResponse.Value;
                            
                            //var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
                            //BigInteger gasReserve = gasPrice.Value * 500000; 
                            
                            BigInteger bufferedGasPrice = (gasPrice * 115) / 100; 
                            BigInteger gasLimit = 800000; 
                            BigInteger gasReserve = gasLimit * bufferedGasPrice;
            
                            if (balance <= gasReserve) 
                            {
                                log.Send($"⏭️ {opInfo} | Skip: Balance too low ({balance} < {gasReserve})", "INFO");
                                continue;
                            }
                            amountToBridge = balance - gasReserve;
                            
                        }
                        
                        await Task.Delay(delayMs);

                        var quoteRequest = new QuoteRequest
                        {
                            FromChain = chainIdInt,
                            ToChain = destinationId,
                            FromToken = token.Address,
                            ToToken = "0x0000000000000000000000000000000000000000", 
                            FromAmount = amountToBridge.ToString(),
                            FromAddress = account.Address,
                            Slippage = 0.03m, 
                            Order = "RECOMMENDED",
                            Integrator = "z3nBank",
                            Fee = "0.05",
                            
                        };

                        var quote = await lifi.GetQuoteAsync(quoteRequest);
                        
                        var result = await lifi.ExecuteTransferAsync(
                            account,
                            quote,
                            waitForCompletion: true 
                        );

                        if (result.Status == "DONE")
                        {
                            successCount++;
                            var receivingTx = result.StatusDetails?.Receiving?.TxHash ?? "N/A";
                            log.Send($"✅ {opInfo} | Sending: {result.TxHash} | Receiving: {receivingTx}", "SUCCESS");
                        }
                        else
                        {
                            failCount++;
                            log.Send($"❌ {opInfo} | Status: {result.Status} | Error: {result.Error}", "ERROR");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        log.Send($"❌ {chainName} {token.Symbol} | {ex.Message}", "ERROR");
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

    /// <summary>
    /// Комбинированный метод: сначала меняет все токены на нативные, затем мостит в целевую сеть
    /// </summary>
    public static async Task SwapAndBridgeAll(Db dbConnection, int id, decimal minValue, string destinationChain, string pin, int delayMs = 108, Logger log = null, string chains = null)
    {
        log = log ?? new Logger(true, acc: id.ToString());
        
        log.Send("🔄 Step 1: Swapping all tokens to native...");
        await SwapAllTokensNative(dbConnection, id, minValue, pin, delayMs, log, chains);
        
        await Task.Delay(5000); // Пауза между этапами
        
        log.Send("🌉 Step 2: Bridging all native tokens to destination...");
        await BridgeAllNative(dbConnection, id, minValue, destinationChain, pin, delayMs, log, chains);
        
        log.Send("✅ SwapAndBridgeAll completed!");
    }

    /// <summary>
    /// Пакетная обработка кошельков
    /// </summary>
    public static async Task SwapAllTokensNativeBatch(Db dbConnection, int startId, int endId, decimal minValue, string pin, int delayMs = 108, string chains = null)
    {
        int successWallets = 0;
        int failWallets = 0;
        
        for (int id = startId; id <= endId; id++)
        {
            Console.WriteLine($"\n{'='*60}");
            Console.WriteLine($"Processing wallet {id}/{endId}");
            Console.WriteLine($"{'='*60}");
            
            var log = new Logger(true, acc: id.ToString());
            
            try
            {
                await SwapAllTokensNative(dbConnection, id, minValue, pin, delayMs, log, chains);
                successWallets++;
            }
            catch (Exception ex)
            {
                failWallets++;
                log.Send($"❌ Wallet {id} failed: {ex.Message}", "ERROR");
            }
            
            if (id < endId)
            {
                await Task.Delay(delayMs * 10); // Пауза между кошельками
            }
        }
        
        Console.WriteLine($"\n{'='*60}");
        Console.WriteLine($"📊 Summary:");
        Console.WriteLine($"✅ Success: {successWallets}");
        Console.WriteLine($"❌ Failed: {failWallets}");
        Console.WriteLine($"📈 Total: {endId - startId + 1}");
        Console.WriteLine($"{'='*60}");
    }

    /// <summary>
    /// Пакетная обработка бриджинга
    /// </summary>
    public static async Task BridgeAllNativeBatch(Db dbConnection, int startId, int endId, decimal minValue, string destinationChain, string pin, int delayMs = 108, string chains = null)
    {
        int successWallets = 0;
        int failWallets = 0;
        
        for (int id = startId; id <= endId; id++)
        {
            Console.WriteLine($"\n{'='*60}");
            Console.WriteLine($"Processing wallet {id}/{endId}");
            Console.WriteLine($"{'='*60}");
            
            var log = new Logger(true, acc: id.ToString());
            
            try
            {
                await BridgeAllNative(dbConnection, id, minValue, destinationChain, pin, delayMs, log, chains);
                successWallets++;
            }
            catch (Exception ex)
            {
                failWallets++;
                log.Send($"❌ Wallet {id} failed: {ex.Message}", "ERROR");
            }
            
            if (id < endId)
            {
                await Task.Delay(delayMs * 10); // Пауза между кошельками
            }
        }
        
        Console.WriteLine($"\n{'='*60}");
        Console.WriteLine($"📊 Summary:");
        Console.WriteLine($"✅ Success: {successWallets}");
        Console.WriteLine($"❌ Failed: {failWallets}");
        Console.WriteLine($"📈 Total: {endId - startId + 1}");
        Console.WriteLine($"{'='*60}");
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

    private static async Task<string> Key(Db dbConnection, int id, string pin)
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
            Console.WriteLine($"❌ Key retrieval error: {ex.Message}");
            return string.Empty;
        }
    }

    #endregion
}