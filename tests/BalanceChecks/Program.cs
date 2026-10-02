using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using z3nSafe;
using z3n;
using System.Globalization;
using System.Net;

if (args.Length == 2 && args[0] == "--rpc-audit-live")
{
    var audit = JObject.Parse(File.ReadAllText(args[1]))["results"]!.OfType<JObject>()
        .Where(r => (bool?)r["ok"] == true && r["checks"]?["eth_blockNumber"] != null).ToArray();
    using var concurrency = new SemaphoreSlim(6);
    var failed = 0;
    await Task.WhenAll(audit.Select(async entry => {
        await concurrency.WaitAsync();
        try {
            var id = (int)entry["expectedId"]!;
            var tokens = new List<Jumper.TokenInfo> { new() { Address = "0x0000000000000000000000000000000000000000", ChainId = id, Decimals = 18, PriceUSD = "0" } };
            if ((string?)entry["token"] is string token) tokens.Add(new() { Address = token, ChainId = id, Decimals = 18, PriceUSD = "0" });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await TreasuryRpcBalances.Read(new Nethereum.Web3.Web3(Rpc.Get(id)), id,
                "0x0000000000000000000000000000000000000001", tokens, cancellation: timeout.Token);
            Console.WriteLine($"PASS: Nethereum/RPC balance reads | {entry["network"]} | {id}");
        } catch (Exception ex) { Interlocked.Increment(ref failed); Console.WriteLine($"FAIL: {entry["network"]} | {ex.Message}"); }
        finally { concurrency.Release(); }
    }));
    Console.WriteLine($"Live Nethereum audit: {audit.Length - failed}/{audit.Length}");
    Environment.ExitCode = failed == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--db-persistence")
{
    var errors = 0;
    DbPersistenceChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    Environment.ExitCode = errors == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--treasury-rpc")
{
    var errors = 0;
    await TreasuryRpcChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    Environment.ExitCode = errors == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--balance-selection")
{
    var errors = 0;
    await BalanceSelectionChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    Environment.ExitCode = errors == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--db-login-live")
{
    var service = new DbConnectionService();
    var controller = new z3nSafe.Controllers.TreasuryController(service, null!);
    var result = (Microsoft.AspNetCore.Mvc.ObjectResult)controller.ConfigureDatabase(new DbConfig {
        Type = "postgres", Host = "localhost", Port = "5432", Database = "postgres", User = "postgres", Password = Guid.NewGuid().ToString("N") });
    var body = Newtonsoft.Json.Linq.JObject.FromObject(result.Value!);
    if ((string?)body["code"] != "invalid_credentials" || service.IsConnected) throw new Exception("Expected real PostgreSQL password rejection");
    Console.WriteLine("PASS: real PostgreSQL wrong password returns readable invalid_credentials error and leaves startup disconnected");
    var loginPath = Path.Combine(AppContext.BaseDirectory, $"db-login-{Guid.NewGuid():N}.db");
    try {
        service.Connect(new DbConfig { Type = "sqlite", SqlitePath = loginPath });
        var previous = service.GetDb();
        controller.ConfigureDatabase(new DbConfig { Type = "postgres", Host = "localhost", Port = "5432", Database = "postgres", User = "postgres", Password = Guid.NewGuid().ToString("N") });
        if (service.GetDb() != previous || service.GetCurrentConfig()?.Type != "sqlite") throw new Exception("Failed settings attempt replaced the working database");
        Console.WriteLine("PASS: real password rejection preserves an existing working database connection");
    } finally { service.Disconnect(); File.Delete(loginPath); }
    return;
}

if (args.Length == 2 && args[0] == "--defi-audit-live")
{
    var positions = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(args[1]))["positions"]!.ToObject<List<DefiPosition>>()!;
    var selected = positions.Where(p => p.AccountId == 37 && p.Protocol == "Stargate" && p.Type == "locked" ||
        p.AccountId == 31 && p.Protocol == "Stargate" && p.Chain == "op" && p.Type == "locked" ||
        p.AccountId == 38 && p.Protocol == "Stargate" && p.Chain == "eth" && p.Type == "locked" ||
        p.AccountId == 38 && p.Protocol == "Compound V3" && p.Type == "deposit" ||
        p.AccountId == 31 && p.Protocol == "LFJ" && p.Type == "reward");
    foreach (var raw in selected)
    {
        var p = raw with { VaultAddress = DefiPositionsClient.PoolId(raw.VaultAddress, raw.Protocol) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await SwapExecution.Run(timeout.Token, async () => {
                var network = DefiVault.Network(p.Chain);
                var q = await DefiWithdrawal.Prepare(new Nethereum.Web3.Web3(network.Rpc), network.ChainId, p, 20);
                Console.WriteLine(JsonConvert.SerializeObject(new { p.AccountId, p.Chain, p.Protocol, p.Type, q.AmountRaw, q.FeeUsd, q.ValueUsd, q.Data, signed = false, broadcast = false }));
            }, 20);
        }
        catch (Exception ex) { Console.WriteLine(JsonConvert.SerializeObject(new { p.AccountId, p.Chain, p.Protocol, p.Type, error = SwapExecution.ErrorDetails(ex), signed = false, broadcast = false })); }
    }
    return;
}

if (args.Length == 2 && args[0] == "--defi-adapter-live")
{
    var p = JsonConvert.DeserializeObject<DefiPosition>(File.ReadAllText(args[1]))!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await SwapExecution.Run(timeout.Token, async () => {
        var network = DefiVault.Network(p.Chain);
        var quote = await DefiWithdrawal.Prepare(new Nethereum.Web3.Web3(network.Rpc), network.ChainId, p, 20);
        Console.WriteLine(JsonConvert.SerializeObject(new { quote, simulated = !quote.RequiresApprovalSimulation, signed = false, broadcast = false }));
    }, 20);
    return;
}

if (args.Length == 5 && args[0] == "--defi-position-live")
{
    using var client = new HttpClient();
    var status = Newtonsoft.Json.Linq.JObject.Parse(await client.GetStringAsync(args[1] + "/api/Treasury/defi/status"));
    var positions = status["positions"]!.ToObject<List<DefiPosition>>()!;
    var p = positions.Single(p => p.AccountId == int.Parse(args[2]) && p.Protocol == args[3] && p.Symbol == args[4] && p.Chain is "eth" or "arb" or "bsc" or "era");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await SwapExecution.Run(timeout.Token, async () => {
        var network = DefiVault.Network(p.Chain);
        var quote = await DefiWithdrawal.Prepare(new Nethereum.Web3.Web3(network.Rpc), network.ChainId, p, 20);
        Console.WriteLine(JsonConvert.SerializeObject(new { p.AccountId, p.Protocol, quote, simulated = !quote.RequiresApprovalSimulation, signed = false, broadcast = false }));
    }, 20);
    return;
}

if (args.Length == 2 && args[0] == "--defi-blast-live")
{
    var p = new DefiPosition("live", 0, args[1], "blast", "SynFutures V3", "deposit", "WETH", "unknown", null,
        DefiWithdrawal.BlastWeth, DefiWithdrawal.BlastGate, "gate");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await SwapExecution.Run(timeout.Token, async () => {
        var quote = await DefiWithdrawal.Prepare(new Nethereum.Web3.Web3(Rpc.Blast), 81457, p, 20);
        Console.WriteLine(JsonConvert.SerializeObject(new { quote.AmountRaw, quote.Symbol, quote.ValueUsd, quote.FeeUsd, quote.L1FeeUsd, quote.Gas,
            simulated = true, signed = false, broadcast = false }));
    }, 20);
    return;
}

if (args.Length == 1 && args[0] == "--defi-live")
{
    using var client = new DefiPositionsClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var positions = await client.ReadAsync(0, "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045", timeout.Token);
    Console.WriteLine($"Rabby live read: {positions.Count} assets; {positions.Select(p => p.Protocol).Distinct().Count()} protocols; {positions.Select(p => p.Chain).Distinct().Count()} networks; no API key or transaction signing.");
    if (positions.Count == 0) throw new InvalidDataException("Expected protocol positions on this public reference address");
    return;
}

if (args.Length == 1 && args[0] == "--defi-snapshot")
{
    var errors = 0;
    DefiSnapshotChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    Environment.ExitCode = errors == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--defi")
{
    var errors = 0;
    await DefiChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    await SynFuturesChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    await LendingStakingChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    await PoolExitChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    await ScrollChecks.Run((name, passed) => { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) errors++; });
    Environment.ExitCode = errors == 0 ? 0 : 1;
    return;
}

if (args.Length == 1 && args[0] == "--tokens")
{
    var tokenFailures = 0;
    TokenChecks.Run((name, passed) => {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) tokenFailures++;
    });
    SwapCostChecks.Run((name, passed) => {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) tokenFailures++;
    });
    await CancellationChecks.Run((name, passed) => {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) tokenFailures++;
    });
    await GasPricingChecks.Run((name, passed) => {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) tokenFailures++;
    });
    await BroadcastChecks.Run((name, passed) => {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) tokenFailures++;
    });
    Environment.ExitCode = tokenFailures == 0 ? 0 : 1;
    return;
}

if (args.Length == 2 && args[0] == "--serve")
{
    var config = JsonConvert.DeserializeObject<DbConfig>(File.ReadAllText(args[1]))!;
    await using var app = await ApiChecks.Start(config);
    File.WriteAllText("logs/balance-check-url.txt", app.Urls.Single());
    await Task.Delay(Timeout.Infinite);
    return;
}
if (args.Length == 2 && args[0] == "--refresh")
{
    var config = JsonConvert.DeserializeObject<DbConfig>(File.ReadAllText(args[1]))!;
    var liveDb = new Db(mode: dbMode.Postgre, pgHost: config.Host!, pgPort: config.Port,
        pgDbName: config.Database!, pgUser: config.User!, pgPass: config.Password!);
    var maxId = int.Parse(liveDb.Query("SELECT COALESCE(MAX(id), 0) FROM _addresses WHERE evm ~ '^0x[0-9a-fA-F]{40}$'", thrw: true));
    var result = await TasksDb.UpdateDb(liveDb, maxId, log: new Logger(false, http: false),
        progress: p => Console.WriteLine($"Progress: {p.Processed}/{maxId}; updated={p.Updated}; failed={p.Failed}; skipped={p.Skipped}"));
    Directory.CreateDirectory("logs");
    File.WriteAllText("logs/balance-refresh-result.json", JsonConvert.SerializeObject(result, Formatting.Indented));
    Console.WriteLine(JsonConvert.SerializeObject(result));
    Environment.ExitCode = result.Failed == 0 ? 0 : 1;
    return;
}

var failures = 0;
void Check(string name, bool passed)
{
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
    if (!passed) failures++;
}

var cached = JsonConvert.DeserializeObject<HeatmapGenerator.TokenInfo>(
    "{\"symbol\":\"USDC\",\"amount\":\"2000000\",\"decimals\":6,\"priceUSD\":\"1\",\"chainId\":1,\"address\":\"0x1\",\"ValueUSD\":999999}")!;
Check("Saved USD value must be recalculated from raw amount and price", cached.ValueUSD == 2m);
var originalCulture = CultureInfo.CurrentCulture;
CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
Check("Prices and raw units are independent of Windows locale", BalanceMath.GetValueUsd("1234567", 6, "1.25") == 1.54320875m);
Check("Comma prices cannot inflate the value", BalanceMath.GetValueUsd("1000000", 6, "1,25") == 0m);
Check("Large uint256 values are scaled before decimal conversion", BalanceMath.GetValueUsd("1000000000000000000000000000000", 18, "1") == 1000000000000m);
Check("Invalid decimals and overflow do not crash the portfolio", BalanceMath.GetValueUsd("1", 256, "1") == 0m && BalanceMath.GetValueUsd("79228162514264337593543950335", 0, "2") == 0m);
Check("Stablecoin detection uses invariant culture", BalanceMath.IsStable("0.9998"));
CultureInfo.CurrentCulture = originalCulture;

const string wallet = "0x0000000000000000000000000000000000000001";
var handler = new StubHandler();
using var http = new HttpClient(handler);
using var jumper = new Jumper(http);
handler.Body = "{\"walletAddress\":\"" + wallet + "\",\"balances\":{}}";
Check("Empty successful API response is a valid snapshot", (await jumper.GetBalances(wallet)).Balances.Count == 0);
Check("Current LI.FI endpoint is used", handler.LastUri!.StartsWith("https://li.quest/v1/wallets/"));
handler.Body = "{\"walletAddress\":\"" + wallet + "\",\"balances\":{},\"limit\":1000}";
handler.Status = HttpStatusCode.TooManyRequests;
try { await jumper.GetBalances(wallet); Check("HTTP failures cannot masquerade as zero balances", false); }
catch (HttpRequestException) { Check("HTTP failures cannot masquerade as zero balances", true); }
handler.Status = HttpStatusCode.OK;
handler.Body = "{\"walletAddress\":\"different-wallet\",\"balances\":{}}";
try { await jumper.GetBalances(wallet); Check("Wrong-wallet responses are rejected", false); }
catch (InvalidDataException) { Check("Wrong-wallet responses are rejected", true); }

var attempts = 0;
handler.Responder = request => {
    attempts++;
    var response = new HttpResponseMessage(attempts == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) {
        Content = new StringContent("{\"walletAddress\":\"" + wallet + "\",\"balances\":{}}")
    };
    if (attempts == 1) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
    return response;
};
using (var retryClient = new Jumper(http, maxRateLimitRetries: 1))
    Check("A rate-limited request is retried and then succeeds", (await retryClient.GetBalances(wallet)).Balances.Count == 0 && attempts == 2);
handler.Responder = null;

var path = Path.Combine(Path.GetTempPath(), $"z3nbank-check-{Guid.NewGuid():N}.db");
try
{
    var db = new Db(mode: dbMode.SQLite, sqLitePath: path);
    db.Query("CREATE TABLE _treasury (id INTEGER PRIMARY KEY, Ethereum TEXT, Base TEXT)", thrw: true);
    db.ReplaceTreasurySnapshot(1, new Dictionary<string, string> { ["Ethereum"] = "[{\"symbol\":\"O'Brien\",\"ValueUSD\":2}]", ["Base"] = "[{\"ValueUSD\":5}]" });
    Check("JSON commas and apostrophes survive a real SQL write", db.Get("Ethereum", "_treasury", id: 1).Contains("O'Brien"));
    db.ReplaceTreasurySnapshot(1, new Dictionary<string, string> { ["Ethereum"] = "[]" });
    Check("Absent chains are cleared atomically", db.Get("Base", "_treasury", id: 1) == "[]");
    db.ReplaceTreasurySnapshot(1, new Dictionary<string, string>());
    Check("An empty wallet clears its previous snapshot", db.Get("Ethereum", "_treasury", id: 1) == "[]");
    db.Query("CREATE TABLE _addresses (id INTEGER PRIMARY KEY, evm TEXT)", thrw: true);
    db.Query($"INSERT INTO _addresses VALUES (1, '{wallet}'), (2, ''), (3, '{wallet}')", thrw: true);
    handler.Responder = request => new HttpResponseMessage(HttpStatusCode.OK) {
        Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/chains")
            ? "{\"chains\":[{\"id\":1,\"name\":\"Ethereum\",\"key\":\"eth\"}]}"
            : "{\"walletAddress\":\"" + wallet + "\",\"balances\":{}}")
    };
    var update = await TasksDb.UpdateDb(db, 2, log: new Logger(false, http: false), client: jumper);
    Check("Updater obeys maxId and skips blank addresses", update.Updated == 1 && update.Skipped == 1 && update.Failed == 0);
    var saved = "[{\"symbol\":\"USDC\",\"amount\":\"2000000\",\"decimals\":6,\"priceUSD\":\"1\"}]";
    db.ReplaceTreasurySnapshot(1, new Dictionary<string, string> { ["Ethereum"] = saved });
    handler.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("/chains")
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"chains\":[{\"id\":1,\"name\":\"Ethereum\"}]}") }
        : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
    update = await TasksDb.UpdateDb(db, 1, log: new Logger(false, http: false), client: jumper);
    Check("A failed update preserves the real database snapshot", update.Failed == 1 && db.Get("Ethereum", "_treasury", id: 1) == saved);
}
finally { if (File.Exists(path)) File.Delete(path); }

TokenChecks.Run(Check);

await ApiChecks.Run(Check);
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class StubHandler : HttpMessageHandler
{
    public string Body = "{}";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string? LastUri;
    public Func<HttpRequestMessage, HttpResponseMessage>? Responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri!.ToString();
        return Task.FromResult(Responder?.Invoke(request) ?? new HttpResponseMessage(Status) { Content = new StringContent(Body) });
    }
}
