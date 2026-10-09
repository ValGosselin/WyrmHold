using System.Windows;
using System.Windows.Media;

namespace WyrmHold.App;

/// <summary>
/// L'affichage d'une notification de succès. OverlayController décide quoi montrer, où et combien de temps.
/// </summary>
public partial class AchievementToastWindow : Window
{
    // Écart avec le bord droit de l'écran, en pixels (le même que l'overlay).
    private const double ScreenMargin = 16;

    private static readonly Brush GoldBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42));

    // La couleur normale du titre, gardée pour revenir après une notification dorée.
    private readonly Brush _normalTitleBrush;

    // La hauteur demandée par OverlayController (sous l'overlay s'il est affiché).
    private double _top;

    public AchievementToastWindow()
    {
        InitializeComponent();
        _normalTitleBrush = TitleText.Foreground;
    }

    // Windows vient de créer la vraie fenêtre : on la rend « fantôme » (voir GhostWindow).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GhostWindow.Apply(this);
    }

    /// <summary>
    /// Affiche une notification. isGold : titre doré (jeu terminé à 100 %).
    /// </summary>
    public void ShowToast(string title, string name, string description, string? iconUrl, bool isGold, double top)
    {
        TitleText.Text = title;
        TitleText.Foreground = isGold ? GoldBrush : _normalTitleBrush;
        NameText.Text = name;
        DescriptionText.Text = description;
        DescriptionText.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        IconImage.Visibility = string.IsNullOrEmpty(iconUrl) ? Visibility.Collapsed : Visibility.Visible;
        WebImageLoader.SetUrl(IconImage, iconUrl);

        _top = top;
        Show();
        PlaceTopRight();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaceTopRight();
    }

    private void PlaceTopRight()
    {
        Left = SystemParameters.WorkArea.Right - ActualWidth - ScreenMargin;
        Top = _top;
    }
}
