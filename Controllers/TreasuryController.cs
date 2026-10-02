using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using z3n;
namespace z3nSafe.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class TreasuryController : ControllerBase
{
    #region Members
    
    private readonly DbConnectionService _dbService;
    private readonly LogService _logService;
    private readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "logs");
    public TreasuryController(DbConnectionService dbService, LogService logService)
    {
        _dbService = dbService;
        _logService = logService;
    }
    private IActionResult CheckDbConnection()
    {
        if (!_dbService.IsConnected)
        {
            return StatusCode(503, new { 
                error = "Database not configured",
                needsConfiguration = true
            });
        }
        return null;
    }
    // DTO classes to avoid serialization issues
    public class AccountDto
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public Dictionary<string, List<TokenDto>> ChainData { get; set; }
    }

    public class TokenDto
    {
        public string Symbol { get; set; }
        public string AmountRaw { get; set; }
        public int Decimals { get; set; }
        public string PriceUSDString { get; set; }
        public int ChainId { get; set; }
        public string Address { get; set; }
        public decimal ValueUSD { get; set; }
    }
    
    
    public class ExecuteRequest
    {
        public int Id { get; set; }
        public List<string> Chains { get; set; } = new();
        public string? Destination { get; set; } // только для bridge
        public string Protocol { get; set; } = "LiFi"; // LiFi или Relay
        public decimal Threshold { get; set; } = 0.01m;
        public bool ExcludeStables { get; set; }
        public decimal GasBoostPercent { get; set; } = GasPricing.DefaultPercent;
    }
    public class ImportWalletsRequest
    {
        public List<string> Wallets { get; set; }
    }
    
    public class PinRequest
    {
        public string Pin { get; set; }
    }
    
    private static string _pin = "";
    private static Protocol _protocol = Protocol.LiFi;
    private static Logger _log = new  Logger(true);
    private static readonly object UpdateLock = new();
    private static bool _updating;
    private static TasksDb.UpdateResult? _updateProgress;
    private static string? _updateError;
    private static DateTimeOffset? _updateFinishedAt;

    [HttpGet("update-status")]
    public IActionResult GetUpdateStatus()
    {
        lock (UpdateLock)
            return Ok(new { running = _updating, progress = _updateProgress, error = _updateError, finishedAt = _updateFinishedAt });
    }
    
    #endregion
    
    private Protocol ParseProtocol(string protocolString)
    {
        if (Enum.TryParse<Protocol>(protocolString, true, out var protocol))
        {
            return protocol;
        }
    
        Console.WriteLine($"⚠️ Unknown protocol '{protocolString}', using LiFi");
        return Protocol.LiFi;
    }
    
    
    
    
    
    [HttpGet("db-status")]
    public IActionResult GetDbStatus()
    {
        return Ok(new { 
            connected = _dbService.IsConnected,
            error = _dbService.StartupError,
            config = PublicDbConfig()
        });
    }

    [HttpPost("db-config")]
    public IActionResult ConfigureDatabase([FromBody] DbConfig config)
    {
        try
        {
            _dbService.Connect(config);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            var error = DatabaseErrors.Describe(ex);
            return BadRequest(new { error = error.Message, code = error.Code, success = false });
        }
    }

    [HttpGet("db-config")]
    public IActionResult GetDbConfig() => Ok(PublicDbConfig());

    private object? PublicDbConfig()
    {
        var config = _dbService.GetCurrentConfig();
        return config == null ? null : new { config.Type, config.SqlitePath, config.Host, config.Port,
            config.Database, config.User, passwordSaved = !string.IsNullOrEmpty(config.Password) };
    }

    [HttpGet("keyboard-status")]
    public IActionResult KeyboardStatus() => Ok(new { capsLock = System.Windows.Forms.Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock) });

    [HttpGet("data")]
    public IActionResult GetTreasuryData([FromQuery] int maxId = 1000, [FromQuery] string chains = null)
    {
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;
        try
        {
            var db = _dbService.GetDb();
            var selectedChains = string.IsNullOrEmpty(chains) 
                ? null 
                : chains.Split(',').ToList();
            
            var generator = new HeatmapGenerator(db);
            var data = generator.GetTreasuryData(maxId, selectedChains);            
            // Convert to DTO
            var dtoList = data.Select(a => new AccountDto
            {
                Id = a.Id,
                Address = a.Address,
                ChainData = a.ChainData.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(t => new TokenDto
                    {
                        Symbol = t.Symbol,
                        AmountRaw = t.Amount,
                        Decimals = t.Decimals,
                        PriceUSDString = t.PriceUSD,
                        ChainId = t.ChainId,
                        Address = t.Address,
                        ValueUSD = t.ValueUSD
                    }).ToList()
                )
            }).ToList();
            
            return Ok(dtoList);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in GetTreasuryData: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [HttpGet("stats")]
    public IActionResult GetStats([FromQuery] int maxId = 1000)
    {
        
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;
    
           
        try
        {
            var db = _dbService.GetDb();
            var generator = new HeatmapGenerator(db);
            var data = generator.GetTreasuryData(maxId);

            var totalValue = 0m;
            var byChain = new Dictionary<string, object>();

            foreach (var account in data)
            {
                foreach (var kvp in account.ChainData)
                {
                    var chainName = kvp.Key;
                    var tokens = kvp.Value;
                    var chainTotal = tokens.Sum(t => t.ValueUSD);
                    
                    totalValue += chainTotal;
                    
                    if (!byChain.ContainsKey(chainName))
                    {
                        byChain[chainName] = new { accounts = 0, totalValue = 0m };
                    }
                    
                    var current = byChain[chainName] as dynamic;
                    byChain[chainName] = new 
                    { 
                        accounts = (current?.accounts ?? 0) + 1,
                        totalValue = (current?.totalValue ?? 0m) + chainTotal
                    };
                }
            }

            var stats = new
            {
                totalAccounts = data.Count,
                activeAccounts = data.Count(a => a.ChainData.Any()),
                totalChains = data.SelectMany(a => a.ChainData.Keys).Distinct().Count(),
                totalValue = totalValue,
                byChain = byChain
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in GetStats: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("account/{id}")]
    public IActionResult GetAccountDetails(int id)
    {
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;

        try
        {
            var db = _dbService.GetDb();
            var address = db.Get("evm", "_addresses", where: $"id = {id}");
            if (string.IsNullOrEmpty(address))
                return NotFound(new { error = "Account not found" });

            var columns = db.GetTableColumns("_treasury");
            var chainColumns = columns.Where(c => c.ToLower() != "id").ToList();

            var chainData = new Dictionary<string, List<TokenDto>>();
            var totalValue = 0m;

            foreach (var chainName in chainColumns)
            {
                var chainJson = db.Get(chainName, "_treasury", where: $"id = {id}");
                if (!string.IsNullOrEmpty(chainJson))
                {
                    try
                    {
                        var tokens = JsonConvert.DeserializeObject<List<HeatmapGenerator.TokenInfo>>(chainJson);
                        tokens = tokens?.Where(t => t.ChainId > 0 && t.ValueUSD > 0).ToList();
                        if (tokens != null && tokens.Count > 0)
                        {
                            var tokenDtos = tokens.Select(t => new TokenDto
                            {
                                Symbol = t.Symbol,
                                AmountRaw = t.Amount,
                                Decimals = t.Decimals,
                                PriceUSDString = t.PriceUSD,
                                ChainId = t.ChainId,
                                Address = t.Address,
                                ValueUSD = t.ValueUSD
                            }).ToList();
                            
                            chainData[chainName] = tokenDtos;
                            totalValue += tokenDtos.Sum(t => t.ValueUSD);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing chain {chainName}: {ex.Message}");
                    }
                }
            }

            return Ok(new
            {
                id,
                address,
                chainData,
                totalValue
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in GetAccountDetails: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("update")]
    public async Task<IActionResult> UpdateBalances([FromQuery] int maxId = 100, [FromQuery] decimal minValue = 0.001m,
        [FromQuery] int[]? accountIds = null)
    {        
        if (maxId < 1 || minValue < 0) return BadRequest(new { error = "maxId must be positive and minValue non-negative" });
        if (accountIds?.Any(id => id < 1 || id > maxId) == true)
            return BadRequest(new { error = "Selected account IDs must be within Max ID" });
        var targets = TasksDb.BalanceUpdateAccounts(maxId, accountIds);
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;

      
            
        try
        {
            var db = _dbService.GetDb();
            lock (UpdateLock)
            {
                if (_updating) return Conflict(new { error = "A balance update is already running" });
                _updating = true;
                _updateProgress = new TasksDb.UpdateResult(0, 0, 0, 0, 0);
                _updateError = null;
                _updateFinishedAt = null;
            }
            // Run update in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await TasksDb.UpdateDb(db, maxId, minValue, accountIds: targets, progress: value =>
                    {
                        lock (UpdateLock) _updateProgress = value;
                    });
                    lock (UpdateLock) _updateProgress = result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Background update error: {ex.Message}");
                    lock (UpdateLock) _updateError = ex.Message;
                }
                finally
                {
                    lock (UpdateLock)
                    {
                        _updating = false;
                        _updateFinishedAt = DateTimeOffset.UtcNow;
                    }
                }
            });

            return Accepted(new { message = $"Update started: {targets.Length} accounts", maxId, minValue, accounts = targets.Length });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in UpdateBalances: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("chains")]
    public IActionResult GetChains()
    {
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;

        try
        {
            var db = _dbService.GetDb();
            var columns = db.GetTableColumns("_treasury");
            var chains = columns.Where(c => c.ToLower() != "id").ToList();
            return Ok(chains);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in GetChains: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { status = "OK", message = "API is working" });
    }
    
    [HttpPost("pin")]
    public IActionResult SetPin([FromBody] PinRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Pin))
            {
                return BadRequest(new { error = "PIN is required" });
            }
            
            _pin = request.Pin;
            Console.WriteLine("✅ PIN set successfully");
            
            return Ok(new { success = true, message = "PIN set successfully" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR in SetPin: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("swap-chains")]
    public async Task<IActionResult> ChainsToNative([FromBody] ExecuteRequest request)
    {
        if (!GasPricing.IsValid(request.GasBoostPercent)) return BadRequest(new { error = "Gas +% must be between 0 and 1000, with at most two decimal places" });
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;
        try
        {
            var db = _dbService.GetDb();
            var pin = _pin;
            var protocol = ParseProtocol(request.Protocol);
            Console.WriteLine($"🚀 Starting swap-chains for account {request.Id}");
            Console.WriteLine($"   Chains: {string.Join(", ", request.Chains)}");
            Console.WriteLine($"   Threshold: {request.Threshold}, ExcludeStables: {request.ExcludeStables}");
            (Guid Id, CancellationTokenSource Cancellation) operation;
            lock (TokenSwapLock)
            {
                if (_defiExitRunning) return Conflict(new { error = "Wait for the DeFi withdrawal to finish" });
                operation = RegisterSwap();
            }
        
            _ = Task.Run(async () =>
            {
                try
                {
                    await SwapExecution.Run(operation.Cancellation.Token, async () => await DeFi.SwapAllTokensNative(
                        db, 
                        request.Id, 
                        request.Threshold, 
                        pin, 
                        excludeStables: request.ExcludeStables,
                        clientType: protocol, 
                        chains: string.Join(",", request.Chains),
                        log:_log
                    ), request.GasBoostPercent);
                    Console.WriteLine($"✅ Completed swap for account {request.Id}");
                }
                catch (OperationCanceledException)
                {
                    _log.Send($"Swap stopped for account #{request.Id}. Broadcast transactions must be checked on-chain.", "WARNING");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Background swap error for {request.Id}: {ex.Message}");
                    _log.Send($"Swap stopped for account #{request.Id}: {SwapExecution.ErrorDetails(ex)}", "ERROR");
                }
                finally { FinishSwap(operation.Id); }
            });
            
            return NoContent();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR in ChainsToNative: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("bridge-chains")]
    public async Task<IActionResult> NativeToOneChain([FromBody] ExecuteRequest request)
    {
        if (!GasPricing.IsValid(request.GasBoostPercent)) return BadRequest(new { error = "Invalid gas boost percentage" });
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;
        try
        {
            var db = _dbService.GetDb();
            if (string.IsNullOrEmpty(request.Destination))
            {
                return BadRequest(new { error = "Destination chain is required" });
            }
            
            var pin = _pin;
            var protocol = ParseProtocol(request.Protocol);
            Console.WriteLine($"🚀 Starting bridge for account {request.Id} to {request.Destination}");
            Console.WriteLine($"   Chains: {string.Join(", ", request.Chains)}");
            Console.WriteLine($"   Bridge: {request.Protocol}, Threshold: {request.Threshold}");
            lock (TokenSwapLock)
            {
                if (_defiExitRunning) return Conflict(new { error = "Wait for the DeFi withdrawal to finish" });
                _defiBlockingBridges++;
            }
        
            _ = Task.Run(async () =>
            {
                try
                {
                    await SwapExecution.Run(CancellationToken.None, async () => await DeFi.BridgeAllNative(
                        db, 
                        request.Id, 
                        request.Threshold, 
                        request.Destination, 
                        pin, 
                        clientType: protocol, 
                        chains: string.Join(",", request.Chains)
                    ), request.GasBoostPercent);
                    Console.WriteLine($"✅ Completed bridge for account {request.Id} to {request.Destination}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Background bridge error for {request.Id}: {ex.Message}");
                }
                finally { lock (TokenSwapLock) _defiBlockingBridges--; }
            });
        
            return NoContent();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR in NativeToOneChain: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    #region LOG
    [HttpPost("log")]
    public async Task<IActionResult> ReceiveLog()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        await _logService.SaveLog(json);
        return Ok();
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int limit = 100, [FromQuery] string level = null)
    {
        // Вызов ReadLogs из сервиса
        var logs = await _logService.ReadLogs(limit, level, null, null, null, null, null, null);
        return Ok(logs);
    }

    [HttpPost("http-log")]
    public async Task<IActionResult> ReceiveHttpLog()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        await _logService.SaveHttpLog(json);
        return Ok();
    }
    
    [HttpPost("clear")]
    public IActionResult Clear()
    {
        _logService.ClearAllLogs();
        return Ok();
    }
    // --- ЭНДПОИНТЫ ДЛЯ ДАШБОРДА (HTML) ---

    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "dashboard.html");
        //if (!File.Exists(path)) return NotFound("Dashboard HTML missing");
        return PhysicalFile(path, "text/html");
    }
    
    
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new { 
            baseDirectory = AppContext.BaseDirectory, // Папка, где лежит .exe / .dll
            currentDirectory = Directory.GetCurrentDirectory() // Папка, из которой запущен терминал
        });
    }
    
    [HttpPost("import-wallets")]
    public async Task<IActionResult> ImportWallets([FromBody] ImportWalletsRequest request)
    {
        var walletsList = request.Wallets;

        if (walletsList == null || !walletsList.Any())
            return BadRequest("No wallets provided.");
        
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;
        try
        {
            var db = _dbService.GetDb();
            
            await DBuilder.ImportWalletsAsync(db, request.Wallets);
            return Ok(new { 
                success = true, 
                message = $"Обработано кошельков: {request.Wallets.Count}" 
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR in ImportWallets: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    
    #endregion
    
}
