using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Wyrmhold.Core;

/// <summary>
/// Comment désinstaller un jeu : Wyrmhold ne supprime JAMAIS de fichier lui-même, il lance le désinstalleur
/// officiel (comme « Paramètres → Applis » de Windows), pour que le launcher reste au courant.
/// </summary>
/// <param name="Kind">Ce que fera le bouton.</param>
/// <param name="FileName">Le programme (ou l'adresse steam://) à lancer.</param>
/// <param name="Arguments">Ses paramètres.</param>
public record UninstallPlan(UninstallKind Kind, string FileName, string Arguments);

public enum UninstallKind
{
    // Le désinstalleur officiel est lancé (Steam, Ubisoft, EA, Battle.net, GOG…).
    Uninstaller,

    // Pas de commande vérifiée (Epic) : on ouvre le launcher, la désinstallation se fait chez lui.
    OpenLauncher
}

/// <summary>
/// Trouve et lance le désinstalleur d'un jeu. Méthodes vérifiées le 10 octobre 2026 sur le PC de Val :
/// - Steam : « steam://uninstall/&lt;appid&gt; » (la commande que Steam écrit lui-même dans la liste de Windows) ;
///   Steam n'inscrit pas tous ses jeux dans cette liste (ex. bibliothèque sur D:\), d'où la commande directe ;
/// - Ubisoft, EA, Battle.net, GOG : l'entrée de la liste des applications installées de Windows (registre
///   « Uninstall »), retrouvée par le dossier d'installation du jeu (ex. Ubisoft : upc.exe uplay://uninstall/16382) ;
/// - Epic n'inscrit pas ses jeux et sa commande de désinstallation n'est pas documentée : on ouvre son launcher.
/// </summary>
public static class GameUninstaller
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Comment désinstaller ce jeu, ou null si aucune méthode n'a été trouvée.</summary>
    public static UninstallPlan? FindPlan(Game game)
    {
        if (!game.IsInstalled)
        {
            return null;
        }

        if (game.Platform == Platform.Steam)
        {
            return new UninstallPlan(UninstallKind.Uninstaller, $"steam://uninstall/{game.PlatformGameId}", "");
        }

        if (game.Platform == Platform.Epic)
        {
            return FindEpicLauncher() is string launcher
                ? new UninstallPlan(UninstallKind.OpenLauncher, launcher, "")
                : null;
        }

        return string.IsNullOrEmpty(game.InstallPath) ? null : FindWindowsUninstaller(game.InstallPath);
    }

    /// <summary>
    /// Lance le plan. Renvoie false si tu as refusé la demande d'autorisation de Windows (certains désinstalleurs,
    /// comme ceux d'EA ou de Battle.net, demandent les droits d'administrateur).
    /// </summary>
    public static bool Run(UninstallPlan plan)
    {
        try
        {
            // UseShellExecute : comme un double-clic. Windows trouve le programme d'une adresse steam://,
            // et affiche lui-même la demande d'autorisation (UAC) si le désinstalleur en a besoin.
            Process.Start(new ProcessStartInfo(plan.FileName, plan.Arguments) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223 = « opération annulée par l'utilisateur » (bouton Non de la demande d'autorisation).
            return false;
        }
    }

    // ----- Liste des applications installées de Windows -----

    private static UninstallPlan? FindWindowsUninstaller(string installPath)
    {
        string target = CleanFolder(installPath);

        // Les jeux s'inscrivent dans l'une de ces trois listes (programmes 64 bits, 32 bits, et ceux de ton compte).
        foreach ((RegistryHive hive, RegistryView view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64)
        })
        {
            try
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstall = root.OpenSubKey(UninstallKeyPath);

                foreach (string name in uninstall?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using RegistryKey? entry = uninstall!.OpenSubKey(name);

                    if (entry?.GetValue("InstallLocation") is string folder
                        && folder.Trim().Length > 0
                        && CleanFolder(folder) == target
                        && entry.GetValue("UninstallString") is string command
                        && SplitCommand(command) is (string file, string arguments))
                    {
                        return new UninstallPlan(UninstallKind.Uninstaller, file, arguments);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Logger.Log($"Liste des applications de Windows illisible ({hive}, {view}) : {ex.Message}");
            }
        }

        return null;
    }

    // « C:/Jeux/Brawlhalla/ » et « C:\Jeux\Brawlhalla » doivent être égaux (Ubisoft écrit avec des « / »).
    private static string CleanFolder(string path)
    {
        return Path.TrimEndingDirectorySeparator(path.Trim().Trim('"').Replace('/', '\\')).ToLowerInvariant();
    }

    /// <summary>
    /// Sépare le programme de ses paramètres :
    /// « "C:\...\upc.exe" uplay://uninstall/16382 » → (C:\...\upc.exe, uplay://uninstall/16382) ;
    /// « MsiExec.exe /X{06A7…} » → (MsiExec.exe, /X{06A7…}). null si la commande est illisible.
    /// </summary>
    internal static (string File, string Arguments)? SplitCommand(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? (command[1..end], command[(end + 1)..].Trim()) : null;
        }

        // Sans guillemets : le programme s'arrête après « .exe ».
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? (command[..(exe + 4)], command[(exe + 4)..].Trim()) : null;
    }

    // ----- Epic -----

    /// <summary>
    /// Le programme du launcher Epic, lu dans la commande que Windows a enregistrée pour les adresses
    /// com.epicgames.launcher:// (ex. « "C:\Program Files\Epic Games\Launcher\…\EpicGamesLauncher.exe" %1 »).
    /// Lancé sans paramètre, il s'ouvre simplement.
    /// </summary>
    private static string? FindEpicLauncher()
    {
        try
        {
            using RegistryKey? key = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command");

            if (key?.GetValue(null) is string command
                && SplitCommand(command) is (string file, _)
                && File.Exists(file))
            {
                return file;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Logger.Log($"Launcher Epic introuvable : {ex.Message}");
        }

        return null;
    }
}
