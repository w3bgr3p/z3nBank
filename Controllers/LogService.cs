using System.Text.Json;
using System.Text;

public class LogService
{
    private readonly string _logPath;
    private readonly string _httpLogPath;
    private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

    public LogService()
    {
        _logPath = Path.Combine(AppContext.BaseDirectory, "logs");
        _httpLogPath = Path.Combine(AppContext.BaseDirectory, "http-logs");
        
        if (!Directory.Exists(_logPath)) Directory.CreateDirectory(_logPath);
        if (!Directory.Exists(_httpLogPath)) Directory.CreateDirectory(_httpLogPath);
    }

    public async Task SaveLog(string json) 
    {
        await _fileLock.WaitAsync();
        try {
            string filePath = Path.Combine(_logPath, "current.jsonl");
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 100 * 1024 * 1024) {
                File.Move(filePath, Path.Combine(_logPath, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl"));
            }
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine);
        } finally { _fileLock.Release(); }
    }
    public async Task<List<object>> ReadLogs(int limit, string? level, string? machine, string? project, string? session, string? port, string? pid, string? account) {
        var result = new List<object>();
        var files = Directory.GetFiles(_logPath, "*.jsonl")
            .OrderByDescending(File.GetCreationTime)
            .Take(5);

        foreach (var file in files) {
            var lines = (await File.ReadAllLinesAsync(file)).Reverse();
            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try {
                    var log = JsonSerializer.Deserialize<JsonElement>(line);
                    
                    // Фильтрация
                    if (!string.IsNullOrEmpty(level) && !log.GetProperty("level").ToString().Equals(level, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(machine) && !log.GetProperty("machine").ToString().Contains(machine, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(project) && !log.GetProperty("project").ToString().Contains(project, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(session)) {
                        if (!log.TryGetProperty("session", out var sessionProp) || 
                            !sessionProp.ToString().Contains(session, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!string.IsNullOrEmpty(port)) {
                        if (!log.TryGetProperty("port", out var portProp) || 
                            !portProp.ToString().Contains(port, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!string.IsNullOrEmpty(pid)) {
                        if (!log.TryGetProperty("pid", out var pidProp) || 
                            !pidProp.ToString().Contains(pid, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!string.IsNullOrEmpty(account)) {
                        if (!log.TryGetProperty("account", out var accProp) || 
                            !accProp.ToString().Contains(account, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    result.Add(log);
                    if (result.Count >= limit) return result;
                } catch { continue; }
            }
        }
        return result;
    }
    private async Task<object> GetStats()
    {
        var logs = await ReadLogs(2000, null, null, null, null, null, null,null); 
    
        var total = logs.Count;
        var levels = new Dictionary<string, int>();
        var machines = new Dictionary<string, int>();
        var projects = new Dictionary<string, int>();
        var sessions = new Dictionary<string, int>();
        var ports = new Dictionary<string, int>();
        var pids = new Dictionary<string, int>();
        var accounts = new Dictionary<string, int>();
        
        foreach (JsonElement log in logs)
        {
            try {
                string lvl = log.TryGetProperty("level", out var l) ? l.ToString() : "UNKNOWN";
                string mch = log.TryGetProperty("machine", out var m) ? m.ToString() : "UNKNOWN";
                string prj = log.TryGetProperty("project", out var p) ? p.ToString() : "UNKNOWN";
                string sess = log.TryGetProperty("session", out var s) ? s.ToString() : "0";
                string prt = log.TryGetProperty("port", out var pt) ? pt.ToString() : "UNKNOWN";
                string pd = log.TryGetProperty("pid", out var pi) ? pi.ToString() : "UNKNOWN";
                string acc = log.TryGetProperty("account", out var a) ? a.ToString() : "";

                levels[lvl] = levels.GetValueOrDefault(lvl) + 1;
                machines[mch] = machines.GetValueOrDefault(mch) + 1;
                projects[prj] = projects.GetValueOrDefault(prj) + 1;
                sessions[sess] = sessions.GetValueOrDefault(sess) + 1;
                ports[prt] = ports.GetValueOrDefault(prt) + 1;
                pids[pd] = pids.GetValueOrDefault(pd) + 1;
                if (!string.IsNullOrEmpty(acc)) 
                    accounts[acc] = accounts.GetValueOrDefault(acc) + 1;
            } catch { continue; }
        }

        return new {
            totalLogs = total,
            byLevel = levels,
            byMachine = machines,
            byProject = projects,
            bySession = sessions,
            byPort = ports,
            byPid = pids,
            byAccount = accounts
        };
    }
    
    private async Task<List<object>> ReadHttpLogs(int limit, string method, string url, string status, string machine, string project, string session, string account, string cookiesSource) 
    {
        var result = new List<object>();
        var files = Directory.GetFiles(_httpLogPath, "*.jsonl")
            .OrderByDescending(File.GetCreationTime)
            .Take(5);

        foreach (var file in files) 
        {
            var lines = (await File.ReadAllLinesAsync(file)).Reverse();
            foreach (var line in lines) 
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try 
                {
                    var log = JsonSerializer.Deserialize<JsonElement>(line);
                    
                    // Фильтрация
                    if (!string.IsNullOrEmpty(method) && 
                        !log.GetProperty("method").ToString().Equals(method, StringComparison.OrdinalIgnoreCase)) 
                        continue;
                        
                    if (!string.IsNullOrEmpty(url) && 
                        !log.GetProperty("url").ToString().Contains(url, StringComparison.OrdinalIgnoreCase)) 
                        continue;
                        
                    if (!string.IsNullOrEmpty(status))
                    {
                        string statusStr = log.GetProperty("statusCode").ToString();
                        if (!statusStr.StartsWith(status)) continue;
                    }
                    
                    if (!string.IsNullOrEmpty(machine) && 
                        !log.GetProperty("machine").ToString().Contains(machine, StringComparison.OrdinalIgnoreCase)) 
                        continue;
                        
                    if (!string.IsNullOrEmpty(project) && 
                        !log.GetProperty("project").ToString().Contains(project, StringComparison.OrdinalIgnoreCase)) 
                        continue;
                        
                    if (!string.IsNullOrEmpty(session)) 
                    {
                        if (!log.TryGetProperty("session", out var sessionProp) || 
                            !sessionProp.ToString().Equals(session, StringComparison.OrdinalIgnoreCase)) 
                            continue;
                    }
                    
                    if (!string.IsNullOrEmpty(account)) 
                    {
                        if (!log.TryGetProperty("account", out var accProp) || 
                            !accProp.ToString().Equals(account, StringComparison.OrdinalIgnoreCase)) 
                            continue;
                    }
                    
                    if (!string.IsNullOrEmpty(cookiesSource)) 
                    {
                        if (!log.TryGetProperty("request", out var req) || 
                            !req.TryGetProperty("cookiesSource", out var csProp) || 
                            !csProp.ToString().Contains(cookiesSource, StringComparison.OrdinalIgnoreCase)) 
                            continue;
                    }

                    result.Add(log);
                    if (result.Count >= limit) return result;
                } 
                catch { continue; }
            }
        }
        return result;
    }
        
    public async Task SaveHttpLog(string json) 
    {
        await _fileLock.WaitAsync();
        try 
        {
            string filePath = Path.Combine(_httpLogPath, "http-current.jsonl");
        
            // Ротация при 50MB (меньше чем обычные логи)
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 50 * 1024 * 1024) 
            {
                File.Move(filePath, Path.Combine(_httpLogPath, $"http-log_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl"));
            }

            await File.AppendAllTextAsync(filePath, json + Environment.NewLine);
        } 
        finally 
        { 
            _fileLock.Release(); 
        }
    }
    public void ClearAllLogs()
    {
        string logPath = Path.Combine(AppContext.BaseDirectory, "logs");
        string httpLogPath = Path.Combine(AppContext.BaseDirectory, "http-logs");

        if (Directory.Exists(logPath))
        {
            foreach (var file in Directory.GetFiles(logPath)) 
                System.IO.File.Delete(file);
        }

        if (Directory.Exists(httpLogPath))
        {
            foreach (var file in Directory.GetFiles(httpLogPath)) 
                System.IO.File.Delete(file);
        }
    }

    
    // Не забудь заменить использование JsonSerializer на стандартный System.Text.Json
}