using z3n;

namespace z3nSafe;

public class DbConnectionService
{
    private Db? _db;
    private DbConfig? _config;
    private readonly object _lock = new object();

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

    public void Connect(DbConfig config)
    {
        lock (_lock)
        {
            try
            {
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
                    
                    
                    _db = new Db(mode: dbMode.SQLite, sqLitePath: config.SqlitePath);
                }
                else if (config.Type == "postgres")
                {
                    Console.WriteLine($"🐘 Connecting to PostgreSQL: {config.Host}:{config.Port}/{config.Database}");
                    _db = new Db(
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
                DBuilder.ImportDbStructure(_db);
                _config = config;
                Console.WriteLine("✅ Database connected successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database connection failed: {ex.Message}");
                _db = null;
                _config = null;
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