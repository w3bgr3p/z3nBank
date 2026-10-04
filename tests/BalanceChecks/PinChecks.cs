using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using z3nSafe;

static class PinChecks
{
    public static async Task Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"z3nbank-pin-{Guid.NewGuid():N}.db");
        await using var app = await ApiChecks.Start(new DbConfig { Type = "sqlite", SqlitePath = path });
        try
        {
            var db = app.Services.GetRequiredService<DbConnectionService>().GetDb();
            var key = new string('1', 64);
            const string correctPin = "верный-PIN-é-🔐";
            var encrypted = SAFU.Encode(key, correctPin, "1");
            if (string.IsNullOrEmpty(encrypted)) throw new Exception("Test wallet encryption failed");
            db.Query($"INSERT INTO _wallets (id, secp256k1) VALUES (1, '{encrypted}')", thrw: true);
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            async Task Check(string name, HttpResponseMessage response, HttpStatusCode status, string? code = null)
            {
                var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (response.StatusCode != status || code != null && (string?)body["code"] != code)
                    throw new Exception($"{name}: {response.StatusCode} {body}");
                Console.WriteLine($"PASS: {name}");
            }
            object Pin(string value) => new { pin = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) };
            await Check("Missing PIN blocks Treasury swap before work starts",
                await http.PostAsJsonAsync("/api/treasury/swap-chains", new { id = 1 }), HttpStatusCode.BadRequest, "pin_required");
            await Check("Missing PIN blocks Treasury bridge before work starts",
                await http.PostAsJsonAsync("/api/treasury/bridge-chains", new { id = 1, destination = "Base" }), HttpStatusCode.BadRequest, "pin_required");
            await Check("Wrong PIN is rejected by real encrypted-key validation",
                await http.PostAsJsonAsync("/api/treasury/pin", Pin("wrong")), HttpStatusCode.BadRequest, "invalid_pin");
            await Check("Failed PIN is never saved",
                await http.PostAsJsonAsync("/api/treasury/swap-chains", new { id = 1 }), HttpStatusCode.BadRequest, "pin_required");
            await Check("Correct PIN decrypts a real wallet key",
                await http.PostAsJsonAsync("/api/treasury/pin", Pin(correctPin)), HttpStatusCode.OK);
            await Check("Malformed PIN is rejected",
                await http.PostAsJsonAsync("/api/treasury/pin", new { pin = "%%%" }), HttpStatusCode.BadRequest, "invalid_pin");
            var other = SAFU.Encode(key, "another-pin", "2");
            db.Query($"INSERT INTO _wallets (id, secp256k1) VALUES (2, '{other}')", thrw: true);
            await Check("Stored PIN is checked against the selected wallet before signing",
                await http.PostAsJsonAsync("/api/treasury/swap-chains", new { id = 2 }), HttpStatusCode.BadRequest, "invalid_pin");
            await Check("PIN prompt validates the requested account",
                await http.PostAsJsonAsync("/api/treasury/pin", new { pin = Convert.ToBase64String(Encoding.UTF8.GetBytes("another-pin")), accountIds = new[] { 2 } }), HttpStatusCode.OK);
        }
        finally { await app.StopAsync(); if (File.Exists(path)) File.Delete(path); }
    }
}
