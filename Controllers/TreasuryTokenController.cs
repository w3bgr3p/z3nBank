using Microsoft.AspNetCore.Mvc;
using z3n;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    public sealed class TokenPlanRequest
    {
        public int MaxId { get; set; } = 100;
        public List<string> Chains { get; set; } = new();
        public List<TokenSelection.Asset> Assets { get; set; } = new();
        public List<int> AccountIds { get; set; } = new();
        public bool ExcludeStables { get; set; }
        public decimal Threshold { get; set; }
        public string Protocol { get; set; } = "Relay";
        public decimal GasBoostPercent { get; set; } = GasPricing.DefaultPercent;
    }
    private sealed record TokenPlan(Guid Id, DateTimeOffset Expires, List<TokenSelection.Target> Targets,
        decimal Threshold, Protocol Protocol, Db Database, decimal GasBoostPercent);
    private static readonly object TokenSwapLock = new();
    private static TokenPlan? _tokenPlan;
    private static bool _tokenSwapRunning;
    private static readonly List<object> TokenSwapResults = new();
    private static int _tokenSwapTotal;
    private static int? _tokenSwapCurrent;
    private static Guid? _tokenSwapJob;
    private static bool _tokenSwapCancelled;

    [HttpPost("token-swap/preview")]
    public IActionResult PreviewTokenSwap([FromBody] TokenPlanRequest request)
    {
        var dbCheck = CheckDbConnection();
        if (dbCheck != null) return dbCheck;
        if (request.AccountIds == null || request.AccountIds.Count is < 1 or > 10000 || request.AccountIds.Any(id => id < 1 || id > request.MaxId) ||
            !GasPricing.IsValid(request.GasBoostPercent) || request.MaxId is < 1 or > 10000 || request.Threshold < 0 || request.Assets == null ||
            request.Assets.Count is < 1 or > 200 || request.Assets.Any(a => a == null || a.ChainId <= 0 ||
                a.Address == null || !System.Text.RegularExpressions.Regex.IsMatch(a.Address, "^0x[0-9a-fA-F]{40}$")) ||
            !Enum.TryParse<Protocol>(request.Protocol, true, out var protocol) || !Enum.IsDefined(protocol))
            return BadRequest(new { error = "Invalid token selection, account range or protocol" });
        var db = _dbService.GetDb();
        var targets = TokenSelection.Plan(new HeatmapGenerator(db).GetTreasuryData(request.MaxId, request.Chains),
            request.Assets, request.Threshold, request.AccountIds, request.ExcludeStables);
        lock (TokenSwapLock)
        {
            if (_tokenSwapRunning) return Conflict(new { error = "Token swap is already running" });
            _tokenPlan = new TokenPlan(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), targets,
                request.Threshold, protocol, db, request.GasBoostPercent);
            return Ok(new { planId = _tokenPlan.Id, targets, accounts = targets.Select(t => t.Id).Distinct().Count(),
                totalUSD = targets.Sum(t => t.ValueUSD), gasBoostPercent = request.GasBoostPercent });
        }
    }

    [HttpPost("token-swap/execute")]
    public IActionResult ExecuteTokenSwap([FromBody] Guid planId)
    {
        var dbCheck = CheckDbConnection();
        if (dbCheck != null) return dbCheck;
        lock (TokenSwapLock)
        {
            if (_defiExitRunning) return Conflict(new { error = "Wait for the DeFi withdrawal to finish" });
            if (_tokenSwapRunning) return Conflict(new { error = "Token swap is already running" });
            var plan = _tokenPlan;
            if (plan == null || plan.Id != planId || plan.Expires < DateTimeOffset.UtcNow ||
                plan.Database != _dbService.GetDb())
                return BadRequest(new { error = "Preview expired or database changed; preview again" });
            if (plan.Targets.Count == 0) return BadRequest(new { error = "No swappable tokens" });
            if (string.IsNullOrEmpty(_pin)) return BadRequest(new { error = "Set wallet PIN first" });
            _tokenPlan = null;
            _tokenSwapRunning = true;
            _tokenSwapJob = plan.Id;
            _tokenSwapCancelled = false;
            TokenSwapResults.Clear();
            _tokenSwapTotal = plan.Targets.Select(t => t.Id).Distinct().Count();
            var pin = _pin;
            var operation = RegisterSwap();
            _ = Task.Run(async () => {
                try { await SwapExecution.Run(operation.Cancellation.Token, () => RunTokenSwap(plan, pin), plan.GasBoostPercent); }
                catch (OperationCanceledException) { lock (TokenSwapLock) _tokenSwapCancelled = true; }
                finally {
                    lock (TokenSwapLock) { _tokenSwapRunning = false; _tokenSwapCurrent = null; }
                    FinishSwap(operation.Id);
                }
            });
            return Accepted(new { jobId = plan.Id });
        }
    }

    [HttpGet("token-swap/status")]
    public IActionResult TokenSwapStatus()
    {
        lock (TokenSwapLock) return Ok(new { jobId = _tokenSwapJob, running = _tokenSwapRunning,
            total = _tokenSwapTotal, currentId = _tokenSwapCurrent, cancelled = _tokenSwapCancelled,
            results = TokenSwapResults.ToArray() });
    }

    private static async Task RunTokenSwap(TokenPlan plan, string pin)
    {
        try
        {
            foreach (var group in plan.Targets.GroupBy(t => t.Id))
            {
                SwapExecution.Check();
                lock (TokenSwapLock) _tokenSwapCurrent = group.Key;
                DeFi.SwapResult result;
                var haltQueue = false;
                string? refreshError = null;
                try
                {
                    var keys = group.Select(t => TokenSelection.Key(t.ChainId, t.Address)).ToHashSet();
                    result = await DeFi.SwapAllTokensNative(plan.Database, group.Key, plan.Threshold, pin,
                        clientType: plan.Protocol, chains: string.Join(",", group.Select(t => t.Chain).Distinct()),
                        log: new Logger(true, acc: group.Key.ToString()), tokenTargets: keys,
                        expectedWallet: group.First().Wallet, confirmedTargets: group.ToArray());
                    refreshError = result.RefreshError;
                }
                catch (ReceiptUnavailableException ex)
                {
                    result = new DeFi.SwapResult(0, 0, ex.Message);
                    haltQueue = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { result = new DeFi.SwapResult(0, 0, ex.Message); }
                lock (TokenSwapLock) TokenSwapResults.Add(new { id = group.Key, result.Succeeded, result.Failed,
                    result.Error, skipped = Math.Max(0, group.Count() - result.Succeeded - result.Failed), result.SkipReasons, refreshError });
                if (haltQueue)
                {
                    _log.Send($"Queue stopped for account #{group.Key}: {result.Error}", "ERROR");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (TokenSwapLock) _tokenSwapCancelled = true;
            _log.Send("Token swap queue stopped. Broadcast transactions must be checked on-chain.", "WARNING");
        }
        finally { lock (TokenSwapLock) { _tokenSwapRunning = false; _tokenSwapCurrent = null; } }
    }
}
