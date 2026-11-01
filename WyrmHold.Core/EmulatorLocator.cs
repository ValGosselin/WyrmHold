using Microsoft.Win32;

namespace Wyrmhold.Core;

/// <summary>
/// Cherche le programme d'un émulateur sans fouiller tout le disque : beaucoup d'émulateurs se dézippent
/// n'importe où et n'ont pas d'installateur. Dans l'ordre :
/// 1. les dossiers habituels ;
/// 2. la liste des applications installées de Windows ;
/// 3. MuiCache : Windows y note chaque programme lancé par l'utilisateur, même dézippé dans Téléchargements.
/// Rien trouvé = l'utilisateur choisit lui-même le fichier (Comptes → Émulateurs).
/// </summary>
internal static class EmulatorLocator
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string MuiCacheKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    private const string MuiCacheSuffix = ".FriendlyAppName";

    public static string? Find(string executableName, string displayName, IEnumerable<string> knownFolders)
    {
        return FindIn(executableName, knownFolders)
            ?? FindIn(executableName, FindUninstallFolders(displayName))
            ?? FindInMuiCache(executableName);
    }

    private static string? FindIn(string executableName, IEnumerable<string> folders)
    {
        return folders
            .Select(folder => Path.Combine(folder, executableName))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindUninstallFolders(string displayName)
    {
        List<RegistryKey> roots = RegistryHelper.OpenLocalMachineKeys(UninstallKeyPath);

        if (Registry.CurrentUser.OpenSubKey(UninstallKeyPath) is RegistryKey userKey)
        {
            roots.Add(userKey);
        }

        List<string> folders = new List<string>();

        foreach (RegistryKey root in roots)
        {
            using (root)
            {
                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    using RegistryKey? entry = root.OpenSubKey(subKeyName);

                    if (entry?.GetValue("DisplayName") is not string name
                        || !name.Contains(displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (entry.GetValue("InstallLocation") is string { Length: > 0 } location)
                    {
                        folders.Add(location.Trim('"'));
                    }

                    // RetroArch ne remplit pas InstallLocation : son désinstalleur est à côté du programme.
                    if (entry.GetValue("UninstallString") is string uninstall
                        && uninstall.Trim('"').Split(".exe", StringSplitOptions.None)[0] is string program
                        && Path.GetDirectoryName(program.Trim('"')) is string folder)
                    {
                        folders.Add(folder);
                    }
                }
            }
        }

        return folders;
    }

    private static string? FindInMuiCache(string executableName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(MuiCacheKeyPath);

        if (key is null)
        {
            return null;
        }

        // Plusieurs copies possibles : la plus récente sur le disque.
        return key.GetValueNames()
            .Where(name => name.EndsWith(MuiCacheSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name.Substring(0, name.Length - MuiCacheSuffix.Length))
            .Where(path => string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
