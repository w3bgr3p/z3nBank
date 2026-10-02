using Microsoft.AspNetCore.Mvc;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using z3n;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    public sealed class DefiScanRequest { public int MaxId { get; set; } = 100; }
    public sealed class DefiExitRequest
    {
        public int AccountId { get; set; }
        public string PositionId { get; set; } = "";
        public decimal GasBoostPercent { get; set; } = GasPricing.DefaultPercent;
    }
    private static readonly object DefiLock = new();
    private static CancellationTokenSource? _defiScan;
    private static Db? _defiDb;
    private static readonly List<DefiPosition> DefiRows = new();
    private static readonly List<object> DefiErrors = new();
    private static readonly Dictionary<int, DefiScanAccount> DefiAccounts = new();
    private static int _defiDone, _defiTotal;
    private static DateTimeOffset? _defiUpdated;
    private static bool _defiCancelled;
    private static bool _defiNeedsRescan;
    private sealed record ExitPlan(Guid Id, DefiPosition Position, DefiVault.Quote Quote, Db Database,
        decimal GasPercent, DateTimeOffset Expires);
    private static ExitPlan? _defiExit;
    private static bool _defiExitRunning;
    private static int _defiBlockingBridges;
    private static string? _defiExitResult;

    [HttpGet("defi/status")]
    public IActionResult DefiStatus()
    {
        var check = CheckDbConnection(); if (check != null) return check;
        lock (DefiLock)
        {
            var db = _dbService.GetDb();
            if (_defiDb != db)
            {
                if (_defiScan != null || _defiPrepare != null || _defiExitRunning)
                    return Conflict(new { error = "Database changed during a DeFi operation" });
                RestoreDefiSnapshot(db);
            }
        }
        lock (DefiLock) return Ok(new { running = _defiScan != null, processed = _defiDone, total = _defiTotal,
            cancelled = _defiCancelled, updatedAt = _defiUpdated, positions = DefiRows.ToArray(), errors = DefiErrors.ToArray(),
            accounts = DefiAccounts.Values.OrderBy(a => a.Id).ToArray(),
            needsRescan = _defiNeedsRescan,
            preparing = _defiPrepare != null, prepareDone = _defiPrepareDone, prepareTotal = _defiPrepareTotal,
            batchTotal = _defiBatchTotal, batchAccount = _defiBatchAccount, batchResults = DefiBatchResults.ToArray(),
            exitRunning = _defiExitRunning, exitResult = _defiExitResult, provider = "Rabby (DeBank data)" });
    }
    [HttpPost("defi/scan")]
    public IActionResult ScanDefi([FromBody] DefiScanRequest request)
    {
        var check = CheckDbConnection(); if (check != null) return check;
        if (request.MaxId is < 1 or > 10000) return BadRequest(new { error = "Invalid account range" });
        lock (DefiLock)
        {
            if (_defiScan != null || _defiPrepare != null || _defiExitRunning) return Conflict(new { error = "DeFi operation is already running" });
            var db = _dbService.GetDb();
            var accounts = new List<(int Id, string Address)>();
            for (var id = 1; id <= request.MaxId; id++)
            {
                var address = db.Get("evm", "_addresses", where: $"id = {id}");
                if (DefiVault.AddressValid(address)) accounts.Add((id, address));
            }
            var source = new CancellationTokenSource(); _defiScan = source; _defiDb = db;
            _defiVersion++; _defiBatchPlan = null;
            _defiNeedsRescan = false; _defiExitResult = null;
            DefiRows.Clear(); DefiErrors.Clear(); _defiExit = null;
            DefiAccounts.Clear();
            foreach (var account in accounts) DefiAccounts[account.Id] = new DefiScanAccount(account.Id, account.Address, "pending");
            _defiDone = 0; _defiTotal = accounts.Count; _defiUpdated = null; _defiCancelled = false;
            _ = Task.Run(async () => {
                using var client = new DefiPositionsClient();
                var scanLog = new Logger(true);
                scanLog.Send($"DeFi scan started | {accounts.Count} accounts");
                try
                {
                    foreach (var account in accounts)
                    {
                        source.Token.ThrowIfCancellationRequested();
                        var accountLog = new Logger(true, acc: account.Id.ToString());
                        try
                        {
                            var rows = await client.ReadForScanAsync(account.Id, account.Address, source.Token);
                            lock (DefiLock) { DefiRows.AddRange(rows); DefiAccounts[account.Id] = new DefiScanAccount(account.Id, account.Address, "scanned"); }
                            accountLog.Send($"DeFi scan completed | {rows.Count} position assets", "SUCCESS");
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            accountLog.Send($"DeFi scan failed; continuing | {SwapExecution.ErrorDetails(ex)}", "ERROR");
                            lock (DefiLock) {
                                DefiErrors.Add(new { accountId = account.Id, error = ex.Message });
                                DefiAccounts[account.Id] = new DefiScanAccount(account.Id, account.Address, "error");
                            }
                            // Account errors, including exhausted provider retries, do not cancel the batch.
                        }
                        finally { lock (DefiLock) { if (DefiAccounts[account.Id].Status != "pending") _defiDone++; SaveDefiProgress(); } }
                        await Task.Delay(1100, source.Token);
                    }
                }
                catch (OperationCanceledException) { lock (DefiLock) _defiCancelled = true; scanLog.Send("DeFi scan stopped", "WARNING"); }
                finally {
                    lock (DefiLock) {
                        _defiScan = null; _defiUpdated = DateTimeOffset.UtcNow;
                        SaveDefiProgress();
                        scanLog.Send($"DeFi scan finished | {_defiDone}/{_defiTotal} accounts | {DefiRows.Count} position assets | {DefiErrors.Count} errors");
                    }
                    source.Dispose();
                }
            });
            return Accepted(new { accounts = accounts.Count });
        }
    }
    [HttpPost("defi/stop")]
    public IActionResult StopDefiScan() { lock (DefiLock) { _defiScan?.Cancel(); _defiPrepare?.Cancel(); return Ok(); } }

    [HttpPost("defi/withdraw/preview")]
    public async Task<IActionResult> PreviewDefiExit([FromBody] DefiExitRequest request)
    {
        var check = CheckDbConnection(); if (check != null) return check;
        if (!GasPricing.IsValid(request.GasBoostPercent)) return BadRequest(new { error = "Invalid gas percentage" });
        DefiPosition? position; var db = _dbService.GetDb(); int version;
        lock (DefiLock)
        {
            if (_defiScan != null || _defiPrepare != null || _defiExitRunning) return Conflict(new { error = "DeFi operation is already running" });
            if (_defiNeedsRescan) return Conflict(new { error = "Positions changed after a broadcast. Scan DeFi again before another withdrawal." });
            position = DefiRows.FirstOrDefault(p => p.AccountId == request.AccountId && p.Id == request.PositionId);
            version = _defiVersion;
            if (db != _defiDb) position = null;
        }
        if (position == null) return BadRequest(new { error = "Scan DeFi positions for the current database first" });
        if ((position.Type is not ("deposit" or "staked") && !DefiStargate.IsSupported(position) && !DefiJoe.IsSupported(position) && !DefiLayerBankRewards.IsSupported(position)) || !DefiVault.AddressValid(position.VaultAddress) || !DefiVault.AddressValid(position.AssetAddress))
            return BadRequest(new { error = "No automatic withdrawal adapter for this position" });
        var previewLog = new Logger(true, acc: position.AccountId.ToString());
        previewLog.Send($"DeFi withdrawal check | {position.Protocol} | {position.Chain} | {position.Symbol}");
        try
        {
            var unavailable = DefiWithdrawal.Unavailable(position);
            if (unavailable != null) { previewLog.Send(unavailable, "WARNING"); return BadRequest(new { error = unavailable }); }
            var network = DefiVault.Network(position.Chain);
            DefiVault.Quote? quote = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            await SwapExecution.Run(timeout.Token, async () => {
                quote = await DefiWithdrawal.Prepare(new Web3(network.Rpc), network.ChainId, position, request.GasBoostPercent);
            }, request.GasBoostPercent);
            lock (DefiLock)
            {
                if (_defiVersion != version || _defiNeedsRescan || _defiScan != null || _defiPrepare != null || _defiExitRunning || _defiDb != db || db != _dbService.GetDb())
                    return Conflict(new { error = "Positions changed; preview again" });
                _defiExit = new ExitPlan(Guid.NewGuid(), position, quote!, db, request.GasBoostPercent, DateTimeOffset.UtcNow.AddMinutes(2));
                previewLog.Send($"DeFi preview ready | {position.Protocol} | {position.Chain} | output ${quote!.ValueUsd:0.########} | fee ${quote.FeeUsd:0.########}", "SUCCESS");
                return Ok(new { planId = _defiExit.Id, position, quote, gasBoostPercent = request.GasBoostPercent });
            }
        }
        catch (Exception ex) { var error = SwapExecution.ErrorDetails(ex); previewLog.Send($"DeFi withdrawal check failed | {error}", "ERROR"); return BadRequest(new { error }); }
    }

    [HttpPost("defi/withdraw/execute")]
    public IActionResult ExecuteDefiExit([FromBody] Guid planId)
    {
        var check = CheckDbConnection(); if (check != null) return check;
        lock (DefiLock)
        {
            lock (TokenSwapLock)
            {
                if (_defiScan != null || _defiPrepare != null || _defiExitRunning || SwapOperations.Count > 0 || _defiBlockingBridges > 0)
                    return Conflict(new { error = "Wait for the active operation to finish" });
                var plan = _defiExit;
                if (plan == null || _defiNeedsRescan || plan.Id != planId || plan.Expires < DateTimeOffset.UtcNow || plan.Database != _dbService.GetDb())
                    return BadRequest(new { error = "Withdrawal preview expired; preview again" });
                if (string.IsNullOrEmpty(_pin)) return BadRequest(new { error = "Set wallet PIN first" });
                var pin = _pin; _defiExit = null; _defiExitRunning = true; _defiExitResult = null;
                DefiBatchResults.Clear(); _defiBatchTotal = 0;
                var operation = RegisterSwap();
                _ = Task.Run(async () => {
                    var log = new Logger(true, acc: plan.Position.AccountId.ToString());
                    try
                    {
                        await SwapExecution.Run(operation.Cancellation.Token, async () => {
                            var hash = await ExecuteDefiPosition(plan, pin);
                            lock (DefiLock) _defiExitResult = $"Confirmed: {hash}. Refresh DeFi and wallet balances.";
                        }, plan.GasPercent);
                    }
                    catch (OperationCanceledException)
                    {
                        lock (DefiLock) _defiExitResult = (_defiExitResult ?? "Stopped before broadcast") + ". Tracking stopped; any broadcast transaction remains on-chain.";
                        log.Send(_defiExitResult, "WARNING");
                    }
                    catch (Exception ex) { lock (DefiLock) _defiExitResult = SwapExecution.ErrorDetails(ex); log.Send(_defiExitResult, "ERROR"); }
                    finally { lock (DefiLock) _defiExitRunning = false; FinishSwap(operation.Id); }
                });
                return Accepted(new { operationId = operation.Id });
            }
        }
    }
}
