using z3n;

namespace z3nSafe.Controllers;

public partial class TreasuryController
{
    // Called under DefiLock. Plans and transaction tasks are never persisted or resumed.
    private static void PersistDefiSnapshot()
    {
        _defiUpdated = DateTimeOffset.UtcNow;
        _defiDb!.SaveDefiSnapshot(new DefiScanSnapshot(1, _defiDone, _defiTotal, _defiUpdated,
            _defiCancelled, _defiNeedsRescan, DefiRows.ToArray(), DefiAccounts.Values.ToArray(), DefiErrors.ToArray()));
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
            _defiDone = DefiAccounts.Values.Count(a => a.Status != "pending"); _defiTotal = DefiAccounts.Count;
            _defiUpdated = snapshot.UpdatedAt;
            _defiCancelled = snapshot.Cancelled || DefiAccounts.Values.Any(a => a.Status == "pending");
            _defiNeedsRescan = snapshot.NeedsRescan || DefiAccounts.Count != snapshot.Accounts.Length;
        }
        _defiDb = db;
    }
}
