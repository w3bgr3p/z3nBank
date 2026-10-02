using z3n;

namespace z3nSafe;

public class DbConnectionService
{
    private Db? _db;
    private DbConfig? _config;
    private readonly object _lock = new object();
    private readonly DbConfigStore? _store;
    public string? StartupError { get; private set; }

    public DbConnectionService(DbConfigStore? store = null)
    {
        _store = store;
        if (store == null) return;
        try { _config = store.Load(); }
        catch { StartupError = "Не удалось прочитать сохранённые настройки БД. Укажите подключение заново."; return; }
        if (_config == null) return;
        try { Connect(_config, persist: false); }
        catch (Exception ex) { StartupError = DatabaseErrors.Describe(ex).Message; }
    }

    public bool IsConnected => _db != null;

    public Db GetDb()
    {
        lock (_lock)
        {
            if (_db == null)
            {
                throw new InvalidOperationException("Database not configured. Please configure database settings first.");
            }
            return _db;
        }
    }

    public bool TryGetDb(out Db? db)
    {
        lock (_lock)
        {
            db = _db;
            return _db != null;
        }
    }

    public void Connect(DbConfig config, bool persist = true)
    {
        lock (_lock)
        {
            try
            {
                if (config.UseSavedPassword)
                {
                    if (_config?.Type != "postgres" || config.Type != _config.Type ||
                        config.Host != _config.Host || config.Port != _config.Port ||
                        config.Database != _config.Database || config.User != _config.User)
                        throw new ArgumentException("Enter the password for the changed database connection");
                    config.Password = _config.Password;
                    config.UseSavedPassword = false;
                }
                Db candidate;
                if (config.Type == "sqlite")
                {
                    Console.WriteLine($"📁 Connecting to SQLite: {config.SqlitePath}");
                    
                    string path = config.SqlitePath;
                    if (!Path.IsPathRooted(path)) 
                    {
                        path = Path.Combine(AppContext.BaseDirectory, path);
                    }
        

                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                    config.SqlitePath = path; 
                    
                    
                    candidate = new Db(mode: dbMode.SQLite, sqLitePath: config.SqlitePath);
                }
                else if (config.Type == "postgres")
                {
                    Console.WriteLine($"🐘 Connecting to PostgreSQL: {config.Host}:{config.Port}/{config.Database}");
                    candidate = new Db(
                        mode: dbMode.Postgre,
                        pgHost: config.Host,
                        pgPort: config.Port,
                        pgDbName: config.Database,
                        pgUser: config.User,
                        pgPass: config.Password
                    );
                }
                else
                {
                    throw new ArgumentException($"Unsupported database type: {config.Type}");
                }
                DBuilder.ImportDbStructure(candidate);
                if (persist) _store?.Save(config);
                _db = candidate;
                _config = config;
                StartupError = null;
                Console.WriteLine("✅ Database connected successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database connection failed: {ex.Message}");
                throw;
            }
        }
    }

    public DbConfig? GetCurrentConfig()
    {
        lock (_lock)
        {
            return _config;
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _db = null;
            _config = null;
            Console.WriteLine("🔌 Database disconnected");
        }
    }
}

public class DbConfig
{
    public bool UseSavedPassword { get; set; }
    public string Type { get; set; } = ""; // "sqlite" or "postgres"
    
    // SQLite
    public string? SqlitePath { get; set; }
    
    // PostgreSQL
    public string? Host { get; set; }
    public string Port { get; set; } = "5432";
    public string? Database { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
}
