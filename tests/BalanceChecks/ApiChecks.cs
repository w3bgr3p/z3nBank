using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using z3nSafe;
using z3nSafe.Controllers;

static class ApiChecks
{
    public static async Task<WebApplication> Start(DbConfig config)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = Path.GetFullPath("wwwroot") });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(TreasuryController).Assembly);
        var service = new DbConnectionService();
        service.Connect(config);
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<LogService>();
        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        await app.StartAsync();
        Console.WriteLine($"API checks URL: {app.Urls.Single()}");
        return app;
    }

    public static async Task Run(Action<string, bool> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"z3nbank-api-{Guid.NewGuid():N}.db");
        await using var app = await Start(new DbConfig { Type = "sqlite", SqlitePath = path });
        try
        {
            var db = app.Services.GetRequiredService<DbConnectionService>().GetDb();
            db.Query("INSERT INTO _addresses (id, evm) VALUES (1, '0x0000000000000000000000000000000000000001')", thrw: true);
            db.ReplaceTreasurySnapshot(1, new Dictionary<string, string> {
                ["Ethereum"] = "[{\"symbol\":\"USDC\",\"amount\":\"2000000\",\"decimals\":6,\"priceUSD\":\"1\",\"chainId\":1,\"address\":\"0x1\",\"ValueUSD\":999999}]",
                ["Base"] = "[{\"symbol\":\"ETH\",\"amount\":\"1000000000000000\",\"decimals\":18,\"priceUSD\":\"2000\",\"chainId\":8453,\"address\":\"0x0\",\"ValueUSD\":999999}]"
            });
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var data = JArray.Parse(await http.GetStringAsync("/api/treasury/data?maxId=1"));
            var stats = JObject.Parse(await http.GetStringAsync("/api/treasury/stats?maxId=1"));
            var details = JObject.Parse(await http.GetStringAsync("/api/treasury/account/1"));
            check("Real HTTP data, stats, and details agree on recalculated USD", (decimal)stats["totalValue"]! == 4m && (decimal)details["totalValue"]! == 4m && (decimal)data[0]["chainData"]!["Ethereum"]![0]["valueUSD"]! == 2m);
            var filtered = JArray.Parse(await http.GetStringAsync("/api/treasury/data?maxId=1&chains=Base"));
            check("HTTP chain filtering includes only the selected chain", ((JObject)filtered[0]["chainData"]!).Count == 1 && filtered[0]["chainData"]!["Base"] != null);
            using var invalid = await http.PostAsync("/api/treasury/update?maxId=0", null);
            check("Invalid update range is rejected before work starts", invalid.StatusCode == System.Net.HttpStatusCode.BadRequest);
            db.ReplaceTreasurySnapshot(1, new Dictionary<string, string>());
            stats = JObject.Parse(await http.GetStringAsync("/api/treasury/stats?maxId=1"));
            check("A cleared real database reports zero total and active accounts", (decimal)stats["totalValue"]! == 0 && (int)stats["activeAccounts"]! == 0);
        }
        finally
        {
            await app.StopAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
