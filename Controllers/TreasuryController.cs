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
            connected = _dbService.IsConnected
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
            return StatusCode(500, new { error = ex.Message, success = false });
        }
    }

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
    public async Task<IActionResult> UpdateBalances([FromQuery] int maxId = 100, [FromQuery] decimal minValue = 0.001m)
    {        
        var dbCheck = CheckDbConnection(); 
        if (dbCheck != null) return dbCheck;

      
            
        try
        {
            var db = _dbService.GetDb();
            // Run update in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await TasksDb.UpdateDb(db, maxId, minValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Background update error: {ex.Message}");
                }
            });

            return Ok(new { message = "Update started", maxId, minValue });
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
        
            _ = Task.Run(async () =>
            {
                try
                {
                    await DeFi.SwapAllTokensNative(
                        db, 
                        request.Id, 
                        request.Threshold, 
                        pin, 
                        excludeStables: request.ExcludeStables,
                        clientType: protocol, 
                        chains: string.Join(",", request.Chains),
                        log:_log
                    );
                    Console.WriteLine($"✅ Completed swap for account {request.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Background swap error for {request.Id}: {ex.Message}");
                }
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
        
            _ = Task.Run(async () =>
            {
                try
                {
                    await DeFi.BridgeAllNative(
                        db, 
                        request.Id, 
                        request.Threshold, 
                        request.Destination, 
                        pin, 
                        clientType: protocol, 
                        chains: string.Join(",", request.Chains)
                    );
                    Console.WriteLine($"✅ Completed bridge for account {request.Id} to {request.Destination}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Background bridge error for {request.Id}: {ex.Message}");
                }
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