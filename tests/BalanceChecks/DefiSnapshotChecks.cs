using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Reflection;
using z3n;
using z3nSafe;
using z3nSafe.Controllers;

internal static class DefiSnapshotChecks
{
    public static void Run(Action<string, bool> check)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"defi-snapshot-{Guid.NewGuid():N}.db");
        var other = path + ".other";
        const string wallet = "0x1111111111111111111111111111111111111111";
        Db Open(string file) => new(dbMode.SQLite, sqLitePath: file);
        JObject Status(Db database) {
            var service = new DbConnectionService();
            typeof(DbConnectionService).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, database);
            var result = (OkObjectResult)new TreasuryController(service, null!).DefiStatus();
            return JObject.FromObject(result.Value!);
        }
        try
        {
            var db = Open(path); db.Query("CREATE TABLE _addresses (id INTEGER PRIMARY KEY, evm TEXT)", thrw: true);
            db.Query($"INSERT INTO _addresses VALUES (1, '{wallet}'), (2, '{wallet}')", thrw: true);
            var p = new DefiPosition("id", 1, wallet, "eth", "O'Brien", "deposit", "Q", "0.123456789123456789", null, wallet, wallet, "group");
            var saved = new DefiScanSnapshot(1, 1, 2, DateTimeOffset.UtcNow, false, false, [p],
                [new(1, wallet, "scanned"), new(2, wallet, "pending")], [new { accountId = 1, error = "test error" }]);
            db.SaveDefiSnapshot(saved);
            var loaded = Open(path).LoadDefiSnapshot()!;
            check("A new database connection restores exact positions, unknown prices, time and errors", JToken.DeepEquals(JObject.FromObject(loaded.Positions.Single()), JObject.FromObject(p)) && loaded.UpdatedAt == saved.UpdatedAt && loaded.Errors.Length == 1);
            var status = Status(Open(path));
            check("Status restores partial scan without starting a scan or withdrawal", status["positions"]!.Count() == 1 && (bool)status["cancelled"]! && !(bool)status["running"]! && !(bool)status["exitRunning"]!);
            db.SaveDefiSnapshot(saved with { NeedsRescan = true });
            check("Broadcast invalidation survives restoration", (bool)Status(Open(path))["needsRescan"]!);
            db.SaveDefiSnapshot(saved with { Positions = [p with { Type = "loan" }] }); Status(db);
            var service = new DbConnectionService();
            typeof(DbConnectionService).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, db);
            var controller = new TreasuryController(service, null!);
            var scanField = typeof(TreasuryController).GetField("_defiScan", BindingFlags.NonPublic | BindingFlags.Static)!;
            using (var scan = new CancellationTokenSource()) {
                scanField.SetValue(null, scan);
                try {
                    var result = controller.PreviewDefiExit(new() { AccountId = 1, PositionId = p.Id }).GetAwaiter().GetResult();
                    check("A scanned account passes the scan lock while other accounts remain pending", result is BadRequestObjectResult && JObject.FromObject(((BadRequestObjectResult)result).Value!)["error"]!.ToString().Contains("adapter"));
                    result = controller.PreviewDefiExit(new() { AccountId = 2, PositionId = p.Id }).GetAwaiter().GetResult();
                    check("An unscanned account remains blocked during the background scan", result is ConflictObjectResult);
                } finally { scanField.SetValue(null, null); }
            }
            db.SaveDefiSnapshot(saved with { Accounts = [new(1, wallet, "scanned"), new(2, wallet, "scanned")] }); Status(Open(path));
            typeof(TreasuryController).GetMethod("InvalidateDefiAccount", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [1]);
            var ready = typeof(TreasuryController).GetMethod("DefiAccountReady", BindingFlags.NonPublic | BindingFlags.Static)!;
            check("Invalidating one account keeps another scanned account usable", !(bool)ready.Invoke(null, [1])! && (bool)ready.Invoke(null, [2])!);
            status = Status(Open(path));
            check("Account-specific invalidation survives restart without blocking other accounts", (bool)status["needsRescan"]! && !(bool)status["requiresFullRescan"]! && !(bool)ready.Invoke(null, [1])! && (bool)ready.Invoke(null, [2])!);
            check("Switching databases never exposes another database's positions", Status(Open(other))["positions"]!.Count() == 0);
            db.Query($"UPDATE _addresses SET evm = '0x2222222222222222222222222222222222222222' WHERE id = 1", thrw: true);
            status = Status(Open(path));
            check("Changed wallet identity drops its saved positions and requires refresh", status["positions"]!.Count() == 0 && (bool)status["needsRescan"]!);
            db.SaveDefiSnapshot(saved with { Positions = [], Accounts = [], Total = 0, Processed = 0 });
            check("Empty scan atomically clears previously saved positions", Open(path).LoadDefiSnapshot()!.Positions.Length == 0);
        }
        finally { File.Delete(path); File.Delete(other); }
    }
}
