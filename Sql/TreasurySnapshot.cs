namespace z3n;

public partial class Db
{
    public void ReplaceTreasurySnapshot(int id, IReadOnlyDictionary<string, string> balances, bool complete = true)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var columns = GetTableColumns("_treasury");
        foreach (var name in balances.Keys)
        {
            if (!columns.Contains(name, StringComparer.Ordinal))
            {
                Query($"ALTER TABLE {Quote("_treasury")} ADD COLUMN {Quote(name)} TEXT DEFAULT ''", thrw: true);
                columns.Add(name);
            }
        }
        columns = columns.Where(c => !c.Equals("id", StringComparison.OrdinalIgnoreCase) && (complete || balances.ContainsKey(c))).ToList();
        if (columns.Count == 0) return;
        using var sql = _dbMode == dbMode.Postgre
            ? new Sql($"Host={_pgHost};Port={_pgPort};Database={_pgDbName};Username={_pgUser};Password={_pgPass};Pooling=true;")
            : new Sql(_sqLitePath, null);
        var parameters = new List<System.Data.IDbDataParameter> { sql.CreateParameter("@id", id) };
        var placeholders = new List<string> { _dbMode == dbMode.SQLite ? "?" : "@id" };
        foreach (var column in columns)
        {
            var name = $"@p{parameters.Count}";
            // An absent chain is empty in this complete successful snapshot.
            parameters.Add(sql.CreateParameter(name, balances.TryGetValue(column, out var json) ? json : "[]"));
            placeholders.Add(_dbMode == dbMode.SQLite ? "?" : name);
        }
        var fields = string.Join(", ", new[] { Quote("id") }.Concat(columns.Select(Quote)));
        var updates = string.Join(", ", columns.Select(c => $"{Quote(c)} = excluded.{Quote(c)}"));
        var query = $"INSERT INTO {Quote("_treasury")} ({fields}) VALUES ({string.Join(", ", placeholders)}) " +
                    $"ON CONFLICT ({Quote("id")}) DO UPDATE SET {updates}";
        if (sql.DbWrite(query, parameters.ToArray()) != 1)
            throw new InvalidOperationException($"Balance snapshot was not saved for account {id}.");
    }
}
