using Microsoft.AspNetCore.Mvc;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using z3n;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    public sealed class DefiBatchRequest
    {
        public string Protocol { get; set; } = "";
        public List<int> AccountIds { get; set; } = new();
        public decimal GasBoostPercent { get; set; } = GasPricing.DefaultPercent;
    }
    private sealed record DefiBatchPlan(Guid Id, List<ExitPlan> Targets, Db Database, int Version, DateTimeOffset Expires);
    private static DefiBatchPlan? _defiBatchPlan;
    private static CancellationTokenSource? _defiPrepare;
    private static int _defiVersion, _defiPrepareDone, _defiPrepareTotal, _defiBatchTotal;
    private static int? _defiBatchAccount;
    private static readonly List<object> DefiBatchResults = new();

    [HttpPost("defi/batch/preview")]
    public async Task<IActionResult> PreviewDefiBatch([FromBody] DefiBatchRequest request)
    {
        var check = CheckDbConnection(); if (check != null) return check;
        if (string.IsNullOrWhiteSpace(request.Protocol) || request.AccountIds == null || request.AccountIds.Count is < 1 or > 10000 ||
            request.AccountIds.Any(id => id < 1 || id > 10000) || !GasPricing.IsValid(request.GasBoostPercent))
            return BadRequest(new { error = "Select a protocol, account batch and valid gas percentage" });
        List<DefiPosition> selected; var db = _dbService.GetDb(); int version;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        lock (DefiLock)
        {
            if (_defiScan != null || _defiPrepare != null || _defiExitRunning) return Conflict(new { error = "A DeFi operation is already running" });
            if (_defiDb != db || _defiNeedsRescan) return Conflict(new { error = "Scan current DeFi positions first" });
            selected = DefiBatch.Select(DefiRows, request.Protocol, request.AccountIds);
            if (selected.Count is < 1 or > 200) return BadRequest(new { error = "Choose a batch containing 1–200 protocol positions" });
            version = _defiVersion; _defiPrepare = source; _defiPrepareDone = 0; _defiPrepareTotal = selected.Count;
            _defiBatchPlan = null; _defiExit = null;
        }
        var targets = new List<ExitPlan>(); var skipped = new List<object>();
        var batchLog = new Logger(true);
        batchLog.Send($"DeFi batch check started | {request.Protocol} | {selected.Count} positions");
        try
        {
            foreach (var p in selected)
            {
                source.Token.ThrowIfCancellationRequested();
                var positionLog = new Logger(true, acc: p.AccountId.ToString());
                try
                {
                    if ((p.Type is not ("deposit" or "staked") && !DefiStargate.IsSupported(p) && !DefiJoe.IsSupported(p) && !DefiLayerBankRewards.IsSupported(p)) || !DefiVault.AddressValid(p.VaultAddress) || !DefiVault.AddressValid(p.AssetAddress))
                        throw new InvalidOperationException("Withdrawal adapter unavailable for this position");
                    var unavailable = DefiWithdrawal.Unavailable(p);
                    if (unavailable != null) throw new InvalidOperationException(unavailable);
                    var network = DefiVault.Network(p.Chain); DefiVault.Quote? quote = null;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    await SwapExecution.Run(timeout.Token, async () => quote = await DefiWithdrawal.Prepare(new Web3(network.Rpc),
                        network.ChainId, p, request.GasBoostPercent), request.GasBoostPercent);
                    targets.Add(new ExitPlan(Guid.NewGuid(), p, quote!, db, request.GasBoostPercent, DateTimeOffset.UtcNow.AddMinutes(10)));
                    positionLog.Send($"DeFi batch preview ready | {p.Protocol} | {p.Chain} | {p.Symbol} | output ${quote!.ValueUsd:0.########} | fee ${quote.FeeUsd:0.########}", "SUCCESS");
                }
                catch (Exception ex) when (!source.IsCancellationRequested)
                {
                    var reason = SwapExecution.ErrorDetails(ex);
                    skipped.Add(new { p.AccountId, p.Chain, p.Protocol, reason });
                    positionLog.Send($"DeFi batch position skipped | {p.Protocol} | {p.Chain} | {p.Symbol} | {reason}", "WARNING");
                }
                finally { lock (DefiLock) _defiPrepareDone++; }
            }
            lock (DefiLock)
            {
                if (_defiVersion != version || _defiDb != db || !_dbService.IsConnected || _dbService.GetDb() != db || _defiNeedsRescan)
                    return Conflict(new { error = "Positions changed; preview again" });
                _defiBatchPlan = new DefiBatchPlan(Guid.NewGuid(), targets, db, version, DateTimeOffset.UtcNow.AddMinutes(10));
                batchLog.Send($"DeFi batch check finished | {targets.Count} ready | {skipped.Count} skipped");
                return Ok(new { planId = _defiBatchPlan.Id, targets = targets.Select(t => new { position = t.Position, quote = t.Quote }),
                    skipped, accounts = targets.Select(t => t.Position.AccountId).Distinct().Count(),
                    gasBoostPercent = request.GasBoostPercent,
                    totalUsd = targets.Sum(t => t.Quote.ValueUsd), feeUsd = targets.Sum(t => t.Quote.FeeUsd) });
            }
        }
        catch (OperationCanceledException) { batchLog.Send("DeFi batch preview stopped; nothing was signed or sent", "WARNING"); return BadRequest(new { error = "Batch preview stopped; nothing was signed or sent" }); }
        finally { lock (DefiLock) _defiPrepare = null; }
    }
    [HttpPost("defi/batch/execute")]
    public IActionResult ExecuteDefiBatch([FromBody] Guid planId)
    {
        var check = CheckDbConnection(); if (check != null) return check;
        lock (DefiLock)
        lock (TokenSwapLock)
        {
            var plan = _defiBatchPlan;
            if (_defiScan != null || _defiPrepare != null || _defiExitRunning || SwapOperations.Count > 0 || _defiBlockingBridges > 0)
                return Conflict(new { error = "Wait for the active operation to finish" });
            if (plan == null || plan.Id != planId || plan.Expires < DateTimeOffset.UtcNow || plan.Version != _defiVersion ||
                plan.Database != _dbService.GetDb() || _defiNeedsRescan || plan.Targets.Count == 0)
                return BadRequest(new { error = "Batch preview is empty or expired; preview again" });
            if (string.IsNullOrEmpty(_pin)) return BadRequest(new { error = "Set wallet PIN first" });
            var pin = _pin; _defiBatchPlan = null; _defiExit = null; _defiExitRunning = true;
            _defiExitResult = null; DefiBatchResults.Clear(); _defiBatchTotal = plan.Targets.Count;
            var operation = RegisterSwap();
            _ = Task.Run(async () => {
                var batchLog = new Logger(true);
                batchLog.Send($"DeFi withdrawal batch started | {plan.Targets.Count} positions");
                try
                {
                    await SwapExecution.Run(operation.Cancellation.Token, async () => {
                        foreach (var target in plan.Targets)
                        {
                            SwapExecution.Check();
                            lock (DefiLock) { _defiBatchAccount = target.Position.AccountId; _defiExitResult = null; }
                            try
                            {
                                var hash = await ExecuteDefiPosition(target, pin);
                                lock (DefiLock) DefiBatchResults.Add(new { target.Position.AccountId, target.Position.Chain, status = "confirmed", hash });
                            }
                            catch (ReceiptUnavailableException ex)
                            {
                                new Logger(true, acc: target.Position.AccountId.ToString()).Send($"DeFi outcome unknown; queue stopped | {ex.Message}", "ERROR");
                                lock (DefiLock) DefiBatchResults.Add(new { target.Position.AccountId, target.Position.Chain, status = "unknown — queue stopped", error = ex.Message });
                                throw;
                            }
                            catch (OperationCanceledException)
                            {
                                lock (DefiLock) DefiBatchResults.Add(new { target.Position.AccountId, target.Position.Chain,
                                    status = "stopped", error = _defiExitResult ?? "Stopped before broadcast" });
                                throw;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                new Logger(true, acc: target.Position.AccountId.ToString()).Send($"DeFi withdrawal failed | {target.Position.Protocol} | {target.Position.Chain} | {SwapExecution.ErrorDetails(ex)}", "ERROR");
                                lock (DefiLock) DefiBatchResults.Add(new { target.Position.AccountId, target.Position.Chain, status = "failed", error = SwapExecution.ErrorDetails(ex) });
                            }
                        }
                    }, plan.Targets[0].GasPercent);
                    lock (DefiLock) _defiExitResult = "Batch finished. Scan DeFi and refresh wallet balances.";
                    batchLog.Send("DeFi withdrawal batch finished");
                }
                catch (OperationCanceledException) { lock (DefiLock) _defiExitResult = "Batch stopped. " + (_defiExitResult ?? "") + " Broadcast transactions remain on-chain."; batchLog.Send(_defiExitResult, "WARNING"); }
                catch (Exception ex) { lock (DefiLock) _defiExitResult = SwapExecution.ErrorDetails(ex); batchLog.Send(_defiExitResult, "ERROR"); }
                finally { lock (DefiLock) { _defiExitRunning = false; _defiBatchAccount = null; } FinishSwap(operation.Id); }
            });
            return Accepted(new { operationId = operation.Id, total = plan.Targets.Count });
        }
    }

    private async Task<string> ExecuteDefiPosition(ExitPlan plan, string pin)
    {
        if (!_dbService.IsConnected || _dbService.GetDb() != plan.Database) throw new InvalidOperationException("Database changed; withdrawal stopped");
        var p = plan.Position; var network = DefiVault.Network(p.Chain);
        var key = await DeFi.GetKey(plan.Database, p.AccountId, pin);
        var account = new Account(key, network.ChainId);
        if (!string.Equals(account.Address, p.Wallet, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.Database.Get("evm", "_addresses", where: $"id = {p.AccountId}"), p.Wallet, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Wallet does not match the preview");
        var log = new Logger(true, acc: p.AccountId.ToString()); var web3 = new Web3(account, network.Rpc);
        log.Send($"DeFi withdrawal started | {p.Protocol} | {p.Chain} | {p.Symbol}");
        var exact = plan.Quote.InputAmountRaw ?? plan.Quote.AmountRaw;
        var quote = await DefiWithdrawal.Prepare(web3, network.ChainId, p, plan.GasPercent, exact);
        DefiVault.ValidateRecheck(plan.Quote, quote);
        if (quote.Approval != null)
        {
            var approvalWei = System.Numerics.BigInteger.Parse(quote.Approval.Gas) * System.Numerics.BigInteger.Parse(quote.Approval.GasPrice);
            var approvalBudget = quote.FeeUsd * (decimal)approvalWei /
                (decimal)(System.Numerics.BigInteger.Parse(quote.Gas) * System.Numerics.BigInteger.Parse(quote.GasPrice) + approvalWei);
            SwapExecution.Check(); lock (DefiLock) { _defiNeedsRescan = true; PersistDefiSnapshot(); }
            var approval = quote.Approval;
            var approvalHash = await TransactionBroadcast.SendAsync(web3, new Nethereum.RPC.Eth.DTOs.TransactionInput {
                From = p.Wallet, To = approval.Destination, Data = approval.Data, Value = new Nethereum.Hex.HexTypes.HexBigInteger(0),
                Gas = new Nethereum.Hex.HexTypes.HexBigInteger(approval.Gas), GasPrice = new Nethereum.Hex.HexTypes.HexBigInteger(approval.GasPrice)
            }, network.ChainId, log);
            lock (DefiLock) _defiExitResult = $"LP approval broadcast: {approvalHash}";
            var approvalReceipt = await ReceiptWaiter.WaitAsync(() => web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(approvalHash),
                approvalHash, log, fallback: ReceiptWaiter.Fallback(network.ChainId, approvalHash));
            if (approvalReceipt.Status?.Value != 1) throw new InvalidOperationException($"LP approval reverted. TX: {approvalHash}");
            lock (DefiLock) _defiExitResult = $"LP approval confirmed: {approvalHash}; withdrawal not yet broadcast";
            log.Send($"LP approval confirmed | TX: {approvalHash}", "SUCCESS");
            SwapExecution.Check();
            try
            {
                quote = await DefiWithdrawal.Prepare(web3, network.ChainId, p, plan.GasPercent, exact);
                if (quote.Approval != null || quote.RequiresApprovalSimulation)
                    throw new InvalidOperationException("LP approval was not effective; preview again");
                DefiVault.ValidateRecheck(plan.Quote, quote, approvalBudget);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { throw new InvalidOperationException($"LP approval confirmed: {approvalHash}. Withdrawal stopped; LP remains in wallet. " + SwapExecution.ErrorDetails(ex), ex); }
        }
        SwapExecution.Check();
        lock (DefiLock) { _defiNeedsRescan = true; PersistDefiSnapshot(); }
        var hash = await TransactionBroadcast.SendAsync(web3, DefiVault.Transaction(quote, p.VaultAddress!, p.Wallet), network.ChainId, log);
        lock (DefiLock) _defiExitResult = $"Broadcast: {hash}";
        var receipt = await ReceiptWaiter.WaitAsync(() => web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(hash),
            hash, log, fallback: ReceiptWaiter.Fallback(network.ChainId, hash));
        if (receipt.Status?.Value != 1) throw new InvalidOperationException($"Withdrawal reverted. TX: {hash}");
        log.Send($"DeFi withdrawal confirmed | {p.Protocol} | TX: {hash}", "SUCCESS");
        return hash;
    }
}
