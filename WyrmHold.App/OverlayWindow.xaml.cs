using System.Windows;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// L'affichage de l'overlay. Elle ne décide de rien : OverlayController lui dit quoi montrer.
/// </summary>
public partial class OverlayWindow : Window
{
    // Écart avec les bords de l'écran, en pixels.
    private const double ScreenMargin = 16;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    // Windows vient de créer la vraie fenêtre : on la rend « fantôme » (voir GhostWindow).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GhostWindow.Apply(this);
    }

    // Le bas de l'overlay, pour poser les notifications juste en dessous quand il est affiché.
    public double Bottom => Top + ActualHeight;

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
