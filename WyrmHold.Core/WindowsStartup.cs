using Microsoft.Win32;

namespace Wyrmhold.Core;

/// <summary>
/// « Lancer Wyrmhold au démarrage de Windows » (15 novembre 2026).
/// Windows lance à l'ouverture de session tous les programmes notés dans la clé de registre
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run (celle de l'utilisateur : pas besoin d'être administrateur).
/// Wyrmhold y écrit une valeur « Wyrmhold » = "chemin\WyrmHold.App.exe" --autostart.
///
/// Le Gestionnaire des tâches (onglet « Applications de démarrage ») ne supprime pas cette valeur quand on
/// y désactive un programme : il note « désactivé » dans une autre clé, StartupApproved\Run.
/// Ce n'est pas documenté par Microsoft : d'après plusieurs blogs, le 1er octet vaut 02 (activé) ou 03 (désactivé),
/// et une valeur absente = activé (vu sur le PC de Val le 15 novembre 2026 : 02 pour Steam, Discord…, rien pour Epic).
/// </summary>
public static class WindowsStartup
{
    // L'argument ajouté à la commande : il dit à Wyrmhold qu'il a été lancé par Windows (→ démarrage caché).
    public const string Argument = "--autostart";

    private const string ValueName = "Wyrmhold";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Ce que Windows fera à la prochaine ouverture de session.
    /// </summary>
    public static StartupState GetState()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);

        if (run?.GetValue(ValueName) is not string)
        {
            return StartupState.Off;
        }

        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);

        // 1er octet impair (03, 07…) = désactivé dans le Gestionnaire des tâches.
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1)
        {
            return StartupState.DisabledByWindows;
        }

        return StartupState.On;
    }

    /// <summary>
    /// La commande enregistrée en ce moment (null = aucune). Sert à vérifier qu'elle vise le bon programme.
    /// </summary>
    public static string? GetCommand()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(ValueName) as string;
    }

    /// <summary>
    /// La commande que Windows doit lancer : le programme entre guillemets (le chemin peut contenir des espaces), puis --autostart.
    /// </summary>
    public static string CommandFor(string exePath)
    {
        return $"\"{exePath}\" {Argument}";
    }

    /// <summary>
    /// Inscrit Wyrmhold au démarrage de Windows (ou corrige le chemin). Retire aussi un ancien « désactivé »
    /// du Gestionnaire des tâches : la personne vient de cocher la case, c'est son choix le plus récent.
    /// </summary>
    public static void Enable(string exePath)
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
        }

        ForgetApproval();
    }

    /// <summary>
    /// Retire Wyrmhold du démarrage de Windows (case décochée, ou désinstallation). Sans erreur si rien n'était inscrit.
    /// </summary>
    public static void Disable()
    {
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        ForgetApproval();
    }

    // La note du Gestionnaire des tâches pour Wyrmhold : supprimée (absente = activé).
    private static void ForgetApproval()
    {
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public enum StartupState
{
    Off,                 // rien dans la clé Run
    On,                  // Windows lancera Wyrmhold
    DisabledByWindows    // inscrit, mais désactivé dans le Gestionnaire des tâches
}
