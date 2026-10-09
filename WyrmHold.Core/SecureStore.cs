using System.Security.Cryptography;
using System.Text;

namespace Wyrmhold.Core;

public static class SecureStore
{
    private static readonly string Folder = Path.Combine(AppPaths.DataFolder, "tokens");

    public static void Save(string name, string secret)
    {
        Directory.CreateDirectory(Folder);

        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(GetPath(name), encrypted);
    }

    public static string? Load(string name)
    {
        string path = GetPath(name);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] decrypted = ProtectedData.Unprotect(
                File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Toutes les valeurs enregistrées (déchiffrées) : sert seulement à les masquer dans un rapport de bug.
    public static IEnumerable<string> LoadAll()
    {
        if (!Directory.Exists(Folder))
        {
            yield break;
        }

        foreach (string path in Directory.GetFiles(Folder, "*.bin"))
        {
            string? value = Load(Path.GetFileNameWithoutExtension(path));

            if (!string.IsNullOrEmpty(value))
            {
                yield return value;
            }
        }
    }

    public static bool Exists(string name)
    {
        return File.Exists(GetPath(name));
    }

    public static void Delete(string name)
    {
        string path = GetPath(name);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetPath(string name)
    {
        return Path.Combine(Folder, name + ".bin");
    }
}