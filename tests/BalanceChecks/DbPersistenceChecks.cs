using Newtonsoft.Json.Linq;
using z3nSafe;

internal static class DbPersistenceChecks
{
    public static void Run(Action<string, bool> check)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "db-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "database.config");
            var store = new DbConfigStore(settingsPath);
            check("First launch without saved settings remains disconnected", !new DbConnectionService(store).IsConnected);
            var secret = "test-only-password-" + Guid.NewGuid().ToString("N");
            store.Save(new DbConfig { Type = "postgres", Host = "localhost", Database = "test", User = "test", Password = secret });
            check("Windows DPAPI round trip preserves credentials", store.Load()?.Password == secret);
            check("Saved configuration contains no plaintext password or database fields", !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(settingsPath)).Contains(secret) && !File.ReadAllText(settingsPath).Contains("localhost"));
            store.Save(new DbConfig { Type = "invalid", Host = "saved-host", Password = secret });
            var rejected = new DbConnectionService(store);
            check("Failed automatic connection keeps settings and exposes an error", !rejected.IsConnected && !string.IsNullOrEmpty(rejected.StartupError) && rejected.GetCurrentConfig()?.Host == "saved-host");
            var response = (Microsoft.AspNetCore.Mvc.OkObjectResult)new z3nSafe.Controllers.TreasuryController(rejected, null!).GetDbConfig();
            var publicConfig = JObject.FromObject(response.Value!);
            check("Settings API never returns the saved password", publicConfig["password"] == null && !publicConfig.ToString().Contains(secret));
            var service = new DbConnectionService(store);
            service.Connect(new DbConfig { Type = "sqlite", SqlitePath = Path.Combine(directory, "wallet.db") });
            var saved = File.ReadAllBytes(settingsPath);
            check("Successful connection is saved and restored by a fresh service", new DbConnectionService(store).IsConnected);
            try { service.Connect(new DbConfig { Type = "invalid" }); } catch (ArgumentException) { }
            check("Rejected new settings preserve working connection and saved file", service.IsConnected && File.ReadAllBytes(settingsPath).SequenceEqual(saved));
            File.WriteAllBytes(settingsPath, [1, 2, 3]);
            rejected = new DbConnectionService(store);
            check("Corrupt settings show a configuration error instead of preventing startup", !rejected.IsConnected && !string.IsNullOrEmpty(rejected.StartupError));
            check("Optimism RPC resolves by LI.FI name and by Chain ID", Rpc.Get("OP Mainnet") == Rpc.Optimism && Rpc.Get(10) == Rpc.Optimism);
            check("Other LI.FI network names resolve to configured RPCs", Rpc.Get("Arbitrum One") == Rpc.Arbitrum && Rpc.Get("zkSync Era") == Rpc.Zksync && Rpc.Get("Manta Pacific") == Rpc.Manta);
            check("Celo balances have a configured RPC", Rpc.Get(42220) == Rpc.Get("Celo"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
