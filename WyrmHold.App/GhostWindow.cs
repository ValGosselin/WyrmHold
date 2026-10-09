using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WyrmHold.App;

/// <summary>
/// Rend une fenêtre « fantôme » : on la voit, mais la souris et le clavier vont toujours au jeu,
/// qui ne se met donc ni en pause ni en arrière-plan. Sert à l'overlay et aux notifications de succès.
/// À appeler dans OnSourceInitialized : c'est là que Windows a créé la vraie fenêtre (son « handle »).
/// </summary>
public static class GhostWindow
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;   // les clics traversent la fenêtre
    private const int WsExToolWindow = 0x00000080;    // absente d'Alt+Tab
    private const int WsExNoActivate = 0x08000000;    // ne prend jamais le focus, même si on clique dessus

    public static void Apply(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int newValue);
}
