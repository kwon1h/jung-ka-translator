using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameOverlayTranslator.App.Services;

public sealed class ApiKeyStore
{
    private readonly string keyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "deepl-free-auth.key");

    public string? Load()
    {
        if (!File.Exists(keyPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(keyPath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }

    public void Save(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(keyPath, protectedBytes);
    }

    public void Delete()
    {
        if (File.Exists(keyPath))
        {
            File.Delete(keyPath);
        }
    }
}
