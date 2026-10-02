using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace z3nSafe;

public sealed class DbConfigStore
{
    private readonly string _path;
    public DbConfigStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "z3nBank", "database.config");

    public DbConfig? Load()
    {
        if (!File.Exists(_path)) return null;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
        return JsonConvert.DeserializeObject<DbConfig>(Encoding.UTF8.GetString(bytes))
            ?? throw new InvalidDataException("Saved database configuration is empty");
    }

    public void Save(DbConfig config)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(config)), null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, _path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
