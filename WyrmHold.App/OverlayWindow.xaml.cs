using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// L'affichage de l'overlay. Elle ne décide de rien : OverlayController lui dit quoi montrer.
/// </summary>
public partial class OverlayWindow : Window
{
    // Écart avec les bords de l'écran, en pixels.
    private const double ScreenMargin = 16;

    // Styles Windows « étendus » de la fenêtre (voir OnSourceInitialized).
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;   // les clics traversent la fenêtre
    private const int WsExToolWindow = 0x00000080;    // absente d'Alt+Tab
    private const int WsExNoActivate = 0x08000000;    // ne prend jamais le focus, même si on clique dessus

    public OverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Appelé quand Windows a créé la vraie fenêtre (son « handle ») : c'est le moment de lui
    /// ajouter des styles que WPF ne propose pas. L'overlay devient « fantôme » : on le voit, mais
    /// la souris et le clavier vont toujours au jeu, qui ne se met donc ni en pause ni en arrière-plan.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int newValue);

    /// <summary>
    /// Affiche le jeu en cours, ou « Aucun jeu détecté » si running vaut null.
    /// </summary>
    public void ShowGame(RunningGame? running, string hotkeyText)
    {
        HotkeyHintText.Text = $"{hotkeyText} pour cacher";

        if (running is null)
        {
            GameNameText.Text = "Aucun jeu détecté";
            PlatformText.Text = "Lance un jeu installé : il apparaîtra ici en quelques secondes.";
            GameDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        Game game = running.Game;

        GameNameText.Text = game.Name;
        PlatformText.Text = game.PlatformName;
        GameDetailsPanel.Visibility = Visibility.Visible;
        AchievementsText.Text = FormatAchievements(game);
        UpdateSessionTime(running);
    }

    public void UpdateSessionTime(RunningGame running)
    {
        TimeSpan elapsed = running.Elapsed;

        SessionText.Text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes:00} min"
            : $"{elapsed.Minutes} min {elapsed.Seconds:00} s";
    }

    private static string FormatAchievements(Game game)
    {
        if (!game.HasAchievements)
        {
            return "Pas de succès connus";
        }

        if (game.IsAchievementsComplete)
        {
            return $"100 % ({game.AchievementsTotal}/{game.AchievementsTotal})";
        }

        string remaining = game.MissingAchievements == 1 ? "1 restant" : $"{game.MissingAchievements} restants";
        return $"{remaining} ({game.AchievementsText})";
    }

    // La taille change avec le contenu (nom du jeu sur deux lignes…) : on se recale en haut à droite.
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;

        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Top + ScreenMargin;
    }
}
