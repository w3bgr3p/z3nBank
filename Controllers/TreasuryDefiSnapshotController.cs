using z3n;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    private static readonly Dictionary<string, DefiPosition> PendingDefiWithdrawals = new(StringComparer.OrdinalIgnoreCase);
    private static string PendingKey(DefiPosition p) => $"{p.AccountId}:{p.Wallet}:{p.Chain}:{p.Protocol}:{p.VaultAddress}:{(p.Type == "reward" ? "reward:" + p.AssetAddress : "withdraw")}";
    private static string PendingAmount(DefiVault.Quote quote)
    {
        var raw = quote.AmountRaw.PadLeft(quote.Decimals + 1, '0');
        return quote.Decimals == 0 ? raw : (raw[..^quote.Decimals] + "." + raw[^quote.Decimals..]).TrimEnd('0').TrimEnd('.');
    }
    private static void MergePendingDefi(List<DefiPosition> rows, int accountId, string wallet)
    {
        foreach (var pending in PendingDefiWithdrawals.Values.Where(p => p.AccountId == accountId && p.Wallet.Equals(wallet, StringComparison.OrdinalIgnoreCase))) {
            rows.RemoveAll(p => PendingKey(p).Equals(PendingKey(pending), StringComparison.OrdinalIgnoreCase));
            rows.Add(pending);
        }
    }
    private static async Task ReconcilePendingDefi(int accountId, string wallet, Db db, CancellationToken token)
    {
        DefiPosition[] pending;
        lock (DefiLock) pending = PendingDefiWithdrawals.Values.Where(p => p.AccountId == accountId && p.Wallet.Equals(wallet, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var position in pending) {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try {
                var network = DefiVault.Network(position.Chain); bool exists = true;
                await SwapExecution.Run(timeout.Token, async () => exists = await DefiPending.Exists(new Nethereum.Web3.Web3(network.Rpc), position));
                if (!exists) lock (DefiLock) {
                    if (_defiDb == db && PendingDefiWithdrawals.TryGetValue(PendingKey(position), out var current) && current == position)
                        PendingDefiWithdrawals.Remove(PendingKey(position));
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested) {
                new Logger(true, acc: accountId.ToString()).Send($"Pending DeFi request check failed; saved request kept | {position.Protocol} | {SwapExecution.ErrorDetails(ex)}", "WARNING");
            }
        }
    }
    // Called under DefiLock. Plans and transaction tasks are never persisted or resumed.
    private static void PersistDefiSnapshot()
    {
        _defiUpdated = DateTimeOffset.UtcNow;
        _defiDb!.SaveDefiSnapshot(new DefiScanSnapshot(1, _defiDone, _defiTotal, _defiUpdated,
            _defiCancelled, _defiNeedsRescan, DefiRows.ToArray(), DefiAccounts.Values.ToArray(), DefiErrors.ToArray(), PendingDefiWithdrawals.Values.ToArray()));
    }

    private static void SaveDefiProgress()
    {
        try { PersistDefiSnapshot(); }
        catch (Exception ex) { new Logger(true).Send($"DeFi snapshot save failed | {ex.Message}", "ERROR"); }
    }

    private static void RestoreDefiSnapshot(Db db)
    {
        var snapshot = db.LoadDefiSnapshot();
        DefiRows.Clear(); DefiAccounts.Clear(); DefiErrors.Clear();
        PendingDefiWithdrawals.Clear();
        _defiExit = null; _defiBatchPlan = null; _defiExitResult = null; DefiBatchResults.Clear();
        _defiVersion++; _defiDone = 0; _defiTotal = 0; _defiUpdated = null;
        _defiCancelled = false; _defiNeedsRescan = false;
        if (snapshot != null)
        {
            foreach (var account in snapshot.Accounts)
            {
                if (account.Id <= 0 || !string.Equals(db.Get("evm", "_addresses", where: $"id = {account.Id}"), account.Address, StringComparison.OrdinalIgnoreCase)) continue;
                DefiAccounts[account.Id] = account;
            }
            DefiRows.AddRange(snapshot.Positions.Where(p => DefiAccounts.TryGetValue(p.AccountId, out var a) && string.Equals(a.Address, p.Wallet, StringComparison.OrdinalIgnoreCase)));
            DefiErrors.AddRange(snapshot.Errors);
            foreach (var pending in snapshot.PendingWithdrawals ?? [])
                if (DefiAccounts.TryGetValue(pending.AccountId, out var owner) && owner.Address.Equals(pending.Wallet, StringComparison.OrdinalIgnoreCase))
                    PendingDefiWithdrawals[PendingKey(pending)] = pending;
            _defiDone = DefiAccounts.Values.Count(a => a.Status != "pending"); _defiTotal = DefiAccounts.Count;
            _defiUpdated = snapshot.UpdatedAt;
            _defiCancelled = snapshot.Cancelled || DefiAccounts.Values.Any(a => a.Status == "pending");
            _defiNeedsRescan = snapshot.NeedsRescan || DefiAccounts.Count != snapshot.Accounts.Length;
        }
        _defiDb = db;
    }
}
