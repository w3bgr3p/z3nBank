using System.Net;
using z3n;
using z3nSafe;

internal static class BalanceSelectionChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var chains = request.RequestUri!.AbsolutePath.EndsWith("/chains");
            if (!chains) Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(chains
                ? "{\"chains\":[{\"id\":1,\"name\":\"Ethereum\",\"key\":\"eth\"}]}"
                : "{\"walletAddress\":\"0x1111111111111111111111111111111111111111\",\"balances\":{}}") });
        }
    }
    public static async Task Run(Action<string, bool> check)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"balance-selection-{Guid.NewGuid():N}.db");
        try {
            var db = new Db(dbMode.SQLite, sqLitePath: path);
            db.Query("CREATE TABLE _addresses (id INTEGER PRIMARY KEY, evm TEXT)", thrw: true);
            db.Query("CREATE TABLE _treasury (id INTEGER PRIMARY KEY, Ethereum TEXT)", thrw: true);
            for (var id = 1; id <= 3; id++) {
                db.Query($"INSERT INTO _addresses VALUES ({id}, '0x1111111111111111111111111111111111111111')", thrw: true);
                db.ReplaceTreasurySnapshot(id, new Dictionary<string, string> { ["Ethereum"] = "saved" });
            }
            var handler = new Handler(); using var http = new HttpClient(handler); using var client = new Jumper(http);
            var result = await TasksDb.UpdateDb(db, 4, client: client, log: new Logger(false, http: false), accountIds: [3, 3]);
            check("Selected account alone is fetched and saved; duplicate selection runs once", handler.Calls == 1 && result.Processed == 1 && result.CurrentId == 3 && result.Updated == 1);
            check("Unselected balance snapshots remain unchanged", db.Get("Ethereum", "_treasury", id: 1) == "saved" && db.Get("Ethereum", "_treasury", id: 2) == "saved" && db.Get("Ethereum", "_treasury", id: 3) == "[]");
            result = await TasksDb.UpdateDb(db, 4, client: client, log: new Logger(false, http: false), accountIds: [4]);
            check("Missing selected account does not fall back to all accounts", handler.Calls == 1 && result.Skipped == 1 && result.Updated == 0);
            result = await TasksDb.UpdateDb(db, 3, client: client, log: new Logger(false, http: false), accountIds: []);
            check("Empty selection updates the entire Max ID range", handler.Calls == 4 && result.Processed == 3 && result.Updated == 3 && db.Get("Ethereum", "_treasury", id: 1) == "[]" && db.Get("Ethereum", "_treasury", id: 2) == "[]");
            check("Absent selection preserves the same all-account behavior", TasksDb.BalanceUpdateAccounts(3, null).SequenceEqual(new[] { 1, 2, 3 }));
            var rejected = false; try { TasksDb.BalanceUpdateAccounts(3, [4]); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check("Out-of-range selection is rejected instead of widening the update", rejected);
        } finally { File.Delete(path); }
    }
}
