using System.Diagnostics;

namespace Wyrmhold.Core;

public static class GameLauncher
{
    public static void Launch(Game game)
    {
        string launchUrl = $"steam://rungameid/{game.PlatformGameId}";
        Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
    }
}