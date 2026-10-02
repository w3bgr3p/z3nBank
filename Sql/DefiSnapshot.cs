using Newtonsoft.Json;
using z3nSafe;

namespace z3n;

public partial class Db
{
    private void EnsureDefiSnapshot() => Query("CREATE TABLE IF NOT EXISTS \"_defi_snapshot\" (\"id\" INTEGER PRIMARY KEY, \"payload\" TEXT NOT NULL)", thrw: true);

    public void SaveDefiSnapshot(DefiScanSnapshot snapshot)
    {
        EnsureDefiSnapshot();
        using var sql = _dbMode == dbMode.Postgre
            ? new Sql($"Host={_pgHost};Port={_pgPort};Database={_pgDbName};Username={_pgUser};Password={_pgPass};Pooling=true;")
            : new Sql(_sqLitePath, null);
        var value = sql.CreateParameter("@payload", JsonConvert.SerializeObject(snapshot));
        var placeholder = _dbMode == dbMode.SQLite ? "?" : "@payload";
        if (sql.DbWrite($"INSERT INTO \"_defi_snapshot\" (\"id\", \"payload\") VALUES (1, {placeholder}) ON CONFLICT (\"id\") DO UPDATE SET \"payload\" = excluded.\"payload\"", [value]) != 1)
            throw new InvalidOperationException("DeFi snapshot was not saved");
    }

    public DefiScanSnapshot? LoadDefiSnapshot()
    {
        EnsureDefiSnapshot();
        var json = Query("SELECT \"payload\" FROM \"_defi_snapshot\" WHERE \"id\" = 1", thrw: true);
        if (string.IsNullOrEmpty(json)) return null;
        var snapshot = JsonConvert.DeserializeObject<DefiScanSnapshot>(json);
        if (snapshot?.Version != 1 || snapshot.Positions == null || snapshot.Accounts == null || snapshot.Errors == null)
            throw new InvalidDataException("Unsupported or incomplete DeFi snapshot");
        return snapshot;
    }
}
