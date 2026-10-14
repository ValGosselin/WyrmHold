using System.Runtime.InteropServices;

namespace WyrmHold.App;

/// <summary>
/// Demande à Windows si un jeu tourne en plein écran exclusif. Dans ce mode, le jeu a l'écran pour lui seul :
/// une fenêtre par-dessus ne s'afficherait pas, ou ferait sortir le jeu du plein écran (Windows le réduit).
/// </summary>
public static class FullScreenDetector
{
    // Valeurs de SHQueryUserNotificationState (énumération QUERY_USER_NOTIFICATION_STATE).
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;

    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const uint MonitorDefaultToNearest = 2;

    public static bool IsExclusiveFullScreen()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) != 0)
            {
                return false;
            }

            if (state == QunsRunningD3dFullScreen)
            {
                return true;
            }

            // Windows 11 répond souvent « occupé » (2) pour un vrai plein écran exclusif (vu le 14 octobre 2026 avec
            // Pony Island, jeu Unity). Or « occupé » vaut aussi pour le fenêtré sans bordure, où l'overlay marche très bien.
            // La différence : en plein écran exclusif, DirectX met la fenêtre du jeu « toujours devant », pile à la taille
            // de l'écran ; une fenêtre sans bordure ne l'est presque jamais (Pony Island en fenêtré : pas « toujours devant »).
            return state == QunsBusy && IsForegroundTopmostFullScreen();
        }
        catch (Exception)
        {
            // En cas de doute, on ne bloque pas l'overlay.
            return false;
        }
    }

    private static bool IsForegroundTopmostFullScreen()
    {
        IntPtr window = GetForegroundWindow();

        if (window == IntPtr.Zero)
        {
            return false;
        }

        // Jamais Wyrmhold lui-même (l'overlay est « toujours devant » et couvre tout l'écran).
        GetWindowThreadProcessId(window, out uint processId);

        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if ((GetWindowLong(window, GwlExStyle) & WsExTopmost) == 0)
        {
            return false;
        }

        MonitorInfo monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetWindowRect(window, out Rect rect)
            || !GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor))
        {
            return false;
        }

        return rect.Left == monitor.Monitor.Left && rect.Top == monitor.Monitor.Top
            && rect.Right == monitor.Monitor.Right && rect.Bottom == monitor.Monitor.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    // Renvoie 0 (S_OK) si la réponse est valable.
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
