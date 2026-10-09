using System.Runtime.InteropServices;

namespace Wyrmhold.Core;

/// <summary>
/// Ce que Wyrmhold sait de lui-même et du PC : utile dans un rapport de bug.
/// La version vient de Directory.Build.props (à la racine du dépôt).
/// </summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/ValGosselin/WyrmHold";

    // « 0.9.0 » : les trois premiers nombres de la version de l'assembly.
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "inconnue";

    // Windows 11 garde le numéro « 10.0 » : on le reconnaît à son numéro de build (22000 et plus).
    public static string WindowsVersion
    {
        get
        {
            Version os = Environment.OSVersion.Version;
            string name = os.Major == 10 && os.Build >= 22000 ? "Windows 11" : $"Windows {os.Major}.{os.Minor}";
            return $"{name} (build {os.Build}, {RuntimeInformation.OSArchitecture})";
        }
    }

    // Ex. « .NET 10.0.1 ».
    public static string DotNetVersion => RuntimeInformation.FrameworkDescription;
}
